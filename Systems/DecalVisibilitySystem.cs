using System;

using System.Collections.Generic;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Serialization;

using Game.Tools;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    public struct RDCHiddenDecal : IComponentData, IEmptySerializable { internal Entity Road, Prefab, Owner; internal byte Kind; }

    public partial class DecalVisibilitySystem : GameSystemBase, IPreSerialize

    {

        private EntityQuery _roads, _changedRoads, _changedNodes, _hidden;

        private PrefabSystem _prefabs; private bool _active, _initial = true;



        private readonly Dictionary<Entity, byte> _kinds = new Dictionary<Entity, byte>();

        private static readonly HashSet<Entity> Requested = new HashSet<Entity>();

        private readonly HashSet<Entity> _pending = new HashSet<Entity>(), _deferred = new HashSet<Entity>();

        internal static void QueueRoad(Entity road) { Requested.Add(road); }

        protected override void OnCreate()

        {

            base.OnCreate(); _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();

            _roads = GetEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<RDCUpgrade>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());

            _changedRoads = GetEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<RDCUpgrade>(), ComponentType.ReadOnly<Game.Objects.SubObject>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());

            _changedRoads.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<Game.Objects.SubObject>(), ComponentType.ReadOnly<PrefabRef>() });

            _changedNodes = GetEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.ReadOnly<Game.Objects.SubObject>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());

            _changedNodes.SetChangedVersionFilter(ComponentType.ReadOnly<Game.Objects.SubObject>());

            _hidden = GetEntityQuery(ComponentType.ReadOnly<RDCHiddenDecal>());

        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _active = false; Restore(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode) { base.OnGameLoadingComplete(purpose, mode); Restore(); Requested.Clear(); _kinds.Clear(); _active = (mode & GameMode.GameOrEditor) != GameMode.None; }

        private bool Live(Entity e) => EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e) && !EntityManager.HasComponent<Temp>(e);

        private bool PureDecal(Entity prefab)

        {

            if (!_prefabs.TryGetPrefab<ObjectGeometryPrefab>(prefab, out var definition) || definition?.m_Meshes == null) return false;

            bool any = false;

            foreach (var part in definition.m_Meshes) { if (part?.m_Mesh == null) continue; if (!part.m_Mesh.Has<DecalProperties>()) return false; any = true; }

            return any;

        }

        private byte Kind(Entity prefab)

        {

            if (_kinds.TryGetValue(prefab, out byte kind)) return kind;

            bool speed = EntityManager.HasComponent<TrafficSignData>(prefab) && (EntityManager.GetComponentData<TrafficSignData>(prefab).m_TypeMask & TrafficSignData.GetTypeMask(TrafficSignType.SpeedLimit)) != 0;

            string name = _prefabs.TryGetPrefab<PrefabBase>(prefab, out var definition) ? definition?.name : null;

            kind = DecalPolicy.Classify(PureDecal(prefab), EntityManager.HasComponent<LaneDirectionData>(prefab), speed, name); _kinds[prefab] = kind;

            if (kind != 0) Mod.Debug("Supported ground decal: " + name + "; kind=" + kind);

            return kind;

        }

        private Entity RoadFor(Entity obj)

        {

            if (!EntityManager.HasComponent<Owner>(obj)) return Entity.Null;

            var owner = EntityManager.GetComponentData<Owner>(obj).m_Owner;

            for (int i = 0; i < 8 && Live(owner); i++)

            {

                if (EntityManager.HasComponent<Road>(owner)) return owner;

                if (EntityManager.HasComponent<Node>(owner) && EntityManager.HasBuffer<ConnectedEdge>(owner) && EntityManager.HasComponent<Game.Objects.Transform>(obj))

                {

                    var transform = EntityManager.GetComponentData<Game.Objects.Transform>(obj); var forward = math.rotate(transform.m_Rotation, new float3(0, 0, 1)).xz;

                    float best = float.MaxValue; Entity road = Entity.Null;

                    foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(owner, true)) if (Live(entry.m_Edge) && EntityManager.HasComponent<Road>(entry.m_Edge) && EntityManager.HasComponent<Curve>(entry.m_Edge))

                        {

                            var curve = EntityManager.GetComponentData<Curve>(entry.m_Edge).m_Bezier; float distance = MathUtils.Distance(curve, transform.m_Position, out float t);

                            float score = distance + 20f * (1f - math.abs(math.dot(math.normalizesafe(forward), math.normalizesafe(MathUtils.Tangent(curve, t).xz))));

                            if (score < best) { best = score; road = entry.m_Edge; }

                        }

                    return road;

                }

                // A decal nested under a physical sign is not a pavement marking.

                if (EntityManager.HasComponent<Game.Objects.ObjectGeometry>(owner) && (!EntityManager.HasComponent<PrefabRef>(owner) || !PureDecal(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab))) return Entity.Null;

                if (!EntityManager.HasComponent<Owner>(owner)) return Entity.Null; owner = EntityManager.GetComponentData<Owner>(owner).m_Owner;

            }

            return Entity.Null;

        }

        private bool ShouldHide(Entity obj, out Entity road)

        {

            road = Entity.Null;

            if (!Live(obj) || !EntityManager.HasComponent<Game.Objects.ObjectGeometry>(obj) || !EntityManager.HasComponent<PrefabRef>(obj)) return false;

            byte kind = Kind(EntityManager.GetComponentData<PrefabRef>(obj).m_Prefab); if (kind == 0) return false;

            road = RoadFor(obj); return Live(road) && EntityManager.HasComponent<RDCUpgrade>(road) && (RoadSideUpgrades.HiddenDecals(EntityManager, road, obj) & kind) != 0;

        }

        private bool ParentHidden(Entity obj) => EntityManager.HasComponent<Owner>(obj) && EntityManager.HasComponent<Hidden>(EntityManager.GetComponentData<Owner>(obj).m_Owner);

        private void RenderChanged(Entity obj) { if (Live(obj) && !EntityManager.HasComponent<BatchesUpdated>(obj)) EntityManager.AddComponent<BatchesUpdated>(obj); }

        private void Release(Entity obj)

        {

            if (!EntityManager.Exists(obj)) return;

            if (EntityManager.HasComponent<Hidden>(obj) && !ParentHidden(obj)) EntityManager.RemoveComponent<Hidden>(obj);

            EntityManager.RemoveComponent<RDCHiddenDecal>(obj); RenderChanged(obj);



        }

        private void Inspect(Entity obj)

        {

            if (!Live(obj)) return;

            if (ShouldHide(obj, out var road))

            {

                if (ParentHidden(obj)) { _deferred.Add(road); return; }

                if (EntityManager.HasComponent<RDCHiddenDecal>(obj)) return;

                if (EntityManager.HasComponent<Hidden>(obj)) return; // Never take ownership of another system's hidden flag.

                var prefab = EntityManager.GetComponentData<PrefabRef>(obj).m_Prefab;

                EntityManager.AddComponentData(obj, new RDCHiddenDecal { Road = road, Prefab = prefab, Owner = EntityManager.GetComponentData<Owner>(obj).m_Owner, Kind = Kind(prefab) });

                EntityManager.AddComponent<Hidden>(obj); RenderChanged(obj);

            }

            else if (EntityManager.HasComponent<RDCHiddenDecal>(obj)) Release(obj);

        }

        private void InspectChildren(Entity owner, int depth)

        {

            if (depth > 4 || !Live(owner) || !EntityManager.HasBuffer<Game.Objects.SubObject>(owner)) return;

            // Structural changes invalidate a DynamicBuffer, so snapshot links first.

            var children = new List<Entity>(); foreach (var link in EntityManager.GetBuffer<Game.Objects.SubObject>(owner, true)) children.Add(link.m_SubObject);

            foreach (var child in children) { Inspect(child); InspectChildren(child, depth + 1); }

        }

        internal void Restore() { Dependency.Complete(); using (var owned = _hidden.ToEntityArray(Allocator.Temp)) foreach (var obj in owned) Release(obj); _initial = true; _deferred.Clear(); }

        public void PreSerialize(Context context) { Restore(); }

        protected override void OnUpdate()
        {long started=ModDiagnostics.RuntimeCosts.Begin();try{UpdateMeasuredCore();}finally{ModDiagnostics.RuntimeCosts.End("DecalVisibilitySystem",started);}}
        private void UpdateMeasuredCore()

        {

            if (Mod.Saving) return;

            if (!_active) return;

            try

            {

                Dependency.Complete(); bool explicitRequest = Requested.Count > 0; _pending.Clear(); foreach (var road in Requested) _pending.Add(road); Requested.Clear();

                if (!explicitRequest && _roads.IsEmpty && _hidden.IsEmpty) { _deferred.Clear(); _initial = false; return; }

                if (_initial) using (var roads = _roads.ToEntityArray(Allocator.Temp)) foreach (var road in roads) _pending.Add(road);

                using (var roads = _changedRoads.ToEntityArray(Allocator.Temp)) foreach (var road in roads) _pending.Add(road);

                using (var nodes = _changedNodes.ToEntityArray(Allocator.Temp)) foreach (var node in nodes) foreach (var link in EntityManager.GetBuffer<ConnectedEdge>(node, true)) if (EntityManager.HasComponent<RDCUpgrade>(link.m_Edge)) _pending.Add(link.m_Edge);

                int budget = 4; foreach (var road in _deferred) { if (budget-- <= 0) break; _pending.Add(road); }

                foreach (var road in _pending) _deferred.Remove(road);

                // Repair only flags owned by this mod after native preview visibility updates.

                using (var repairs = new EntityCommandBuffer(Allocator.Temp))

                {

                    using (var owned = _hidden.ToEntityArray(Allocator.Temp)) foreach (var obj in owned)

                        {

                            var marker = EntityManager.GetComponentData<RDCHiddenDecal>(obj);

                            bool unchanged = Live(obj) && Live(marker.Road) && !_pending.Contains(marker.Road) && EntityManager.HasComponent<PrefabRef>(obj) && EntityManager.HasComponent<Owner>(obj) && EntityManager.GetComponentData<PrefabRef>(obj).m_Prefab == marker.Prefab && EntityManager.GetComponentData<Owner>(obj).m_Owner == marker.Owner && EntityManager.HasComponent<RDCUpgrade>(marker.Road) && (RoadSideUpgrades.HiddenDecals(EntityManager, marker.Road, obj) & marker.Kind) != 0;

                            if (!unchanged)

                            {

                                if (!ShouldHide(obj, out var road)) { Release(obj); continue; }

                                var prefab = EntityManager.GetComponentData<PrefabRef>(obj).m_Prefab;

                                EntityManager.SetComponentData(obj, new RDCHiddenDecal { Road = road, Prefab = prefab, Owner = EntityManager.GetComponentData<Owner>(obj).m_Owner, Kind = Kind(prefab) });

                            }

                            if (!EntityManager.HasComponent<Hidden>(obj)) { repairs.AddComponent<Hidden>(obj); if (!EntityManager.HasComponent<BatchesUpdated>(obj)) repairs.AddComponent<BatchesUpdated>(obj); }

                        }

                    repairs.Playback(EntityManager);

                }

                foreach (var road in _pending)

                {

                    if (!Live(road)) continue; InspectChildren(road, 0);

                    if (EntityManager.HasComponent<Edge>(road)) { var edge = EntityManager.GetComponentData<Edge>(road); InspectChildren(edge.m_Start, 0); InspectChildren(edge.m_End, 0); }

                }

                _initial = false;

            }

            catch (Exception error) { Restore(); Enabled = false; Mod.Log.Error("Decal visibility stopped and owned flags restored: " + error); }

        }

    }

}

