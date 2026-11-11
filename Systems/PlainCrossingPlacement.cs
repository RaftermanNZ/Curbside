using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using ObjectLight = Game.Objects.TrafficLight;
using ObjectTransform = Game.Objects.Transform;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private struct PlainCrossingChange
        {
            internal Entity Actor, Prefab;
            internal ObjectTransform Original, Placed;
            internal ushort VehicleMask, PedestrianMask;
            internal bool Valid;
        }

        private void ApplyPlainCrossingPlan(Entity node, IList<float3> surface)
        {
            var candidates = new List<Entity>();
            foreach (var actor in _props)
            {
                if (!SignalProp(actor, node) || EntityManager.HasComponent<Game.Tools.Temp>(actor)) continue;
                var native = OriginalPrefab(actor);
                if (!EntityManager.HasComponent<TrafficLightData>(native)) continue;
                var role = EntityManager.GetComponentData<TrafficLightData>(native).m_Type;
                if (!FarSideLightPolicy.Vehicle(role)
                    && (role & (TrafficLightType.CrossingLeft | TrafficLightType.CrossingRight)) != 0) candidates.Add(actor);
            }
            if (candidates.Count == 0) return;
            var crossings = new List<PlainCrossingPlacementPolicy.Crossing>();
            if (EntityManager.HasBuffer<Game.Net.SubLane>(node))
                foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node, true))
                {
                    var lane = entry.m_SubLane;
                    if (!Live(lane) || !EntityManager.HasComponent<PedestrianLane>(lane)
                        || !EntityManager.HasComponent<LaneSignal>(lane) || !EntityManager.HasComponent<Curve>(lane)
                        || (EntityManager.GetComponentData<PedestrianLane>(lane).m_Flags & PedestrianLaneFlags.Crosswalk) == 0) continue;
                    var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    crossings.Add(new PlainCrossingPlacementPolicy.Crossing
                    {
                        Lane = lane, A = curve.a, B = curve.d,
                        FacingA = math.normalizesafe(curve.b.xz - curve.a.xz, math.normalizesafe(curve.d.xz - curve.a.xz)),
                        FacingB = math.normalizesafe(curve.c.xz - curve.d.xz, math.normalizesafe(curve.a.xz - curve.d.xz)),
                        Mask = EntityManager.GetComponentData<LaneSignal>(lane).m_GroupMask
                    });
                }

            var supports = new List<PlainCrossingPlacementPolicy.Support>();
            // Use this junction's native children, never the city-wide moved map.
            foreach (var actor in _props)
            {
                if (!_moved.TryGetValue(actor, out var moved) || moved.Node != node || !SignalProp(actor, node)
                    || EntityManager.HasComponent<Game.Tools.Temp>(actor)
                    || EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab != moved.AppliedPrefab
                    || !EntityManager.HasComponent<TrafficLightData>(moved.Prefab)
                    || !FarSideLightPolicy.Vehicle(EntityManager.GetComponentData<TrafficLightData>(moved.Prefab).m_Type)
                    || !EntityManager.GetComponentData<ObjectTransform>(actor).Equals(moved.Applied)
                    || math.distancesq(moved.Original.m_Position, moved.Applied.m_Position) < .0001f) continue;
                supports.Add(new PlainCrossingPlacementPolicy.Support
                { Actor = actor, Position = moved.Applied.m_Position, Radius = PlainPoleRadius(moved.Prefab) });
            }

            var changes = new List<PlainCrossingChange>();
            foreach (var actor in candidates)
            {
                var native = OriginalPrefab(actor);
                var current = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
                if (_moved.TryGetValue(actor, out var previous)
                    && current != previous.Prefab && current != previous.AppliedPrefab) continue;
                if (_moved.TryGetValue(actor, out previous) && previous.AppliedPrefab != previous.Prefab)
                    Undo(actor, previous); // Native masks/model restored by the existing ownership-aware path.
                var original = OriginalTransform(actor);
                var binding = EntityManager.GetComponentData<ObjectLight>(actor);
                // Return custom models/masks to the actor's native plain style.
                ushort mask = _moved.TryGetValue(actor, out previous) && binding.m_GroupMask1 == previous.AppliedPedMask
                    ? previous.OriginalPedMask : binding.m_GroupMask1;
                bool valid = PlainCrossingPlacementPolicy.TryPlan(original, mask, crossings, supports,
                    PlainPoleRadius(native), out var plan);
                if (valid)
                {
                    valid = FarSideLightPolicy.TryGround(original.m_Position, plan.Pose.m_Position, surface, out var height);
                    if (valid) plan.Pose.m_Position.y = height;
                }
                changes.Add(new PlainCrossingChange { Actor = actor, Prefab = native,
                    Original = original, Placed = plan.Pose, VehicleMask = binding.m_GroupMask0,
                    PedestrianMask = mask, Valid = valid });
            }
            // Reject both sides of a collision instead of claiming one actor
            // according to native SubObject enumeration order.
            var collisions = new bool[changes.Count];
            for (int i = 0; i < changes.Count; i++)
            for (int j = i + 1; j < changes.Count; j++)
                if (changes[i].Valid && changes[j].Valid
                    && math.distancesq(changes[i].Placed.m_Position, changes[j].Placed.m_Position) < .16f)
                {
                    collisions[i] = collisions[j] = true;
                }
            for (int index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                if (collisions[index]) change.Valid = false;
                if (!change.Valid)
                {
                    if (_moved.TryGetValue(change.Actor, out var previous)) { Undo(change.Actor, previous); _moved.Remove(change.Actor); }
                    continue;
                }
                var current = EntityManager.GetComponentData<ObjectTransform>(change.Actor);
                var prefab = EntityManager.GetComponentData<PrefabRef>(change.Actor).m_Prefab;
                if (prefab == change.Prefab && current.Equals(change.Placed)
                    && _moved.TryGetValue(change.Actor, out var existing) && existing.Applied.Equals(change.Placed)) continue;
                ReleaseHiddenSignal(change.Actor);
                if (prefab != change.Prefab) SetPreviewPrefab(change.Actor, change.Prefab);
                // Plain relocation does not write phase membership or state.
                _moved[change.Actor] = new Moved { Node = node, Owner = node, Prefab = change.Prefab,
                    AppliedPrefab = change.Prefab, Original = change.Original, Applied = change.Placed,
                    OriginalPedMask = change.PedestrianMask, AppliedPedMask = change.PedestrianMask,
                    AppliedVehicleMask = change.VehicleMask };
                Write(change.Actor, change.Placed);
            }
        }

        private float PlainPoleRadius(Entity prefab)
        {
            if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) return .25f;
            var size = math.abs(EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Size);
            return math.all(math.isfinite(size)) ? math.clamp(math.min(size.x, size.z) * .5f, .1f, .5f) : .25f;
        }
    }
}
