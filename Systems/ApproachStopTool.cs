using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public partial class ApproachStopTool:ToolBaseSystem
    {
        internal const string AssetName="MGR Two Way Stop", SingleAssetName="MGR Single Approach Stop", YieldAssetName="MGR Two Way Yield", SingleYieldAssetName="MGR Single Approach Yield";
        private bool Single=>_selected?.name==SingleAssetName||_selected?.name==SingleYieldAssetName;
        private bool Yield=>_selected?.name==YieldAssetName||_selected?.name==SingleYieldAssetName;
        private PrefabBase _selected;
        private readonly UpgradeDrag _drag = new UpgradeDrag();
        protected override void OnCreate(){base.OnCreate();m_ToolSystem.tools.Remove(this);m_ToolSystem.tools.Insert(0,this);}
        public override string toolID=>"MGRApproachStop";
        public override PrefabBase GetPrefab()=>_selected;
        public override bool TrySetPrefab(PrefabBase prefab){if(prefab==null||prefab.name!=AssetName&&prefab.name!=SingleAssetName&&prefab.name!=YieldAssetName&&prefab.name!=SingleYieldAssetName)return false;_selected=prefab;m_ToolSystem.EventPrefabChanged?.Invoke(prefab);return true;}
        protected override void OnStartRunning(){base.OnStartRunning();applyAction.shouldBeEnabled=true;secondaryApplyAction.shouldBeEnabled=true;cancelAction.shouldBeEnabled=true;}
        protected override void OnStopRunning()
        {
            _drag.Reset();
            base.OnStopRunning();
        }

        public override void InitializeRaycast(){base.InitializeRaycast();m_ToolRaycastSystem.typeMask=TypeMask.Net;m_ToolRaycastSystem.netLayerMask=Layer.Road;m_ToolRaycastSystem.collisionMask=CollisionMask.OnGround|CollisionMask.Overground;}
        private void RefreshEndpoint(Entity node)
        {
            if(!EntityManager.Exists(node)||EntityManager.HasComponent<Deleted>(node)||EntityManager.HasComponent<Temp>(node))return;
            if(EntityManager.HasBuffer<Game.Net.SubLane>(node))
            {
                using(var lanes=EntityManager.GetBuffer<Game.Net.SubLane>(node,true).ToNativeArray(Allocator.Temp))foreach(var entry in lanes)
                {
                    var lane=entry.m_SubLane;
                    if(!EntityManager.Exists(lane)||EntityManager.HasComponent<Deleted>(lane)||EntityManager.HasComponent<Temp>(lane))continue;
                    if(!EntityManager.HasComponent<Updated>(lane))EntityManager.AddComponent<Updated>(lane);
                }
            }
            if(!EntityManager.HasComponent<Updated>(node))EntityManager.AddComponent<Updated>(node);
            World.GetOrCreateSystemManaged<ApproachStopSystem>().Queue(node);
        }
        protected override JobHandle OnUpdate(JobHandle deps)
        {
            if (m_ToolSystem.activeTool != this || Mod.Saving)
            {
                _drag.Reset();
                return deps;
            }
            if (UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame()))
            {
                _drag.Reset();
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                return deps;
            }
            var phase = _drag.Read(_selected, m_HasFocus, applyAction.WasPressedThisFrame(),
                secondaryApplyAction.WasPressedThisFrame(), applyAction.IsPressed(), secondaryApplyAction.IsPressed());
            if (phase == UpgradeDrag.Phase.Commit)
            {
                if (_drag.Count != 0) { deps.Complete(); deps = default; }
                _drag.Commit(ApplyDragTarget);
                return deps;
            }
            deps = UpdateHover(deps);
            if (phase == UpgradeDrag.Phase.Collect)
                _drag.Draw(World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>());
            return deps;
        }

        private JobHandle UpdateHover(JobHandle deps)
        {
            if (!m_HasFocus || UpgradeInputPolicy.PointerBlocked || !m_ToolRaycastSystem.GetRaycastResult(out var hit))
                return deps;
            var position = hit.m_Hit.m_HitPosition;
            if (!SectionUpgradeTargeting.TryRoad(EntityManager, hit.m_Owner, position, out var road)) return deps;
            var edge = EntityManager.GetComponentData<Edge>(road);
            Entity node;
            if (EntityManager.HasComponent<EdgeGeometry>(road))
                node = NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(road), position)
                    ? edge.m_End : edge.m_Start;
            else node = new UpgradeNetworkGraph(EntityManager).NearestNode(road, position);
            if (!UpgradeDragTarget.Live(EntityManager, node)) return deps;
            if (_drag.Anchored)
            {
                if (_drag.RouteTo(EntityManager, new UpgradeDragTarget(node), hit.m_Hit.m_HitPosition))
                    PlanApproaches();
                return deps;
            }

            if (!CanTargetNode(node)) return deps;
            var selected = ApproachStopSystem.Away(EntityManager, road, node);
            var endpoint = SelectApproaches(node, selected, -selected, road);
            if (endpoint.Part == Entity.Null) return deps;
            if (_drag.RouteTo(EntityManager, endpoint, hit.m_Hit.m_HitPosition))
                PlanApproaches();
            else if (!_drag.Active)
            {
                var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>();
                var buffer = overlay.GetBuffer(out var pending); pending.Complete();
                var color = new UnityEngine.Color(.15f, 1f, .3f, .8f);
                if (TryApproachPreview(node, endpoint.Part, out var roadPath)) buffer.DrawCurve(color, roadPath, 2f);
                if (TryApproachPreview(node, endpoint.Other, out var oppositePath)) buffer.DrawCurve(color, oppositePath, 2f);
            }
            return deps;
        }

        private bool CanTargetNode(Entity node) => EntityManager.HasBuffer<ConnectedEdge>(node)
            && EntityManager.GetBuffer<ConnectedEdge>(node, true).Length >= 3 && !EntityManager.HasComponent<TrafficLights>(node);

        private void PlanApproaches()
        {
            var anchor = _drag.Anchor;
            if (!CanTargetNode(anchor.Owner)) return;
            var direction = ApproachStopSystem.Away(EntityManager, anchor.Part, anchor.Owner);
            var opposite = Single ? -direction : ApproachStopSystem.Away(EntityManager, anchor.Other, anchor.Owner);
            AddApproach(anchor);
            foreach (var step in _drag.Steps)
            {
                if (step.To == anchor.Owner || !CanTargetNode(step.To)) continue;
                var choice = SelectApproaches(step.To, direction, opposite);
                if (choice.Part != Entity.Null) AddApproach(choice);
            }
        }

        private UpgradeDragTarget SelectApproaches(Entity node, float3 direction, float3 opposite, Entity exact = default)
        {
            var first = ApproachStopSystem.MatchApproach(EntityManager, node, direction);
            if (first == Entity.Null || (exact != Entity.Null && first != exact)) return default;
            if (Single) return new UpgradeDragTarget(node, first);
            var second = ApproachStopSystem.MatchApproach(EntityManager, node, opposite);
            if (second == Entity.Null || second == first ||
                !ApproachDirection.Opposite(ApproachStopSystem.Away(EntityManager, first, node),
                    ApproachStopSystem.Away(EntityManager, second, node))) return default;
            return new UpgradeDragTarget(node, first, other: second);
        }

        private bool TryApproachPreview(Entity node, Entity road, out Bezier4x3 path)
        {
            path = default;
            if (road == Entity.Null || !EntityManager.HasComponent<Curve>(road) || !EntityManager.HasComponent<Edge>(road)) return false;
            var edge = EntityManager.GetComponentData<Edge>(road);
            path = MathUtils.Cut(EntityManager.GetComponentData<Curve>(road).m_Bezier,
                edge.m_Start == node ? new float2(0, .15f) : new float2(.85f, 1));
            return true;
        }

        private void AddApproach(UpgradeDragTarget target)
        {
            if (!TryApproachPreview(target.Owner, target.Part, out var selected)) return;
            var opposite = default(Bezier4x3);
            if (target.Other != Entity.Null && !TryApproachPreview(target.Owner, target.Other, out opposite)) return;
            if (!_drag.Add(target, default, 0f)) return;
            _drag.AddHighlight(selected, 2f);
            if (target.Other != Entity.Null) _drag.AddHighlight(opposite, 2f);
        }

        private void ApplyDragTarget(UpgradeDragTarget target, bool remove)
        {
            var node = target.Owner;
            var road = target.Part;
            var opposite = target.Other;
            if (!UpgradeDragTarget.Live(EntityManager, node) || EntityManager.HasComponent<TrafficLights>(node)
                || !EntityManager.HasBuffer<ConnectedEdge>(node) || !Connected(road)
                || opposite != Entity.Null && !Connected(opposite)) return;

            using (var roads = EntityManager.GetBuffer<ConnectedEdge>(node, true).ToNativeArray(Allocator.Temp))
            {
                if (roads.Length < 3) return;
                bool allWay=EntityManager.HasComponent<Upgraded>(node)&&(EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General&CompositionFlags.General.AllWayStop)!=0;
                if(!EntityManager.HasBuffer<MGRStopApproach>(node))EntityManager.AddBuffer<MGRStopApproach>(node);
                // Finish structural changes before borrowing a dynamic buffer.
                if(allWay){var upgrade=EntityManager.GetComponentData<Upgraded>(node);upgrade.m_Flags.m_General&=~CompositionFlags.General.AllWayStop;EntityManager.SetComponentData(node,upgrade);}
                foreach(var entry in roads)
                {
                    if (!Connected(entry.m_Edge) || !EntityManager.HasComponent<Curve>(entry.m_Edge)) continue;
                    var direction=ApproachStopSystem.Away(EntityManager,entry.m_Edge,node);bool selectedApproach=entry.m_Edge==road||entry.m_Edge==opposite;
                    if (!selectedApproach && !allWay) continue;
                    var choices = EntityManager.GetBuffer<MGRStopApproach>(node);
                    for (int i = choices.Length - 1; i >= 0; i--)
                        if (ApproachStopSystem.MatchApproach(EntityManager, node, choices[i].Outward) == entry.m_Edge)
                            choices.RemoveAt(i);
                    choices.Add(new MGRStopApproach { Outward = direction,
                        Stop = (byte)(selectedApproach ? (remove ? 0 : Yield ? 2 : 1) : 1) });
                    if(!EntityManager.HasComponent<Updated>(entry.m_Edge))EntityManager.AddComponent<Updated>(entry.m_Edge);
                    var endpoints=EntityManager.GetComponentData<Edge>(entry.m_Edge);
                    RefreshEndpoint(endpoints.m_Start);
                    RefreshEndpoint(endpoints.m_End);
                }
            }
            if(!EntityManager.HasComponent<Updated>(node))EntityManager.AddComponent<Updated>(node);
            World.GetOrCreateSystemManaged<ApproachStopSystem>().Queue(node);

            bool Connected(Entity approach)
            {
                if (!UpgradeDragTarget.Live(EntityManager, approach) || !EntityManager.HasComponent<Road>(approach)
                    || !EntityManager.HasComponent<Edge>(approach)) return false;
                var edge = EntityManager.GetComponentData<Edge>(approach);
                return edge.m_Start == node || edge.m_End == node;
            }
        }
    }
}
