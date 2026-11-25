using System;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Entities;

using Unity.Collections;

using Unity.Jobs;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    public struct MGRFarSideLights : IComponentData, IEmptySerializable { }

    public partial class FarSideLightUpgradeTool : ToolBaseSystem

    {

        internal const string AssetName = "MGR Far Side Traffic Lights";

        private PrefabBase _selected;
        private readonly UpgradeDrag _drag = new UpgradeDrag();

        public override string toolID => "MGRFarSideLightUpgrade";

        public override PrefabBase GetPrefab() => _selected;

        public override bool TrySetPrefab(PrefabBase prefab) { if (prefab == null || prefab.name != AssetName && !GardenCityLightPolicy.IsAsset(prefab.name)) return false; if (_selected != prefab) { _selected = prefab; m_ToolSystem.EventPrefabChanged?.Invoke(prefab); } return true; }

        protected override void OnStartRunning() { base.OnStartRunning(); applyAction.shouldBeEnabled = true; secondaryApplyAction.shouldBeEnabled = true; cancelAction.shouldBeEnabled = true; }

        protected override void OnStopRunning()
        {
            _drag.Reset();
            base.OnStopRunning();
        }

        public override void InitializeRaycast() { base.InitializeRaycast(); m_ToolRaycastSystem.typeMask = TypeMask.Net; m_ToolRaycastSystem.netLayerMask = Layer.Road; m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground; }

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
                secondaryApplyAction.WasPressedThisFrame(), applyAction.IsPressed(), secondaryApplyAction.IsPressed(), rawPrimary: true);
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
            var node = hit.m_Owner;

            if (EntityManager.HasComponent<Temp>(node)) node = EntityManager.GetComponentData<Temp>(node).m_Original;

            for (int i = 0; i < 8 && !EntityManager.HasComponent<Node>(node) && !EntityManager.HasComponent<Edge>(node) && EntityManager.HasComponent<Owner>(node); i++) node = EntityManager.GetComponentData<Owner>(node).m_Owner;

            if (EntityManager.HasComponent<Edge>(node))

            {

                var edge = EntityManager.GetComponentData<Edge>(node); bool end;

                if (EntityManager.HasComponent<EdgeGeometry>(node)) end = NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(node), hit.m_Hit.m_HitPosition);

                else if (EntityManager.HasComponent<Curve>(node)) { MathUtils.Distance(EntityManager.GetComponentData<Curve>(node).m_Bezier, hit.m_Hit.m_HitPosition, out float t); end = t > .5f; } else return deps;

                node = end ? edge.m_End : edge.m_Start;

            }

            if (!EntityManager.HasComponent<Node>(node) || EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node) || !EntityManager.HasBuffer<ConnectedEdge>(node)) return deps;

            var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>(); var buffer = overlay.GetBuffer(out var pending); pending.Complete(); bool roadFound = false;

            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(node, true))

            {

                var road = entry.m_Edge; if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<Curve>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Temp>(road)) continue;

                roadFound = true; var edge = EntityManager.GetComponentData<Edge>(road); var range = edge.m_Start == node ? new float2(0, .12f) : new float2(.88f, 1);

                if (!_drag.Anchored)
                    buffer.DrawCurve(new UnityEngine.Color(.15f, 1f, .3f, .8f), MathUtils.Cut(EntityManager.GetComponentData<Curve>(road).m_Bezier, range), 2f);

            }

            if (roadFound && _drag.RouteTo(EntityManager, new UpgradeDragTarget(node), hit.m_Hit.m_HitPosition))
            {
                _drag.AddNode(EntityManager, _drag.Anchor.Owner);
                var steps = _drag.Steps;
                for (int i = 0; i < steps.Count; i++) _drag.AddNode(EntityManager, steps[i].To);
            }
            return deps;
        }

        private void ApplyDragTarget(UpgradeDragTarget target, bool remove)
        {
            var node = target.Owner;
            if (!UpgradeDragTarget.Live(EntityManager, node) || !EntityManager.HasComponent<Node>(node)) return;
            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return;
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("signal.click",_selected.name+" node="+node+" remove="+remove);
            Mod.Log.Info("Signal request: "+_selected.name+" node="+node+" remove="+remove);

            bool plainFromCustom=!remove&&!GardenCityLightPolicy.IsAsset(_selected.name)&&EntityManager.HasComponent<MGRGardenCitySignals>(node);
            if(plainFromCustom)
            {
                EntityManager.RemoveComponent<MGRGardenCitySignals>(node);
                if(EntityManager.HasComponent<MGRSignalStyle>(node))EntityManager.RemoveComponent<MGRSignalStyle>(node);
            }
            if(GardenCityLightPolicy.IsAsset(_selected.name))
            {
                if(!remove&&!GardenCityLightPolicy.ReadyStyle(World.GetOrCreateSystemManaged<PrefabSystem>(),EntityManager,_selected.name)){UpgradeFeedback.Report(_selected.name,"The selected signal assets are not ready.");return;}
                if(!remove)
                {if(!EntityManager.HasComponent<MGRGardenCitySignals>(node))EntityManager.AddComponent<MGRGardenCitySignals>(node);var style=new MGRSignalStyle{Value=GardenCityLightPolicy.Style(_selected.name)};if(EntityManager.HasComponent<MGRSignalStyle>(node))EntityManager.SetComponentData(node,style);else EntityManager.AddComponentData(node,style);}

            }
            if(remove) { if(EntityManager.HasComponent<MGRGardenCitySignals>(node))EntityManager.RemoveComponent<MGRGardenCitySignals>(node); if(EntityManager.HasComponent<MGRSignalStyle>(node))EntityManager.RemoveComponent<MGRSignalStyle>(node); }
            bool has=EntityManager.HasComponent<MGRFarSideLights>(node);

            bool customStyle=GardenCityLightPolicy.IsAsset(_selected.name);
            bool hadUpgrade=EntityManager.HasComponent<Upgraded>(node);
            var upgrade=hadUpgrade?EntityManager.GetComponentData<Upgraded>(node):default;
            bool nativeChange=SignalUpgradePolicy.NeedsNativeRebuild(remove,EntityManager.HasComponent<TrafficLights>(node),upgrade.m_Flags.m_General);

            // Every style, including plain far-side placement, records the
            // explicit control choice for subsequent native road updates.
            var previousFlags=upgrade.m_Flags;
            upgrade.m_Flags.m_General=SignalUpgradePolicy.SetSignalControl(previousFlags.m_General,remove);
            if(!hadUpgrade)EntityManager.AddComponent<Upgraded>(node);
            if(!hadUpgrade||!upgrade.m_Flags.Equals(previousFlags))EntityManager.SetComponentData(node,upgrade);

            if(nativeChange)
            {
                var endpoints = NativeSignalRebuild.Request(EntityManager, node);
                foreach (var endpoint in endpoints)
                    if (EntityManager.HasBuffer<MGRStopApproach>(endpoint))
                        World.GetOrCreateSystemManaged<ApproachStopSystem>().Queue(endpoint);
            }

            if(remove){if(has)EntityManager.RemoveComponent<MGRFarSideLights>(node);}

            else if(!has)EntityManager.AddComponent<MGRFarSideLights>(node);

            else if(!SignalUpgradePolicy.NeedsVisualRefresh(nativeChange,customStyle,plainFromCustom,has))return;

            FarSideLightSystem.Queue(World,node);
            UpgradeFeedback.Report(_selected.name,remove?"Traffic lights removed from junction.":"Signal upgrade applied to junction; placement queued.");

        }

    }

}
