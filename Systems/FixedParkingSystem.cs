using System;

using System.Collections.Generic;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Serialization;

using Game.Vehicles;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    // Runtime-only tracking; no new prefab is registered with PrefabSystem.

    public partial class FixedParkingSystem : GameSystemBase, IPreSerialize

    {

        private EntityQuery _lanes, _changedLanes, _references, _changedOwners,_changedNodes;

        private bool _scanAll = true;

        private static bool _scanRequested;

        internal static void RequestScan() { _scanRequested = true; }

        private static readonly HashSet<Entity> _queuedOwners = new HashSet<Entity>();

        internal static void QueueRoad(Entity road) { _queuedOwners.Add(road); }

        private readonly HashSet<Entity> _waiting = new HashSet<Entity>();

        private readonly HashSet<Entity> _pendingLanes = new HashSet<Entity>();
        private static readonly ComponentType[] LaneChanges = { ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() };

        private readonly Dictionary<Entity, Entity> _originals = new Dictionary<Entity, Entity>();

        private readonly Dictionary<Entity, Entity> _copies = new Dictionary<Entity, Entity>();

        private float _length = -1f, _setback = -1f;

        private readonly Dictionary<Entity, List<LaneOverlap>> _restrictions = new Dictionary<Entity, List<LaneOverlap>>();

        private bool _active;



        protected override void OnCreate()

        {

            base.OnCreate();

            _lanes = GetEntityQuery(ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.ReadOnly<PrefabRef>(),

                ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            _changedLanes = GetEntityQuery(ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.ReadOnly<PrefabRef>(),

                ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());



            _changedOwners = GetEntityQuery(new EntityQueryDesc

            {

                All = new[] { ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Game.Net.SubLane>() },

                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }

            });

            _changedNodes=GetEntityQuery(ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<ConnectedEdge>(),ComponentType.ReadOnly<Updated>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            _references = GetEntityQuery(ComponentType.ReadWrite<PrefabRef>());

        }



        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _active = false; RestoreAndDestroy(true); _queuedOwners.Clear(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            RestoreAndDestroy(true); _queuedOwners.Clear();

            _active = (mode & GameMode.GameOrEditor) != GameMode.None;

        }



        internal void RestoreReferences()

        {

            if ((_originals.Count == 0 && _restrictions.Count == 0) || _changedOwners.IsEmpty&&_changedNodes.IsEmpty) return;

            Dependency.Complete();

            var repairOwners=new HashSet<Entity>();
            using(var owners=_changedOwners.ToEntityArray(Allocator.Temp))foreach(var owner in owners)repairOwners.Add(owner);
            using(var nodes=_changedNodes.ToEntityArray(Allocator.Temp))foreach(var node in nodes)
                foreach(var edge in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(EntityManager.HasComponent<Road>(edge.m_Edge)&&EntityManager.HasBuffer<Game.Net.SubLane>(edge.m_Edge)&&!EntityManager.HasComponent<Deleted>(edge.m_Edge)&&!EntityManager.HasComponent<Game.Tools.Temp>(edge.m_Edge))repairOwners.Add(edge.m_Edge);
            foreach(var owner in repairOwners)
                {
                    // Regeneration can replace only LaneOverlap, leaving Curve/PrefabRef unchanged.
                    // Reapply this owner after the native lane jobs finish.
                    _queuedOwners.Add(owner);

                    foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true))

                    {

                        Entity lane = entry.m_SubLane;

                        RestoreRestriction(lane);

                        if (!_originals.TryGetValue(lane, out var original) || !EntityManager.Exists(lane)

                            || !EntityManager.HasComponent<PrefabRef>(lane) || !EntityManager.Exists(original)) continue;

                        Entity current = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;

                        if (_copies.TryGetValue(original, out var copy) && current == copy)

                            EntityManager.SetComponentData(lane, new PrefabRef(original));

                    }
                }

        }



        internal void RestoreAndDestroy(bool forget)

        {

            Dependency.Complete();

            foreach (var lane in new List<Entity>(_restrictions.Keys)) RestoreRestriction(lane);

            if (_copies.Count == 0)

            {

                if (forget) { _originals.Clear(); _waiting.Clear(); }

                _scanAll = true;

                return;

            }

            // Restore every reference, including any references another native

            // system copied, before destroying private definitions for saving.

            var reverse = new Dictionary<Entity, Entity>();

            foreach (var pair in _copies) reverse[pair.Value] = pair.Key;

            using (var entities = _references.ToEntityArray(Allocator.Temp))

                foreach (var entity in entities)

                {

                    var reference = EntityManager.GetComponentData<PrefabRef>(entity);

                    if (reverse.TryGetValue(reference.m_Prefab, out var original) && EntityManager.Exists(original))

                        EntityManager.SetComponentData(entity, new PrefabRef(original));

                }

            foreach (var copy in _copies.Values) if (EntityManager.Exists(copy)) EntityManager.DestroyEntity(copy);

            _copies.Clear();

            if (forget) { _originals.Clear(); _waiting.Clear(); }

            _scanAll = true;

            CurbLineEmissionSystem.RequestRoadRefresh();

        }



        public void PreSerialize(Context context)

        {

            // Runs BEFORE BeginPrefabSerializationSystem, whose prefab-index

            // array must never contain two entities with the same PrefabData.

            RestoreAndDestroy(false);

            Mod.Debug("Fixed parking: restored original references and removed private definitions before serialization.");

        }



        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("FixedParkingSystem",started);}
        }
        private void UpdateMeasured()

        {

            if (Mod.Saving) return;

            if (!_active || Mod.Settings == null)

            {

                if (_copies.Count != 0 || _originals.Count != 0 || _restrictions.Count != 0) RestoreAndDestroy(true);

                _scanAll = true;

                return;

            }

            try { ApplyBays(); }

            catch (Exception error)

            {

                RestoreAndDestroy(true);

                Enabled = false;

                Mod.Log.Error("Fixed parking stopped: " + error);

            }

        }



        private void ApplyBays()

        {

            if (_scanRequested) { _scanAll = true; _scanRequested = false; }

            float length = ParkingBayPolicy.Length(Mod.Settings.FixedParkingBayLength);

            if (_length != length || _setback != Mod.Settings.ParkingIntersectionSetback)

            { RestoreAndDestroy(true); _length = length; _setback = Mod.Settings.ParkingIntersectionSetback; }

            var pending = _pendingLanes; pending.Clear();

            _lanes.ResetFilter();

            if (!_scanAll) _lanes.SetChangedVersionFilter(LaneChanges);

            try { using (var changed = _lanes.ToEntityArray(Allocator.Temp)) foreach (var lane in changed) pending.Add(lane); }

            finally { _lanes.ResetFilter(); }

            foreach (var owner in _queuedOwners) if (EntityManager.Exists(owner) && EntityManager.HasBuffer<Game.Net.SubLane>(owner))

                    foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true))

                    {

                        var lane = entry.m_SubLane;

                        if (EntityManager.HasComponent<Game.Net.ParkingLane>(lane) && EntityManager.HasComponent<PrefabRef>(lane) && EntityManager.HasComponent<Curve>(lane) && EntityManager.HasComponent<Owner>(lane) && !EntityManager.HasComponent<Deleted>(lane) && !EntityManager.HasComponent<Game.Tools.Temp>(lane)) pending.Add(lane);

                    }

            _queuedOwners.Clear();

            if (pending.Count == 0) { _scanAll = false; return; }

            World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RefreshDefinitionsIfNeeded();



            var stale = new List<Entity>();

            foreach (var pair in _originals)

                if (!EntityManager.Exists(pair.Key) || EntityManager.HasComponent<Deleted>(pair.Key)) stale.Add(pair.Key);

            foreach (var lane in stale) { _originals.Remove(lane); _waiting.Remove(lane); }

            foreach (var lane in pending)

            {

                var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;

                if (!EntityManager.HasComponent<Road>(owner) || EntityManager.HasComponent<Deleted>(owner)) continue;

                byte mode = RoadSideUpgrades.ParkingMode(EntityManager, owner, Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier, .5f));

                bool banned=ParkingBanPolicy.Banned(EntityManager,owner,Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier,.5f));
                if(banned)mode=1;
                bool marked = mode >= 2;

                if (!marked)

                {

                    if (mode == 0) RestoreRestriction(lane);

                    if (_originals.TryGetValue(lane, out var saved) && EntityManager.Exists(saved))

                    {

                        // Restore our copy only. If native road regeneration already

                        // supplied a newer vanilla definition, preserve that definition.

                        var currentReference = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;

                        if (_copies.TryGetValue(saved, out var privateCopy) && currentReference == privateCopy)

                            EntityManager.SetComponentData(lane, new PrefabRef(saved));

                        _originals.Remove(lane);

                        if (!EntityManager.HasComponent<Updated>(lane)) EntityManager.AddComponent<Updated>(lane);

                        CurbLineEmissionSystem.QueueRoad(owner);

                    }

                    // Native spacing, with the user's intersection setback retained.

                    if (mode != 0) ApplySetback(lane, owner); continue;

                }

                var parking = EntityManager.GetComponentData<Game.Net.ParkingLane>(lane);

                if ((parking.m_Flags & (ParkingLaneFlags.VirtualLane | ParkingLaneFlags.SpecialVehicles | ParkingLaneFlags.ParkingDisabled)) != 0) continue;

                Entity current = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;

                bool known = _originals.TryGetValue(lane, out var original);

                if (known && current != original && (!_copies.TryGetValue(original, out var oldCopy) || current != oldCopy))

                { _originals.Remove(lane); known = false; }

                if (!known) original = current;

                if (!EntityManager.HasComponent<ParkingLaneData>(original) || !EntityManager.HasComponent<NetLaneData>(original)) continue;

                var data = EntityManager.GetComponentData<ParkingLaneData>(original);

                if (!math.isfinite(data.m_SlotAngle) || math.abs(data.m_SlotAngle) > 0.01f) continue;

                ApplySetback(lane, owner);

                // Apply native slots to existing roads too. Parked cars keep

                // their transforms; occupancy blocks their current space and

                // subsequent arrivals use the fixed slot grid.

                _waiting.Remove(lane);

                if (!_copies.TryGetValue(original, out var copy) || !EntityManager.Exists(copy))

                {

                    copy = EntityManager.Instantiate(original);

                    // Do not register/reinitialize a managed prefab. Only

                    // this private ECS copy's parking data is changed.

                    if (EntityManager.HasComponent<Created>(copy)) EntityManager.RemoveComponent<Created>(copy);

                    if (EntityManager.HasComponent<Updated>(copy)) EntityManager.RemoveComponent<Updated>(copy);

                    data.m_SlotInterval = ParkingBayPolicy.NativeInterval(length);

                    data.m_SlotSize = new float2(EntityManager.GetComponentData<NetLaneData>(original).m_Width, length);

                    data.m_MaxCarLength = math.max(0f, length - 0.50f);

                    EntityManager.SetComponentData(copy, data);

                    _copies[original] = copy;

                }

                if (current != copy) EntityManager.SetComponentData(lane, new PrefabRef(copy));

                _originals[lane] = original;

                if (!known)

                {

                    if (!EntityManager.HasComponent<Updated>(lane)) EntityManager.AddComponent<Updated>(lane);

                    CurbLineEmissionSystem.QueueRoad(owner);

                }

            }

            _scanAll = false;

        }



        private void RestoreRestriction(Entity lane)

        {

            if (!_restrictions.TryGetValue(lane, out var added)) return;

            if (EntityManager.Exists(lane) && EntityManager.HasBuffer<LaneOverlap>(lane))

            {

                var buffer = EntityManager.GetBuffer<LaneOverlap>(lane);

                foreach (var owned in added)

                    for (int i = buffer.Length - 1; i >= 0; i--)

                        if (buffer[i].Equals(owned)) { buffer.RemoveAt(i); break; }

            }

            _restrictions.Remove(lane);
            if(EntityManager.Exists(lane)&&!EntityManager.HasComponent<Deleted>(lane)&&!EntityManager.HasComponent<Updated>(lane))EntityManager.AddComponent<Updated>(lane);

        }



        private bool Junction(Entity node)

        {

            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return false;

            int count = 0;

            foreach (var connection in EntityManager.GetBuffer<ConnectedEdge>(node, true))

                if (EntityManager.HasComponent<Road>(connection.m_Edge) && !EntityManager.HasComponent<Deleted>(connection.m_Edge)

                    && !EntityManager.HasComponent<Game.Tools.Temp>(connection.m_Edge)) count++;

            return count >= 3;

        }



        private void ApplySetback(Entity lane, Entity road)

        {

            bool banned=ParkingBanPolicy.Banned(EntityManager,road,Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier,.5f));
            if (!banned && _setback <= 0f) { RestoreRestriction(lane); return; }

            if (!EntityManager.HasBuffer<LaneOverlap>(lane)

                || !EntityManager.HasBuffer<Game.Net.SubLane>(road) || !EntityManager.HasComponent<Game.Net.Edge>(road)) return;

            Entity blocker = Entity.Null;

            float lower = 0f, upper = 1f;

            var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;

            var marking = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();

            foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(road, true))

            {

                Entity other = entry.m_SubLane;

                if (EntityManager.HasComponent<Deleted>(other)) continue;

                if (blocker == Entity.Null && EntityManager.HasComponent<Game.Net.CarLane>(other)) blocker = other;



            }

            if (blocker == Entity.Null) return;

            var edge = EntityManager.GetComponentData<Game.Net.Edge>(road);

            bool roadStartJunction = Junction(edge.m_Start), roadEndJunction = Junction(edge.m_End);

            float2 delta = new float2(float.NaN);

            if (EntityManager.HasComponent<EdgeLane>(lane)) delta = EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;

            float firstRoadPosition = float.NaN, lastRoadPosition = float.NaN;

            if (EntityManager.HasComponent<Curve>(road))

            {

                var roadCurve = EntityManager.GetComponentData<Curve>(road).m_Bezier;

                Colossal.Mathematics.MathUtils.Distance(roadCurve, curve.a, out firstRoadPosition);

                Colossal.Mathematics.MathUtils.Distance(roadCurve, curve.d, out lastRoadPosition);

            }

            bool start = ParkingSetback.JunctionEnd(delta.x, firstRoadPosition, roadStartJunction, roadEndJunction);

            bool end = ParkingSetback.JunctionEnd(delta.y, lastRoadPosition, roadStartJunction, roadEndJunction);

            marking.GetRoadStopRange(road, curve, marking.LaneForward(road, lane, curve), out lower, out upper);

            bool available = ParkingSetback.TryLimits(curve, lower, upper, start, end, _setback, out float first, out float last);

            var additions = new List<LaneOverlap>();

            if (banned || !available) additions.Add(new LaneOverlap(blocker, new float4(0f, 1f, 0f, 1f), OverlapFlags.Road, 0f, 0));

            else

            {

                if (first > 0f) { var overlap = new LaneOverlap(blocker, new float4(0f, first, 0f, 1f), OverlapFlags.Road, 0f, 0); overlap.m_ThisEnd = (byte)math.clamp(math.ceil(first * 255f), 0f, 255f); additions.Add(overlap); }

                if (last < 1f) { var overlap = new LaneOverlap(blocker, new float4(last, 1f, 0f, 1f), OverlapFlags.Road, 0f, 0); overlap.m_ThisStart = (byte)math.clamp(math.floor(last * 255f), 0f, 255f); additions.Add(overlap); }

            }

            if (additions.Count == 0) { RestoreRestriction(lane); return; }

            if (_restrictions.TryGetValue(lane, out var existing) && existing.Count == additions.Count)

            {

                bool same = true;

                var currentBuffer = EntityManager.GetBuffer<LaneOverlap>(lane, true);

                for (int i = 0; i < additions.Count; i++)

                {

                    bool present = false;

                    foreach (var current in currentBuffer) if (current.Equals(additions[i])) { present = true; break; }

                    if (!existing[i].Equals(additions[i]) || !present) { same = false; break; }

                }

                if (same) return;

            }

            RestoreRestriction(lane);

            var overlaps = EntityManager.GetBuffer<LaneOverlap>(lane);

            foreach (var overlap in additions) overlaps.Add(overlap);

            // Native parking expects overlap ranges ordered by their start.

            for (int i = 1; i < overlaps.Length; i++)

            {

                var value = overlaps[i]; int j = i - 1;

                while (j >= 0 && overlaps[j].CompareTo(value) > 0) { overlaps[j + 1] = overlaps[j]; j--; }

                overlaps[j + 1] = value;

            }

            _restrictions[lane] = additions;
            if(!EntityManager.HasComponent<Updated>(lane))EntityManager.AddComponent<Updated>(lane);

            CurbLineEmissionSystem.QueueRoad(road);

        }





    }



    public partial class FixedParkingRestoreSystem : GameSystemBase

    {

        protected override void OnUpdate() { if (!Mod.Saving) World.GetOrCreateSystemManaged<FixedParkingSystem>().RestoreReferences(); }

    }

}
