using Colossal.Serialization.Entities;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Entities;

using Unity.Jobs;

namespace MedianGrassReplacement.Systems

{

    public struct RDCUpgrade : IComponentData, ISerializable

    {

        public byte Mask;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Mask); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Mask); }

    }

    internal static class DecalPolicy

    {

        internal static byte Apply(byte existing, byte selection, bool remove) => (byte)((remove ? existing & ~selection : existing | selection) & 3);

        internal static byte Classify(bool decal, bool laneDirection, bool speedSign, string name)

        {

            if (!decal) return 0;

            if (laneDirection) return 1;

            if (speedSign || (!string.IsNullOrEmpty(name) && name.Replace(" ", "").Replace("_", "").Replace("-", "").IndexOf("speedlimit", System.StringComparison.OrdinalIgnoreCase) >= 0)) return 2;

            return 0;

        }

    }

    public partial class DecalUpgradeTool : ToolBaseSystem

    {

        private PrefabBase _selected;
        private Entity previewNode;private byte previewStyle;private float previewUntil;
        private System.Collections.Generic.List<Colossal.Mathematics.Bezier4x3> nodePaths=new System.Collections.Generic.List<Colossal.Mathematics.Bezier4x3>();

        public override string toolID => "RDCRoadUpgrade";

        public override PrefabBase GetPrefab() => _selected;

        public override bool TrySetPrefab(PrefabBase prefab)

        {

            if (prefab == null || (UpgradeAssetsSystem.Mask(prefab.name) == 0 && prefab.name != RoadSideUpgrades.DecalAsset && prefab.name != VisualPowerlineSystem.AssetName && prefab.name != ParkingBanPolicy.AssetName && prefab.name != CenterlineVisibility.AssetName && CenterlineUpgradePolicy.Style(prefab.name)==0 && prefab.name!=StreetLightingSystem.AssetName && LaneDividerPolicy.Style(prefab.name)==0 && EdgeUpgradePolicy.Style(prefab.name)==0 && !SegmentUpgradePolicy.Known(prefab.name) && prefab.name!=RoadFeatureInspector.AssetName && SurfaceUpgradePolicy.Slot(prefab.name)==0 && prefab.name!=ParkingHatchPolicy.AssetName&&prefab.name!=ParkingHatchPolicy.Yellow&&prefab.name!=LineShapePolicy.Visibility&&LineShapePolicy.Style(prefab.name)==0)) return false;

            if (_selected != prefab) { _selected = prefab; m_ToolSystem.EventPrefabChanged?.Invoke(prefab); }

            return true;

        }

        protected override void OnStartRunning() { base.OnStartRunning(); _lineHoverUntil=0; previewNode=Entity.Null;previewUntil=0;nodePaths.Clear();applyAction.shouldBeEnabled = true; secondaryApplyAction.shouldBeEnabled = true; cancelAction.shouldBeEnabled = true; }

        public override void InitializeRaycast() { base.InitializeRaycast(); m_ToolRaycastSystem.typeMask = TypeMask.Net; m_ToolRaycastSystem.netLayerMask = Layer.Road; m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground; }

        protected override JobHandle OnUpdate(JobHandle deps)

        {

            if(m_ToolSystem.activeTool!=this || Mod.Saving) return deps;

            bool remove = UpgradeInputPolicy.RemovePressed(secondaryApplyAction.WasPressedThisFrame());

            if (UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame())) { m_ToolSystem.activeTool = m_DefaultToolSystem; return deps; }

            if (!m_HasFocus || UpgradeInputPolicy.PointerBlocked || !m_ToolRaycastSystem.GetRaycastResult(out var hit)) return deps;

            deps.Complete();
            if(HandleHoverLine(EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity)?hit.m_Hit.m_HitEntity:hit.m_Owner,hit.m_Hit.m_HitPosition,remove))return default;
            byte edgeStyle=EdgeUpgradePolicy.Style(_selected?.name);
            byte dividerStyle=LaneDividerPolicy.Style(_selected?.name);bool dividerTool=dividerStyle!=0;
            byte nodeCenterStyle=CenterlineUpgradePolicy.Style(_selected?.name);
            bool nodeVisibility=SegmentUpgradePolicy.Selection(_selected?.name)!=0||_selected?.name==CenterlineVisibility.AssetName||_selected?.name==SegmentUpgradePolicy.ResetLines;
            if((edgeStyle!=0||dividerTool||nodeCenterStyle!=0||nodeVisibility)&&StreetLightingSystem.TryLightTarget(EntityManager,EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity)?hit.m_Hit.m_HitEntity:hit.m_Owner,out var lineTarget)&&EntityManager.HasComponent<Node>(lineTarget))
            {
                var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();emitter.RetainPaintTarget(lineTarget);
                var overlayNode=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var nodeJob);nodeJob.Complete();
                overlayNode.DrawCircle(new UnityEngine.Color(.3f,.8f,1,.8f),EntityManager.GetComponentData<Node>(lineTarget).m_Position,4f);
                byte requested=edgeStyle!=0?edgeStyle:nodeCenterStyle!=0?(byte)(6+nodeCenterStyle):(byte)(16+dividerStyle);
                if(previewNode!=lineTarget||previewStyle!=requested||UnityEngine.Time.realtimeSinceStartup>=previewUntil){previewNode=lineTarget;previewStyle=requested;previewUntil=UnityEngine.Time.realtimeSinceStartup+.25f;nodePaths=nodeVisibility?new System.Collections.Generic.List<Colossal.Mathematics.Bezier4x3>():nodeCenterStyle!=0?emitter.NodeCenterPlan(lineTarget,nodeCenterStyle):dividerTool?emitter.NodeDividerPreview(lineTarget,dividerStyle):emitter.JunctionEdgePreview(lineTarget,edgeStyle);}
                var color=(nodeCenterStyle!=0?CenterlineUpgradePolicy.YellowPaint(nodeCenterStyle,Mod.Settings.YellowToolPaint):(dividerTool?LaneDividerPolicy.IsYellow(dividerStyle):EdgeUpgradePolicy.IsYellow(edgeStyle)))?new UnityEngine.Color(1,.8f,.15f,.9f):new UnityEngine.Color(1,1,1,.9f);
                foreach(var path in nodePaths){if(EdgeUpgradePolicy.Paired(edgeStyle)){overlayNode.DrawCurve(color,NetUtils.OffsetCurveLeftSmooth(path,-emitter.EdgeSpacing(edgeStyle)),.12f);overlayNode.DrawCurve(color,NetUtils.OffsetCurveLeftSmooth(path,emitter.EdgeSpacing(edgeStyle)),.12f);}else overlayNode.DrawCurve(color,path,.12f);}
                if(applyAction.WasPressedThisFrame()||remove){if(nodeVisibility)World.GetOrCreateSystemManaged<SegmentUpgradeSystem>().ApplyNode(lineTarget,_selected.name,remove);else if(nodeCenterStyle!=0)emitter.ApplyNodeCenter(lineTarget,nodeCenterStyle,remove);else if(edgeStyle!=0)emitter.ApplyEdge(lineTarget,true,true,edgeStyle,remove);else emitter.ApplyNodeDividers(lineTarget,remove,dividerStyle);}
                return default;
            }
            if(_selected?.name==StreetLightingSystem.AssetName&&StreetLightingSystem.TryLightTarget(EntityManager,EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity)?hit.m_Hit.m_HitEntity:hit.m_Owner,out var lightTarget)&&EntityManager.HasComponent<Node>(lightTarget))
            {
                var lights=World.GetOrCreateSystemManaged<StreetLightingSystem>();
                var nodeOverlay=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var nodePending);nodePending.Complete();
                nodeOverlay.DrawCircle(new UnityEngine.Color(.3f,.8f,1,.8f),EntityManager.GetComponentData<Node>(lightTarget).m_Position,4f);
                if(applyAction.WasPressedThisFrame()||remove)lights.ApplyNode(lightTarget,!remove);
                return default;
            }
            if(!SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var road))return default;

            for (int i = 0; i < 8 && !EntityManager.HasComponent<Road>(road) && EntityManager.HasComponent<Owner>(road); i++) road = EntityManager.GetComponentData<Owner>(road).m_Owner;

            if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Curve>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Temp>(road)) return default;

            World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RetainPaintTarget(road);
            bool wear = _selected?.name == RoadSideUpgrades.DecalAsset;bool ban=_selected?.name==ParkingBanPolicy.AssetName;bool hideCenter=_selected?.name==CenterlineVisibility.AssetName;byte centerStyle=CenterlineUpgradePolicy.Style(_selected?.name);

            bool left = RoadSideUpgrades.IsLeft(EntityManager, road, hit.m_Hit.m_HitPosition);

            var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>(); var buffer = overlay.GetBuffer(out var pending); pending.Complete(); if(!dividerTool)buffer.DrawCurve(new UnityEngine.Color(.15f, 1f, .3f, .8f), (hideCenter || wear && Mod.Settings.DecalsBothSides) ? EntityManager.GetComponentData<Curve>(road).m_Bezier : RoadSideUpgrades.Preview(EntityManager, road, left), 2f);

            if(_selected?.name==RoadFeatureInspector.AssetName)return default;
            if (applyAction.WasPressedThisFrame() || remove)

            {

                if(_selected?.name==ParkingHatchPolicy.AssetName||_selected?.name==ParkingHatchPolicy.Yellow){World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().ApplyHatching(road,left,remove,_selected?.name==ParkingHatchPolicy.Yellow);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);return default;}
                if(SegmentUpgradePolicy.Known(_selected?.name)) {World.GetOrCreateSystemManaged<SegmentUpgradeSystem>().Apply(road,_selected.name,remove);return default;}
                if(edgeStyle!=0){World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().ApplyEdge(road,false,left,edgeStyle,remove);return default;}
                if(dividerTool)
                {
                    var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                    if(!remove&&!emitter.TryDividerPlan(road,out _)){UpgradeFeedback.Report(_selected.name,"No adjacent same-direction traffic lanes on this section.");return default;}
                    World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
                    if(!EntityManager.HasComponent<MGRLaneDivider>(road))EntityManager.AddComponent<MGRLaneDivider>(road);
                    EntityManager.SetComponentData(road,new MGRLaneDivider{Value=remove?(byte)2:dividerStyle});
                    CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                    UpgradeFeedback.Report(_selected.name,remove?"Interior lane lines removed; center and edge lines kept.":LaneDividerPolicy.Label(dividerStyle)+" applied; center and edge lines kept.");return default;
                }
                if(centerStyle!=0)
                {
                    World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
                    byte old=CenterlineUpgradePolicy.Mode(EntityManager,road);
                    if(!remove&&!World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().TryCenterUpgradePlan(road,centerStyle,out _,out var reason)){UpgradeFeedback.Report(_selected.name,reason);return default;}
                    byte next=remove?CenterlineUpgradePolicy.Removed:centerStyle;
                    if(next!=old){if(next==0)EntityManager.RemoveComponent<MGRCenterlineStyle>(road);else {if(!EntityManager.HasComponent<MGRCenterlineStyle>(road))EntityManager.AddComponent<MGRCenterlineStyle>(road);EntityManager.SetComponentData(road,new MGRCenterlineStyle{Value=next});}}
                    if(EntityManager.HasComponent<MGRHideCenterline>(road))EntityManager.RemoveComponent<MGRHideCenterline>(road);
                    CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                    UpgradeFeedback.Report(_selected.name,remove?"Centerline removed. Use Restore line defaults to restore native markings.":CenterlineUpgradePolicy.Pattern(centerStyle)==2?"Double centerline replacement queued.":"Single centerline replacement queued.");return default;
                }
                if(_selected?.name==StreetLightingSystem.AssetName){World.GetOrCreateSystemManaged<StreetLightingSystem>().Apply(road,!remove);return default;}
                if(hideCenter)
                {
                    if(!remove&&EntityManager.HasComponent<MGRCenterlineStyle>(road))EntityManager.RemoveComponent<MGRCenterlineStyle>(road);
                    bool has=EntityManager.HasComponent<MGRHideCenterline>(road);
                    if(has==remove)
                    {
                        if(remove)EntityManager.RemoveComponent<MGRHideCenterline>(road);else EntityManager.AddComponent<MGRHideCenterline>(road);
                        CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                    }
                    return default;
                }
                if(ban)
                {
                    World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
                    byte old=EntityManager.HasComponent<MGRParkingBanSides>(road)?EntityManager.GetComponentData<MGRParkingBanSides>(road).Mask:(byte)0;
                    byte next=RoadSideUpgrades.SetMask(old,left,remove);
                    if(next!=old)
                    {
                        if(next==0)EntityManager.RemoveComponent<MGRParkingBanSides>(road);
                        else {if(!EntityManager.HasComponent<MGRParkingBanSides>(road))EntityManager.AddComponent<MGRParkingBanSides>(road);EntityManager.SetComponentData(road,new MGRParkingBanSides{Mask=next});}
                        
                        FixedParkingSystem.QueueRoad(road);CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                    }
                    UpgradeFeedback.Report(_selected.name,remove?"No parking removed on this side; prior parking style restored.":"No parking applied on this side; bay parking is suspended here.");
                    return default;
                }

                if(_selected?.name==VisualPowerlineSystem.AssetName)

                {

                    byte old=EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(road)?EntityManager.GetComponentData<MGRVisualPowerlineUpgrade>(road).Sides:(byte)0;

                    byte next=RoadSideUpgrades.SetMask(old,left,remove);

                    if(next!=old)

                    {

                        if(next==0) EntityManager.RemoveComponent<MGRVisualPowerlineUpgrade>(road);

                        else {if(!EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(road)) EntityManager.AddComponent<MGRVisualPowerlineUpgrade>(road);EntityManager.SetComponentData(road,new MGRVisualPowerlineUpgrade {Sides=next});}

                        World.GetOrCreateSystemManaged<VisualPowerlineSystem>().QueueRoad(road,remove);

                    }

                    if(remove&&next==old)World.GetOrCreateSystemManaged<VisualPowerlineSystem>().QueueRoad(road,true);
                    return default;

                }

                if (wear)

                {

                    byte oldSides = EntityManager.HasComponent<MGRCurbDecalSides>(road) ? EntityManager.GetComponentData<MGRCurbDecalSides>(road).Mask : (byte)0;

                    byte sides = RoadSideUpgrades.SelectDecals(oldSides, left, remove, Mod.Settings.DecalsBothSides);

                    if (sides != oldSides)

                    {

                        if (sides == 0) EntityManager.RemoveComponent<MGRCurbDecalSides>(road);

                        else { if (!EntityManager.HasComponent<MGRCurbDecalSides>(road)) EntityManager.AddComponent<MGRCurbDecalSides>(road); EntityManager.SetComponentData(road, new MGRCurbDecalSides { Mask = sides }); }

                        World.GetOrCreateSystemManaged<CurbDecalSystem>().QueueRoad(road);

                    }

                    return default;

                }

                var original = RoadSideUpgrades.Decals(EntityManager, road);

                var changed = RoadSideUpgrades.SetDecals(original, left, UpgradeAssetsSystem.Mask(_selected?.name), remove);

                if (changed.Left != original.Left || changed.Right != original.Right)

                {

                    byte summary = (byte)(changed.Left | changed.Right);

                    if (summary == 0)

                    {

                        if (EntityManager.HasComponent<RDCUpgrade>(road)) EntityManager.RemoveComponent<RDCUpgrade>(road);

                        if (EntityManager.HasComponent<RDCSideUpgrade>(road)) EntityManager.RemoveComponent<RDCSideUpgrade>(road);

                    }

                    else

                    {

                        if (!EntityManager.HasComponent<RDCSideUpgrade>(road)) EntityManager.AddComponent<RDCSideUpgrade>(road);

                        EntityManager.SetComponentData(road, changed);

                        if (!EntityManager.HasComponent<RDCUpgrade>(road)) EntityManager.AddComponent<RDCUpgrade>(road);

                        EntityManager.SetComponentData(road, new RDCUpgrade { Mask = summary });

                    }

                    DecalVisibilitySystem.QueueRoad(road);

                }

            }

            return default;

        }

    }

}

