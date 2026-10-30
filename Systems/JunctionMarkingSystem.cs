using System;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Serialization;

using Unity.Collections;

using Unity.Entities;



namespace MedianGrassReplacement.Systems

{

    // Tracks only Hidden flags added by this mod; pre-existing flags are untouched.

    public struct MGRHiddenJunctionLine : IComponentData, IEmptySerializable { }



    public partial class JunctionMarkingSystem : GameSystemBase, IPreSerialize

    {

        private EntityQuery _lines, _hidden, _changedNodes;

        private PrefabSystem _prefabs;

        private bool _active, _initial = true, _previousEnabled, _previousCurbs, _previousParkingCurbs;

        private static bool _refresh;

        private static readonly System.Collections.Generic.HashSet<Entity> RequestedNodes = new System.Collections.Generic.HashSet<Entity>();

        private readonly System.Collections.Generic.HashSet<Entity> _nodeWork = new System.Collections.Generic.HashSet<Entity>();

        internal static void RequestNodeRefresh(Entity node) { RequestedNodes.Add(node); }



        internal static void RequestNodeRefresh() { _refresh = true; }

        internal static void RequestRefresh() { _refresh = true; }

        protected override void OnCreate()

        {

            base.OnCreate();

            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();

            var description = new EntityQueryDesc

            {

                All = new[] { ComponentType.ReadOnly<Game.Net.Lane>(), ComponentType.ReadOnly<Owner>(),

                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Curve>() },

                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>() }

            };

            _lines = GetEntityQuery(description);



            _hidden = GetEntityQuery(ComponentType.ReadOnly<MGRHiddenJunctionLine>());

            _changedNodes = GetEntityQuery(ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            _changedNodes.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.ReadOnly<Game.Net.SubLane>() });

        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _externalPaint.Clear(); _active = false; Restore(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            Restore(); RequestedNodes.Clear();

            _active = (mode & GameMode.GameOrEditor) != GameMode.None;

            _initial = true;

        }

        private int RoadCount(Entity node)

        {

            if (!EntityManager.HasComponent<Game.Net.Node>(node) || EntityManager.HasComponent<Deleted>(node)

                || !EntityManager.HasBuffer<ConnectedEdge>(node)) return 0;

            int count = 0;

            foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(node, true))

                if (EntityManager.HasComponent<Road>(edge.m_Edge) && !EntityManager.HasComponent<Deleted>(edge.m_Edge)

                    && !EntityManager.HasComponent<Game.Tools.Temp>(edge.m_Edge)) count++;

            return count;

        }

        private readonly ExternalPaintOwnership _externalPaint=new ExternalPaintOwnership();

        private bool ShouldHide(Entity line)

        {

            if (!EntityManager.Exists(line) || EntityManager.HasComponent<Deleted>(line)

                || !EntityManager.HasComponent<Owner>(line) || !EntityManager.HasComponent<PrefabRef>(line)) return false;

            var owner = EntityManager.GetComponentData<Owner>(line).m_Owner;

            if (!EntityManager.HasComponent<Game.Net.Node>(owner)) return false;

            if(_externalPaint.Contains(EntityManager,line))return false;
            int roads = RoadCount(owner);

            // Defensive simulation guard: never hide a lane used by vehicles/pedestrians.

            if (EntityManager.HasComponent<Game.Net.CarLane>(line) || EntityManager.HasComponent<Game.Net.ParkingLane>(line)

                || EntityManager.HasComponent<Game.Net.PedestrianLane>(line) || EntityManager.HasComponent<Game.Net.TrackLane>(line)) return false;

            if (!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(line).m_Prefab, out var prefab) || prefab == null) return false;
            if(World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().OwnsHighwayNodePaint(owner,line))return true;
            if(EntityManager.HasComponent<Curve>(line)&&NodeLinePolicy.IsLineGeometry(prefab)&&HighwayNodePaintPolicy.Role(prefab.name,World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().IsYellowNodeBoundary(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier))==1&&World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().OwnsExplicitNodeCenter(owner))return true;
            if((NodeLinePolicy.IsLineGeometry(prefab)||(SegmentUpgradePolicy.Mode(EntityManager,owner)&SegmentUpgradePolicy.HideNative)!=0&&NodeLinePolicy.IsPaintFill(prefab.name))&&!NodeLinePolicy.IsProtected(prefab.name)&&EntityManager.HasComponent<Curve>(line))
            {
                var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();bool center=prefab.name.IndexOf("Center Line",System.StringComparison.OrdinalIgnoreCase)>=0||emitter.IsYellowNodeBoundary(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier);
                if((SegmentUpgradePolicy.Mode(EntityManager,owner)&SegmentUpgradePolicy.HideNative)!=0||SegmentUpgradePolicy.Hides(EntityManager,owner,center,prefab.name.IndexOf("Edge Line",System.StringComparison.OrdinalIgnoreCase)>=0)||center&&EntityManager.HasComponent<MGRHideCenterline>(owner))return true;
            }
            if(EntityManager.HasComponent<Curve>(line)&&NodeLinePolicy.IsLineGeometry(prefab)&&World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RemovesNodeDivider(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier))return true;
            if((EntityManager.HasComponent<MGRRemoveJunctionEdges>(owner)||EntityManager.HasComponent<MGRJunctionEdgeLines>(owner))&&EntityManager.HasComponent<Curve>(line)&&NodeLinePolicy.IsLineGeometry(prefab)&&!NodeLinePolicy.IsProtected(prefab.name))
            {
                var edgePaint=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                if(edgePaint.HighwayEdgeSource(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier)&&(EntityManager.HasComponent<MGRRemoveJunctionEdges>(owner)||edgePaint.CoversNativeNodePaint(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier,false)))return true;
            }
            if(EntityManager.HasComponent<Curve>(line)&&!NodeLinePolicy.IsProtected(prefab.name)&&(NodeLinePolicy.IsLineGeometry(prefab)||NodeLinePolicy.IsPaintFill(prefab.name))&&SegmentUpgradePolicy.HidesNode(EntityManager,owner,EntityManager.GetComponentData<Curve>(line).m_Bezier,false,false,true))return true;

            if(EntityManager.HasComponent<MGRRemoveJunctionEdges>(owner)&&EntityManager.HasComponent<Curve>(line)&&NodeLinePolicy.IsLineGeometry(prefab)&&prefab.name.IndexOf("Edge Line",System.StringComparison.OrdinalIgnoreCase)>=0
                &&!World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().IsYellowNodeBoundary(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier))return true;
            if (EntityManager.HasComponent<MGRHideNodeLines>(owner))

            {

                if (NodeLinePolicy.IsProtected(prefab.name)) return false;

                if (NodeLinePolicy.IsLaneLine(prefab.name)) return true;

                // Some paint-only variants keep a generic lane name. Inspect only

                // decal meshes; asphalt, curbs and other physical geometry stay intact.

                if (prefab.m_Meshes == null || prefab.m_Meshes.Length == 0) return false;

                bool paint = NodeLinePolicy.IsPaintFill(prefab.name), any = false;

                foreach (var mesh in prefab.m_Meshes)

                {

                    if (mesh?.m_Mesh == null) continue;

                    if (!mesh.m_Mesh.Has<DecalProperties>() || NodeLinePolicy.IsProtected(mesh.m_Mesh.name)) return false;

                    any = true;

                    paint |= NodeLinePolicy.IsLaneLine(mesh.m_Mesh.name) || NodeLinePolicy.IsPaintFill(mesh.m_Mesh.name);

                }

                return any && paint;

            }

            if(EntityManager.HasComponent<MGRJunctionEdgeLines>(owner)&&EntityManager.HasComponent<Curve>(line)&&NodeLinePolicy.IsLaneLine(prefab.name)&&!NodeLinePolicy.IsProtected(prefab.name))
            {
                var marking=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                if(marking.CoversNativeNodePaint(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier,false))return true;
            }
            bool highway=roads>=3;
            if(highway)foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(owner,true))if(EntityManager.HasComponent<Road>(connection.m_Edge)&&!RoadSurfacePolicy.IsHighway(EntityManager,connection.m_Edge)){highway=false;break;}
            if(highway)return World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().OwnsHighwayNodePaint(owner,line);
            if(roads==2&&EntityManager.HasComponent<Curve>(line)&&ExternalPaintPolicy.IsBoundary(prefab.name))
            {
                var marking=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                if(marking.OwnsNodeUTurnPaint(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier))return true;
                bool center=prefab.name.IndexOf("Center Line",StringComparison.OrdinalIgnoreCase)>=0||marking.IsYellowNodeBoundary(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier);
                return center&&marking.OwnsNodeCenterRole(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier)||marking.CoversNativeNodePaint(owner,EntityManager.GetComponentData<Curve>(line).m_Bezier,center);
            }

            if (roads < 2 || (roads == 2 && Mod.Settings.CurbWhiteLines && !World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().HasNodeJoin(owner))) return false;

            return JunctionMarkingPolicy.Suppress(prefab.name, roads, true);

        }

        private bool HideIfNeeded(Entity line)

        {

            if (!EntityManager.Exists(line) || EntityManager.HasComponent<Deleted>(line) || EntityManager.HasComponent<Game.Tools.Temp>(line) || EntityManager.HasComponent<MGRCurbMarking>(line) || EntityManager.HasComponent<MGRIntersectionPaint>(line) || EntityManager.HasComponent<MGRHiddenJunctionLine>(line) || !ShouldHide(line) || EntityManager.HasComponent<Game.Tools.Hidden>(line)) return false;

            EntityManager.AddComponent<MGRHiddenJunctionLine>(line); EntityManager.AddComponent<Game.Tools.Hidden>(line);

            if (!EntityManager.HasComponent<BatchesUpdated>(line)) EntityManager.AddComponent<BatchesUpdated>(line); return true;

        }

        private void Reveal(Entity line)

        {

            if (!EntityManager.Exists(line) || !EntityManager.HasComponent<MGRHiddenJunctionLine>(line)) return;

            bool parentHidden = EntityManager.HasComponent<Owner>(line) && EntityManager.HasComponent<Game.Tools.Hidden>(EntityManager.GetComponentData<Owner>(line).m_Owner);

            if (EntityManager.HasComponent<Game.Tools.Hidden>(line) && !parentHidden && !EntityManager.HasComponent<MGRReplacedPaint>(line)) EntityManager.RemoveComponent<Game.Tools.Hidden>(line);

            EntityManager.RemoveComponent<MGRHiddenJunctionLine>(line);

            if (!EntityManager.HasComponent<Deleted>(line) && !EntityManager.HasComponent<BatchesUpdated>(line)) EntityManager.AddComponent<BatchesUpdated>(line);

        }

        internal void Restore()

        {

            Dependency.Complete();

            using (var lines = _hidden.ToEntityArray(Allocator.Temp)) foreach (var line in lines) Reveal(line);

            _initial = true;

        }

        public void PreSerialize(Context context) { Restore(); }

        protected override void OnUpdate()

        {

            if (Mod.Saving) return;

            if (!_active) return;



            // Junction cleanup is independent of whether custom edge lines are visible.

            bool enabled = Mod.Settings != null;

            if (enabled && (_previousCurbs != Mod.Settings.CurbWhiteLines || _previousParkingCurbs != Mod.Settings.CurbLinesBesideParking))

            {

                _initial = true; _previousCurbs = Mod.Settings.CurbWhiteLines; _previousParkingCurbs = Mod.Settings.CurbLinesBesideParking;

            }

            if (!enabled) { if (_previousEnabled) Restore(); _previousEnabled = false; return; }

            if (!_previousEnabled || _refresh) _initial = true;

            _previousEnabled = true; _refresh = false;

            _nodeWork.Clear(); foreach (var node in RequestedNodes) _nodeWork.Add(node); RequestedNodes.Clear();

            using (var nodes = _changedNodes.ToEntityArray(Allocator.Temp)) foreach (var node in nodes) _nodeWork.Add(node);

            bool nodeChange = _nodeWork.Count > 0;

            _lines.ResetFilter();

            if (!_initial) _lines.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() });

            var query = _lines;

            if (query.IsEmpty && !nodeChange && _hidden.IsEmpty) { _initial = false; return; }

            Dependency.Complete();

            try

            {

                // LaneHiddenSystem removes per-lane Hidden when the parent is

                // visible. Run AFTER ModificationBarrier5, immediately before

                // PreCulling, and repair only our small tracked set. BatchesUpdated

                // explicitly refreshes the renderer without rebuilding lane geometry.

                using (var visibility = new EntityCommandBuffer(Allocator.Temp))

                {

                    using (var owned = _hidden.ToEntityArray(Allocator.Temp))

                        foreach (var line in owned)

                        {

                            if (EntityManager.HasComponent<Deleted>(line)) continue;
                            if(_externalPaint.Contains(EntityManager,line)){Reveal(line);continue;}

                            if (!EntityManager.HasComponent<Game.Tools.Hidden>(line))

                            {

                                visibility.AddComponent<Game.Tools.Hidden>(line);

                                if (!EntityManager.HasComponent<BatchesUpdated>(line)) visibility.AddComponent<BatchesUpdated>(line);

                            }

                        }

                    visibility.Playback(EntityManager);

                }

                // Recheck owned flags when the node topology changes or refresh is pressed.

                if (_initial || nodeChange)

                    using (var hidden = _hidden.ToEntityArray(Allocator.Temp))

                        foreach (var line in hidden) if ((_initial || !EntityManager.HasComponent<Owner>(line) || _nodeWork.Contains(EntityManager.GetComponentData<Owner>(line).m_Owner)) && !ShouldHide(line)) Reveal(line);

                int added = 0;

                using (var lines = query.ToEntityArray(Allocator.Temp))

                    foreach (var line in lines)

                    {

                        if (HideIfNeeded(line)) added++;

                    }

                foreach (var node in _nodeWork) if (EntityManager.Exists(node) && EntityManager.HasBuffer<Game.Net.SubLane>(node))

                    {

                        var lanes = new System.Collections.Generic.List<Entity>(); foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node, true)) lanes.Add(entry.m_SubLane);

                        foreach (var lane in lanes) if (HideIfNeeded(lane)) added++;

                    }

                if (_initial || added > 0) Mod.Debug($"Junction cleanup: {added} edge lines hidden; {_hidden.CalculateEntityCount()} owned flags. Crosswalks/stop bars retained.");

                _initial = false;

            }

            catch (Exception error) { Restore(); Enabled = false; Mod.Log.Error("Junction cleanup paused: " + error); }

            finally { _lines.ResetFilter(); }

        }

    }

}





