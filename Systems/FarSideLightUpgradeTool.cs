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

        public override string toolID => "MGRFarSideLightUpgrade";

        public override PrefabBase GetPrefab() => _selected;

        public override bool TrySetPrefab(PrefabBase prefab) { if (prefab == null || prefab.name != AssetName && !GardenCityLightPolicy.IsAsset(prefab.name)) return false; if (_selected != prefab) { _selected = prefab; m_ToolSystem.EventPrefabChanged?.Invoke(prefab); } return true; }

        protected override void OnStartRunning() { base.OnStartRunning(); applyAction.shouldBeEnabled = true; secondaryApplyAction.shouldBeEnabled = true; cancelAction.shouldBeEnabled = true; }

        public override void InitializeRaycast() { base.InitializeRaycast(); m_ToolRaycastSystem.typeMask = TypeMask.Net; m_ToolRaycastSystem.netLayerMask = Layer.Road; m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground; }

        protected override JobHandle OnUpdate(JobHandle deps)

        {

            if(m_ToolSystem.activeTool!=this || Mod.Saving) return deps;

            bool remove = UpgradeInputPolicy.RemovePressed(secondaryApplyAction.WasPressedThisFrame());

            if (UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame())) { m_ToolSystem.activeTool = m_DefaultToolSystem; return deps; }

            if (!m_HasFocus || UpgradeInputPolicy.PointerBlocked || !m_ToolRaycastSystem.GetRaycastResult(out var hit)) return deps;

            deps.Complete(); var node = hit.m_Owner;

            if (EntityManager.HasComponent<Temp>(node)) node = EntityManager.GetComponentData<Temp>(node).m_Original;

            for (int i = 0; i < 8 && !EntityManager.HasComponent<Node>(node) && !EntityManager.HasComponent<Edge>(node) && EntityManager.HasComponent<Owner>(node); i++) node = EntityManager.GetComponentData<Owner>(node).m_Owner;

            if (EntityManager.HasComponent<Edge>(node))

            {

                var edge = EntityManager.GetComponentData<Edge>(node); bool end;

                if (EntityManager.HasComponent<EdgeGeometry>(node)) end = NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(node), hit.m_Hit.m_HitPosition);

                else if (EntityManager.HasComponent<Curve>(node)) { MathUtils.Distance(EntityManager.GetComponentData<Curve>(node).m_Bezier, hit.m_Hit.m_HitPosition, out float t); end = t > .5f; } else return default;

                node = end ? edge.m_End : edge.m_Start;

            }

            if (!EntityManager.HasComponent<Node>(node) || EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node) || !EntityManager.HasBuffer<ConnectedEdge>(node)) return default;

            var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>(); var buffer = overlay.GetBuffer(out var pending); pending.Complete(); bool roadFound = false;

            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(node, true))

            {

                var road = entry.m_Edge; if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<Curve>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Temp>(road)) continue;

                roadFound = true; var edge = EntityManager.GetComponentData<Edge>(road); var range = edge.m_Start == node ? new float2(0, .12f) : new float2(.88f, 1);

                buffer.DrawCurve(new UnityEngine.Color(.15f, 1f, .3f, .8f), MathUtils.Cut(EntityManager.GetComponentData<Curve>(road).m_Bezier, range), 2f);

            }

            bool apply=applyAction.WasPressedThisFrame() || UnityEngine.InputSystem.Mouse.current?.leftButton.wasPressedThisFrame==true;
            if (!roadFound || (!apply && !remove)) return default;
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("signal.click",_selected.name+" node="+node+" remove="+remove);
            Mod.Log.Info("Signal request: "+_selected.name+" node="+node+" remove="+remove);

            if(GardenCityLightPolicy.IsAsset(_selected.name))
            {
                if(!remove&&!GardenCityLightPolicy.Ready(World.GetOrCreateSystemManaged<PrefabSystem>(),EntityManager)){UpgradeFeedback.Report(_selected.name,"Garden City signal assets are not ready.");return default;}
                if(!remove)
                {if(!EntityManager.HasComponent<MGRGardenCitySignals>(node))EntityManager.AddComponent<MGRGardenCitySignals>(node);var style=new MGRSignalStyle{Value=GardenCityLightPolicy.Style(_selected.name)};if(EntityManager.HasComponent<MGRSignalStyle>(node))EntityManager.SetComponentData(node,style);else EntityManager.AddComponentData(node,style);}

            }
            if(remove) { if(EntityManager.HasComponent<MGRGardenCitySignals>(node))EntityManager.RemoveComponent<MGRGardenCitySignals>(node); if(EntityManager.HasComponent<MGRSignalStyle>(node))EntityManager.RemoveComponent<MGRSignalStyle>(node); }
            bool has=EntityManager.HasComponent<MGRFarSideLights>(node);

            bool nativeChange=remove||!EntityManager.HasComponent<TrafficLights>(node)||GardenCityLightPolicy.IsAsset(_selected.name);

            if(nativeChange)

            {

                var upgrade=EntityManager.HasComponent<Upgraded>(node)?EntityManager.GetComponentData<Upgraded>(node):default;

                if(remove){upgrade.m_Flags.m_General&=~CompositionFlags.General.TrafficLights;upgrade.m_Flags.m_General|=CompositionFlags.General.RemoveTrafficLights;}

                else{upgrade.m_Flags.m_General|=CompositionFlags.General.TrafficLights;upgrade.m_Flags.m_General&=~CompositionFlags.General.RemoveTrafficLights;}

                if(!EntityManager.HasComponent<Upgraded>(node))EntityManager.AddComponent<Upgraded>(node);

                EntityManager.SetComponentData(node,upgrade);

                using(var edges=EntityManager.GetBuffer<ConnectedEdge>(node,true).ToNativeArray(Allocator.Temp))foreach(var entry in edges)

                    if(EntityManager.Exists(entry.m_Edge)&&!EntityManager.HasComponent<Deleted>(entry.m_Edge)&&!EntityManager.HasComponent<Updated>(entry.m_Edge))EntityManager.AddComponent<Updated>(entry.m_Edge);

                if(!EntityManager.HasComponent<Updated>(node))EntityManager.AddComponent<Updated>(node);

            }

            if(remove){if(has)EntityManager.RemoveComponent<MGRFarSideLights>(node);}

            else if(!has)EntityManager.AddComponent<MGRFarSideLights>(node);

            else if(!nativeChange&&!GardenCityLightPolicy.IsAsset(_selected.name))return default;

            FarSideLightSystem.Queue(node);
            UpgradeFeedback.Report(_selected.name,remove?"Traffic lights removed from junction.":"Signal upgrade applied to junction; placement queued.");

            return default;

        }

    }

}
