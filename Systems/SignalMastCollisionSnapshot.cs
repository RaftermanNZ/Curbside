using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    internal static class MastCollisionSnapshotPolicy
    {
        internal const int WireLimit = 32;
        internal const int ControlLimit = 8;
        internal const int CandidateLimit = 64;

        internal static bool Finite(Bounds3 bounds)
            => math.all(math.isfinite(bounds.min)) && math.all(math.isfinite(bounds.max))
                && math.all(bounds.min <= bounds.max);

        internal static bool TriangleIndices(int3 indices, int count)
            => count >= 0 && math.all(indices >= 0) && math.all(indices < count);

        // Broad-phase candidates only. Intersection is not proof of the native
        // narrow-phase result, and a distant origin must not exclude a large mesh.
        internal static bool Candidate(Bounds3 a, Bounds3 b, float3 origin, float3 other)
            => Finite(a) && Finite(b) && math.all(math.isfinite(origin)) && math.all(math.isfinite(other))
                && (math.all(a.min.xz <= b.max.xz) && math.all(b.min.xz <= a.max.xz)
                    || math.distancesq(origin.xz, other.xz) <= 144f);

        // Exact footprint predicate used by the installed native area iterator.
        // Eligibility, collision masks and ownership are evaluated separately.
        internal static bool AreaOverlap(ObjectGeometryData geometry, Transform pose, Triangle2 triangle)
            => (geometry.m_Flags & Game.Objects.GeometryFlags.Circular) != 0
                ? MathUtils.Intersect(triangle, new Circle2(geometry.m_Size.x * .5f, pose.m_Position.xz))
                : MathUtils.Intersect(Game.Objects.ObjectUtils.CalculateBaseCorners(pose.m_Position,
                    pose.m_Rotation, geometry.m_Bounds).xz, triangle);
    }

    public partial class FarSideLightSystem
    {
        private struct MastCollisionActor
        {
            internal Entity Actor, Node;
            internal bool Wire;
        }

        private struct MastCollisionObject
        {
            internal Entity Actor, Prefab;
            internal Transform Pose;
            internal Bounds3 Bounds;
            internal bool BroadOverlap;
            internal float Distance;
        }

        // Explicit snapshot only. No update hook, polling, repair, marker
        // removal, native geometry write, or scene mutation is performed here.
        private void RecordMastCollisionSnapshot()
        {
            var manager = EntityManager;
            var prefabs = World.GetExistingSystemManaged<PrefabSystem>();
            var wires = new List<MastCollisionActor>();
            var controls = new List<MastCollisionActor>();
            var seen = new HashSet<Entity>();
            int wireTotal = 0;
            using (var nodes = _upgrades.ToEntityArray(Allocator.Temp))
                foreach (var node in nodes)
                {
                    if (!manager.HasBuffer<Game.Objects.SubObject>(node)) continue;
                    bool wire = manager.HasComponent<MGRSignalStyle>(node)
                        && GardenCityLightPolicy.IsWireStyle(manager.GetComponentData<MGRSignalStyle>(node).Value);
                    foreach (var child in manager.GetBuffer<Game.Objects.SubObject>(node, true))
                    {
                        var actor = child.m_SubObject;
                        if (!Live(actor) || !seen.Add(actor) || !manager.HasComponent<Game.Objects.TrafficLight>(actor)
                            || !manager.HasComponent<Transform>(actor) || !manager.HasComponent<PrefabRef>(actor)) continue;
                        if (wire)
                        {
                            var prefab = manager.GetComponentData<PrefabRef>(actor).m_Prefab;
                            if (!prefabs.TryGetPrefab<StaticObjectPrefab>(prefab, out var model) || model.m_Meshes == null) continue;
                            bool pole = false;
                            foreach (var part in model.m_Meshes)
                                if (part?.m_Mesh != null && SignalMastSnapshotPolicy.Pole(part.m_Mesh.name)) pole = true;
                            if (!pole) continue;
                            wireTotal++;
                            if (wires.Count < MastCollisionSnapshotPolicy.WireLimit)
                                wires.Add(new MastCollisionActor { Actor = actor, Node = node, Wire = true });
                        }
                        else controls.Add(new MastCollisionActor { Actor = actor, Node = node });
                    }
                }
            Mod.Log.Info("Mast collision scope: wireTotal=" + wireTotal + " sampled=" + wires.Count
                + " wireLimit=" + MastCollisionSnapshotPolicy.WireLimit + "; snapshot-only broad-phase evidence, not a collision verdict.");
            if (wires.Count == 0) return;

            // Select nearby non-wire controls deterministically, rather than
            // equating absence from the wire-only probe with healthy rendering.
            controls.Sort((a, b) => CompareMastEntity(a.Actor, b.Actor));
            int controlCount = 0;
            foreach (var control in controls)
            {
                var pose = manager.GetComponentData<Transform>(control.Actor);
                bool nearby = false;
                for (int i = 0; i < wires.Count; i++)
                    if (wires[i].Wire && math.distancesq(pose.m_Position,
                        manager.GetComponentData<Transform>(wires[i].Actor).m_Position) <= 16384f) nearby = true;
                if (!nearby) continue;
                wires.Add(control);
                if (++controlCount == MastCollisionSnapshotPolicy.ControlLimit) break;
            }
            Mod.Log.Info("Mast collision controls: nearbyNonWire=" + controlCount + " limit=" + MastCollisionSnapshotPolicy.ControlLimit);

            var targetBounds = new Bounds3[wires.Count];
            var targetPositions = new float3[wires.Count];
            var targetValid = new bool[wires.Count];
            for (int i = 0; i < wires.Count; i++)
            {
                var actor = wires[i].Actor;
                var prefab = manager.GetComponentData<PrefabRef>(actor).m_Prefab;
                if (!manager.HasComponent<ObjectGeometryData>(prefab)) continue;
                var pose = manager.GetComponentData<Transform>(actor);
                targetBounds[i] = Game.Objects.ObjectUtils.CalculateBounds(pose.m_Position, pose.m_Rotation,
                    manager.GetComponentData<ObjectGeometryData>(prefab));
                targetPositions[i] = pose.m_Position;
                targetValid[i] = MastCollisionSnapshotPolicy.Finite(targetBounds[i]);
            }
            var objects = new List<MastCollisionObject>();
            var query = manager.CreateEntityQuery(ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            try
            {
                using (var actors = query.ToEntityArray(Allocator.Temp))
                    foreach (var actor in actors)
                    {
                        var prefab = manager.GetComponentData<PrefabRef>(actor).m_Prefab;
                        if (!manager.HasComponent<ObjectGeometryData>(prefab)) continue;
                        var pose = manager.GetComponentData<Transform>(actor);
                        var geometry = manager.GetComponentData<ObjectGeometryData>(prefab);
                        var bounds = Game.Objects.ObjectUtils.CalculateBounds(pose.m_Position, pose.m_Rotation, geometry);
                        if (!MastCollisionSnapshotPolicy.Finite(bounds)) continue;
                        bool relevant = false;
                        for (int i = 0; i < wires.Count; i++)
                            if (targetValid[i] && MastCollisionSnapshotPolicy.Candidate(targetBounds[i], bounds, targetPositions[i], pose.m_Position))
                            { relevant = true; break; }
                        if (!relevant) continue;
                        objects.Add(new MastCollisionObject { Actor = actor, Prefab = prefab, Pose = pose, Bounds = bounds });
                    }
            }
            finally { query.Dispose(); }

            foreach (var target in wires)
            {
                var actor = target.Actor;
                var currentPrefab = manager.GetComponentData<PrefabRef>(actor).m_Prefab;
                var current = manager.GetComponentData<Transform>(actor);
                var originalPrefab = OriginalPrefab(actor);
                var original = OriginalTransform(actor);
                Mod.Log.Info("Mast collision target: actor=" + actor + " node=" + target.Node + " wire=" + target.Wire
                    + " tracked=" + _moved.ContainsKey(actor) + " nativeOverridden=" + manager.HasComponent<Overridden>(actor)
                    + " secondary=" + manager.HasComponent<Game.Objects.Secondary>(actor) + " owners=" + MastOwnerChain(actor));
                RecordMastCollisionGeometry(actor, "applied", currentPrefab, current);
                RecordMastCollisionGeometry(actor, "original", originalPrefab, original);
                if (!manager.HasComponent<ObjectGeometryData>(currentPrefab)) continue;
                var shape = manager.GetComponentData<ObjectGeometryData>(currentPrefab);
                var bounds = Game.Objects.ObjectUtils.CalculateBounds(current.m_Position, current.m_Rotation, shape);
                var candidates = new List<MastCollisionObject>();
                foreach (var item in objects)
                {
                    if (item.Actor == actor || !MastCollisionSnapshotPolicy.Candidate(bounds, item.Bounds, current.m_Position, item.Pose.m_Position)) continue;
                    var candidate = item;
                    candidate.BroadOverlap = math.all(bounds.min.xz <= item.Bounds.max.xz) && math.all(item.Bounds.min.xz <= bounds.max.xz);
                    candidate.Distance = math.distancesq(current.m_Position.xz, item.Pose.m_Position.xz);
                    candidates.Add(candidate);
                }
                candidates.Sort((a, b) => a.BroadOverlap != b.BroadOverlap ? (a.BroadOverlap ? -1 : 1)
                    : a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : CompareMastEntity(a.Actor, b.Actor));
                int count = math.min(candidates.Count, MastCollisionSnapshotPolicy.CandidateLimit);
                for (int i = 0; i < count; i++)
                {
                    var item = candidates[i];
                    Mod.Log.Info("Mast collision candidate: target=" + actor + " actor=" + item.Actor
                        + " groundBoundsOverlap=" + item.BroadOverlap + " groundDistanceSquared=" + item.Distance + " owners=" + MastOwnerChain(item.Actor));
                    RecordMastCollisionGeometry(item.Actor, "candidate", item.Prefab, item.Pose);
                }
                Mod.Log.Info("Mast collision candidate count: target=" + actor + " total=" + candidates.Count
                    + " reported=" + count + " truncated=" + (candidates.Count > count));
            }
            RecordMastCollisionAreas(wires);
            RecordMastCollisionRoads(wires);
        }

        private Entity MastCollisionRoot(Entity actor, bool area, out bool complete)
        {
            var seen = new HashSet<Entity>();
            var at = actor;
            complete = false;
            for (int depth = 0; depth < 32; depth++)
            {
                if (!EntityManager.Exists(at) || !seen.Add(at)) return at;
                if (EntityManager.HasComponent<Game.Buildings.Building>(at) || !EntityManager.HasComponent<Owner>(at))
                { complete = true; break; }
                at = EntityManager.GetComponentData<Owner>(at).m_Owner;
            }
            if (!complete || !area) return at;
            seen.Clear();
            for (int depth = 0; depth < 32; depth++)
            {
                if (!seen.Add(at)) { complete = false; return at; }
                if (!EntityManager.HasComponent<Owner>(at)) return at;
                var parent = EntityManager.GetComponentData<Owner>(at).m_Owner;
                if (!EntityManager.Exists(parent) || !EntityManager.HasBuffer<Game.Areas.Node>(parent)) return at;
                at = parent;
            }
            complete = false;
            return at;
        }

        private void RecordMastCollisionAreas(List<MastCollisionActor> targets)
        {
            var manager = EntityManager;
            var query = manager.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.Geometry>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Areas.Node>(), ComponentType.ReadOnly<Game.Areas.Triangle>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            try
            {
                using (var areas = query.ToEntityArray(Allocator.Temp))
                    foreach (var target in targets)
                    {
                        var actor = target.Actor;
                        var currentPrefab = manager.GetComponentData<PrefabRef>(actor).m_Prefab;
                        var nativePrefab = OriginalPrefab(actor);
                        if (!manager.HasComponent<ObjectGeometryData>(currentPrefab) || !manager.HasComponent<ObjectGeometryData>(nativePrefab)) continue;
                        var currentGeometry = manager.GetComponentData<ObjectGeometryData>(currentPrefab);
                        var nativeGeometry = manager.GetComponentData<ObjectGeometryData>(nativePrefab);
                        var current = manager.GetComponentData<Transform>(actor);
                        var original = OriginalTransform(actor);
                        var currentBounds = Game.Objects.ObjectUtils.CalculateBounds(current.m_Position, current.m_Rotation, currentGeometry);
                        var originalBounds = Game.Objects.ObjectUtils.CalculateBounds(original.m_Position, original.m_Rotation, nativeGeometry);
                        var root = MastCollisionRoot(actor, false, out var rootComplete);
                        var areaRoot = MastCollisionRoot(actor, true, out var areaRootComplete);
                        bool ignoreMarkers = (Game.SceneFlow.GameManager.instance.gameMode & Game.GameMode.Editor) == 0 || manager.HasComponent<Owner>(actor);
                        var mask = manager.HasComponent<Game.Objects.Elevation>(actor)
                            ? Game.Objects.ObjectUtils.GetCollisionMask(currentGeometry, manager.GetComponentData<Game.Objects.Elevation>(actor), ignoreMarkers)
                            : Game.Objects.ObjectUtils.GetCollisionMask(currentGeometry, ignoreMarkers);
                        int reported = 0, omitted = 0;
                        foreach (var area in areas)
                        {
                            var areaBounds = manager.GetComponentData<Game.Areas.Geometry>(area).m_Bounds;
                            bool nearCurrent = math.all(currentBounds.min.xz <= areaBounds.max.xz) && math.all(areaBounds.min.xz <= currentBounds.max.xz);
                            bool nearOriginal = math.all(originalBounds.min.xz <= areaBounds.max.xz) && math.all(areaBounds.min.xz <= originalBounds.max.xz);
                            if (!nearCurrent && !nearOriginal) continue;
                            var prefab = manager.GetComponentData<PrefabRef>(area).m_Prefab;
                            if (!manager.HasComponent<AreaGeometryData>(prefab)) continue;
                            var geometry = manager.GetComponentData<AreaGeometryData>(prefab);
                            if ((geometry.m_Flags & Game.Areas.GeometryFlags.CanOverrideObjects) == 0) continue;
                            var otherRoot = MastCollisionRoot(area, false, out var otherComplete);
                            bool ownerExcluded = area == areaRoot || otherRoot == areaRoot || otherRoot == root;
                            var areaMask = Game.Areas.AreaUtils.GetCollisionMask(geometry);
                            bool appliedHit = false, nativeAtAppliedHit = false, customAtOriginalHit = false, nativeHit = false;
                            var nodes = manager.GetBuffer<Game.Areas.Node>(area, true);
                            var triangles = manager.GetBuffer<Game.Areas.Triangle>(area, true);
                            int firstApplied = -1, invalidTriangles = 0;
                            for (int i = 0; i < triangles.Length; i++)
                            {
                                if (!MastCollisionSnapshotPolicy.TriangleIndices(triangles[i].m_Indices, nodes.Length))
                                { invalidTriangles++; continue; }
                                var triangle = Game.Areas.AreaUtils.GetTriangle3(nodes, triangles[i]).xz;
                                if (!math.all(math.isfinite(triangle.a)) || !math.all(math.isfinite(triangle.b)) || !math.all(math.isfinite(triangle.c)))
                                { invalidTriangles++; continue; }
                                bool hit = MastCollisionSnapshotPolicy.AreaOverlap(currentGeometry, current, triangle);
                                if (hit && firstApplied < 0) firstApplied = i;
                                appliedHit |= hit;
                                nativeAtAppliedHit |= MastCollisionSnapshotPolicy.AreaOverlap(nativeGeometry, current, triangle);
                                customAtOriginalHit |= MastCollisionSnapshotPolicy.AreaOverlap(currentGeometry, original, triangle);
                                nativeHit |= MastCollisionSnapshotPolicy.AreaOverlap(nativeGeometry, original, triangle);
                            }
                            if (reported++ >= MastCollisionSnapshotPolicy.CandidateLimit) { omitted++; continue; }
                            string name = World.GetExistingSystemManaged<PrefabSystem>().TryGetPrefab<PrefabBase>(prefab, out var definition) ? definition.name : "unresolved";
                            Mod.Log.Info("Mast collision area: actor=" + actor + " area=" + area + " name=" + name + " owners=" + MastOwnerChain(area)
                                + " flags=" + geometry.m_Flags + " areaMask=" + areaMask + " targetMask=" + mask + " masksOverlap=" + ((mask & areaMask) != 0)
                                + " ownerExcluded=" + ownerExcluded + " ownerResolutionComplete=" + (rootComplete && areaRootComplete && otherComplete)
                                + " appliedHit=" + appliedHit + " nativeAtAppliedHit=" + nativeAtAppliedHit + " customAtOriginalHit=" + customAtOriginalHit
                                + " nativeHit=" + nativeHit + " firstAppliedTriangle=" + firstApplied + " invalidTriangles=" + invalidTriangles + " boundsMin=" + areaBounds.min + " boundsMax=" + areaBounds.max
                                + "; current area predicate only, not the historical tag writer.");
                        }
                        Mod.Log.Info("Mast collision area count: actor=" + actor + " total=" + reported + " omitted=" + omitted);
                    }
            }
            finally { query.Dispose(); }
        }

        private void RecordMastCollisionRoads(List<MastCollisionActor> targets)
        {
            var manager = EntityManager;
            var query = manager.CreateEntityQuery(ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.EdgeGeometry>(),
                ComponentType.ReadOnly<Game.Net.Composition>(), ComponentType.ReadOnly<Game.Net.Curve>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            try
            {
                using (var edges = query.ToEntityArray(Allocator.Temp))
                    foreach (var target in targets)
                    {
                        var position = manager.GetComponentData<Transform>(target.Actor).m_Position;
                        int reported = 0;
                        foreach (var road in edges)
                        {
                            var curve = manager.GetComponentData<Game.Net.Curve>(road).m_Bezier;
                            var min = math.min(math.min(curve.a, curve.b), math.min(curve.c, curve.d));
                            var max = math.max(math.max(curve.a, curve.b), math.max(curve.c, curve.d));
                            var nearest = math.clamp(position, min, max);
                            if (math.distancesq(position.xz, nearest.xz) > 4096f) continue;
                            var edge = manager.GetComponentData<Game.Net.Edge>(road);
                            var composition = manager.GetComponentData<Game.Net.Composition>(road);
                            if (reported++ >= MastCollisionSnapshotPolicy.CandidateLimit) continue;
                            Mod.Log.Info("Mast collision road: actor=" + target.Actor + " road=" + road + " start=" + edge.m_Start + " end=" + edge.m_End
                                + " connectedToSignalNode=" + (edge.m_Start == target.Node || edge.m_End == target.Node) + " owners=" + MastOwnerChain(road));
                            foreach (var prefab in new[] { composition.m_Edge, composition.m_StartNode, composition.m_EndNode })
                                if (manager.HasComponent<NetCompositionData>(prefab))
                                {
                                    var data = manager.GetComponentData<NetCompositionData>(prefab);
                                    Mod.Log.Info("Mast collision road composition: road=" + road + " prefab=" + prefab + " width=" + data.m_Width
                                        + " flags=" + data.m_Flags + " mask=" + Game.Net.NetUtils.GetCollisionMask(data, true));
                                }
                            var geometry = manager.GetComponentData<Game.Net.EdgeGeometry>(road);
                            RecordMastCollisionCurve(road, "start-left", geometry.m_Start.m_Left);
                            RecordMastCollisionCurve(road, "start-right", geometry.m_Start.m_Right);
                            RecordMastCollisionCurve(road, "end-left", geometry.m_End.m_Left);
                            RecordMastCollisionCurve(road, "end-right", geometry.m_End.m_Right);
                        }
                        Mod.Log.Info("Mast collision road count: actor=" + target.Actor + " total=" + reported
                            + " omitted=" + math.max(0, reported - MastCollisionSnapshotPolicy.CandidateLimit));
                    }
            }
            finally { query.Dispose(); }
        }

        private static void RecordMastCollisionCurve(Entity road, string role, Bezier4x3 curve)
            => Mod.Log.Info("Mast collision road curve: road=" + road + " role=" + role
                + " a=" + curve.a + " b=" + curve.b + " c=" + curve.c + " d=" + curve.d);

        private static int CompareMastEntity(Entity a, Entity b)
            => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Version.CompareTo(b.Version);

        private string MastOwnerChain(Entity actor)
        {
            var visited = new HashSet<Entity>();
            string result = "";
            var at = actor;
            for (int depth = 0; depth < 32; depth++)
            {
                if (!visited.Add(at)) return result + "[cycle]";
                result += (depth == 0 ? "" : ">") + at;
                if (!EntityManager.Exists(at)) return result + "[missing]";
                if (EntityManager.HasComponent<Overridden>(at)) result += "[overridden]";
                if (EntityManager.HasComponent<Game.Buildings.Building>(at)) result += "[building]";
                if (!EntityManager.HasComponent<Owner>(at)) return result;
                at = EntityManager.GetComponentData<Owner>(at).m_Owner;
            }
            return result + "[depth-limit]";
        }

        private void RecordMastCollisionGeometry(Entity actor, string role, Entity prefab, Transform pose)
        {
            var manager = EntityManager;
            if (!manager.Exists(prefab) || !manager.HasComponent<ObjectGeometryData>(prefab))
            {
                Mod.Log.Info("Mast collision geometry unavailable: actor=" + actor + " role=" + role + " prefab=" + prefab);
                return;
            }
            var geometry = manager.GetComponentData<ObjectGeometryData>(prefab);
            var prefabs = World.GetExistingSystemManaged<PrefabSystem>();
            string name = prefabs.TryGetPrefab<PrefabBase>(prefab, out var model) ? model.name : "unresolved";
            bool ignoreMarkers = (Game.SceneFlow.GameManager.instance.gameMode & Game.GameMode.Editor) == 0 || manager.HasComponent<Owner>(actor);
            bool hasElevation = manager.HasComponent<Game.Objects.Elevation>(actor);
            var mask = hasElevation ? Game.Objects.ObjectUtils.GetCollisionMask(geometry, manager.GetComponentData<Game.Objects.Elevation>(actor), ignoreMarkers)
                : Game.Objects.ObjectUtils.GetCollisionMask(geometry, ignoreMarkers);
            var bounds = Game.Objects.ObjectUtils.CalculateBounds(pose.m_Position, pose.m_Rotation, geometry);
            Mod.Log.Info("Mast collision geometry: actor=" + actor + " role=" + role + " prefab=" + prefab + " name=" + name
                + " flags=" + geometry.m_Flags + " collisionMask=" + mask + " boundsMin=" + geometry.m_Bounds.min + " boundsMax=" + geometry.m_Bounds.max
                + " size=" + geometry.m_Size + " legSize=" + geometry.m_LegSize + " legOffset=" + geometry.m_LegOffset
                + " position=" + pose.m_Position + " rotation=" + pose.m_Rotation + " worldMin=" + bounds.min + " worldMax=" + bounds.max
                + " elevation=" + (hasElevation ? manager.GetComponentData<Game.Objects.Elevation>(actor).m_Elevation.ToString() : "none")
                + " stack=" + manager.HasComponent<Game.Objects.Stack>(actor) + " moving=" + manager.HasBuffer<Game.Objects.TransformFrame>(actor)
                + " secondary=" + manager.HasComponent<Game.Objects.Secondary>(actor) + " toolOverride=" + manager.HasComponent<Game.Tools.Override>(actor)
                + " nativeOverridden=" + manager.HasComponent<Overridden>(actor) + " hidden=" + manager.HasComponent<Game.Tools.Hidden>(actor));
            if (model is StaticObjectPrefab definition)
            {
                bool standing = definition.TryGet<StandingObject>(out var legs);
                Mod.Log.Info("Mast collision definition: actor=" + actor + " role=" + role + " name=" + name
                    + " circular=" + definition.m_Circular + " standing=" + standing
                    + (standing ? " legSize=" + legs.m_LegSize + " legGap=" + legs.m_LegGap + " circularLeg=" + legs.m_CircularLeg : ""));
            }
        }
    }
}
