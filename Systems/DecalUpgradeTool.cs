using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Unity.Mathematics;

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

    public partial class DecalUpgradeTool : ToolBaseSystem

    {

        private PrefabBase _selected;
        private readonly UpgradeDrag _drag = new UpgradeDrag();
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

        protected override void OnStopRunning()
        {
            _drag.Reset();
            _placedDividerDragPaths.Clear();
            base.OnStopRunning();
        }

        public override void InitializeRaycast() { base.InitializeRaycast(); m_ToolRaycastSystem.typeMask = _selected?.name == RoadFeatureInspector.AssetName
            ? TypeMask.Net | TypeMask.Terrain : TypeMask.Net; m_ToolRaycastSystem.netLayerMask = Layer.Road; m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground; }

        private JobHandle InspectRoad(JobHandle deps)
        {
            bool worldInput = UnityEngine.Application.isFocused && Game.Input.InputManager.instance.controlOverWorld
                && !UpgradeInputPolicy.PointerBlocked;
            if (!worldInput) return deps;

            if (!m_ToolRaycastSystem.GetRaycastResult(out var hit)) return deps;

            var point = hit.m_Hit.m_HitPosition;
            bool direct = SectionUpgradeTargeting.TryMarking(EntityManager, hit.m_Hit.m_HitEntity, hit.m_Owner, point, out var target);

            if (direct)
            {
                World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RetainPaintTarget(target);
                var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var pending);
                pending.Complete();
                if (EntityManager.HasComponent<Node>(target))
                    overlay.DrawCircle(new UnityEngine.Color(.3f,.8f,1,.8f),
                        EntityManager.GetComponentData<Node>(target).m_Position,4f);
                else if (EntityManager.HasComponent<Curve>(target))
                {
                    bool left = RoadSideUpgrades.IsLeft(EntityManager, target, point);
                    byte sides = RoadSideUpgrades.PreviewSides(_selected.name, left, Mod.Settings.DecalsBothSides);
                    var color = new UnityEngine.Color(.15f,1f,.3f,.8f);
                    if ((sides & 1) != 0) overlay.DrawCurve(color, RoadSideUpgrades.Preview(EntityManager, target, true), 2f);
                    if ((sides & 2) != 0) overlay.DrawCurve(color, RoadSideUpgrades.Preview(EntityManager, target, false), 2f);
                }
            }
            return deps;
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
            if (_selected?.name == RoadFeatureInspector.AssetName)
            {
                _drag.Reset();
                return InspectRoad(deps);
            }
            var phase = _drag.Read(_selected, m_HasFocus, applyAction.WasPressedThisFrame(),
                secondaryApplyAction.WasPressedThisFrame(), applyAction.IsPressed(), secondaryApplyAction.IsPressed());
            if (phase == UpgradeDrag.Phase.Commit)
            {
                if (_drag.Count != 0) { deps.Complete(); deps = default; }
                _drag.Commit(ApplyDragTarget);
                _placedDividerDragPaths.Clear();
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
            var markingHit=SectionUpgradeTargeting.TryMarking(EntityManager,hit.m_Hit.m_HitEntity,hit.m_Owner,hit.m_Hit.m_HitPosition,out var markingTarget)?markingTarget:hit.m_Owner;
            if(HandleHoverLine(markingHit,hit.m_Hit.m_HitPosition))return deps;
            byte edgeStyle=EdgeUpgradePolicy.Style(_selected?.name);
            byte dividerStyle=LaneDividerPolicy.Style(_selected?.name);bool dividerTool=dividerStyle!=0;
            byte nodeCenterStyle=CenterlineUpgradePolicy.Style(_selected?.name);
            bool nodeVisibility=SegmentUpgradePolicy.Selection(_selected?.name)!=0||_selected?.name==CenterlineVisibility.AssetName||_selected?.name==SegmentUpgradePolicy.ResetLines;
            bool streetLights = _selected?.name == StreetLightingSystem.AssetName;
            bool supportsNode = edgeStyle != 0 || dividerTool || nodeCenterStyle != 0 || nodeVisibility || streetLights;
            var position = hit.m_Hit.m_HitPosition;
            if (_drag.Anchored && !_drag.StartsOnNode && !streetLights && EntityManager.HasComponent<Node>(markingHit))
            {
                PlanSectionRoute(markingHit, position);
                return deps;
            }
            bool useNode = supportsNode && (!_drag.Anchored || _drag.StartsOnNode || streetLights);
            var lineTarget = Entity.Null;
            if (useNode)
            {
                var raw = EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity) ? hit.m_Hit.m_HitEntity : hit.m_Owner;
                if (!StreetLightingSystem.TryLightTarget(EntityManager, raw, out lineTarget) || !EntityManager.HasComponent<Node>(lineTarget))
                {
                    lineTarget = Entity.Null;
                    if (_drag.StartsOnNode && !streetLights && SectionUpgradeTargeting.TryRoad(EntityManager, raw, position, out var nearby))
                        lineTarget = new UpgradeNetworkGraph(EntityManager).NearestNode(nearby, position);
                }
            }
            if (lineTarget != Entity.Null)
            {
                if (_drag.Active)
                {
                    PlanSectionRoute(lineTarget, position);
                    return deps;
                }
                if (streetLights)
                {
                    var lightingOverlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var lightingDeps);
                    lightingDeps.Complete();
                    lightingOverlay.DrawCircle(new UnityEngine.Color(.15f, 1f, .3f, .8f),
                        EntityManager.GetComponentData<Node>(lineTarget).m_Position, 4f);
                    return deps;
                }
                var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();emitter.RetainPaintTarget(lineTarget);
                var overlayNode=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var nodeJob);nodeJob.Complete();
                overlayNode.DrawCircle(new UnityEngine.Color(.3f,.8f,1,.8f),EntityManager.GetComponentData<Node>(lineTarget).m_Position,4f);
                byte requested=edgeStyle!=0?edgeStyle:nodeCenterStyle!=0?(byte)(6+nodeCenterStyle):(byte)(16+dividerStyle);
                if(previewNode!=lineTarget||previewStyle!=requested||UnityEngine.Time.realtimeSinceStartup>=previewUntil){previewNode=lineTarget;previewStyle=requested;previewUntil=UnityEngine.Time.realtimeSinceStartup+.25f;nodePaths=nodeVisibility?new System.Collections.Generic.List<Colossal.Mathematics.Bezier4x3>():nodeCenterStyle!=0?emitter.NodeCenterPlan(lineTarget,nodeCenterStyle):dividerTool?emitter.NodeDividerPreview(lineTarget,dividerStyle):emitter.JunctionEdgePreview(lineTarget,edgeStyle);}
                var color=(nodeCenterStyle!=0?CenterlineUpgradePolicy.YellowPaint(nodeCenterStyle,Mod.Settings.YellowToolPaint):(dividerTool?LaneDividerPolicy.IsYellow(dividerStyle):EdgeUpgradePolicy.IsYellow(edgeStyle)))?new UnityEngine.Color(1,.8f,.15f,.9f):new UnityEngine.Color(1,1,1,.9f);
                foreach(var path in nodePaths){if(EdgeUpgradePolicy.Paired(edgeStyle)){overlayNode.DrawCurve(color,NetUtils.OffsetCurveLeftSmooth(path,-emitter.EdgeSpacing(edgeStyle)),.12f);overlayNode.DrawCurve(color,NetUtils.OffsetCurveLeftSmooth(path,emitter.EdgeSpacing(edgeStyle)),.12f);}else overlayNode.DrawCurve(color,path,.12f);}
                return deps;
            }
            if (_drag.StartsOnNode && !streetLights) return deps;
            if(!SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var road))return deps;

            for (int i = 0; i < 8 && !EntityManager.HasComponent<Road>(road) && EntityManager.HasComponent<Owner>(road); i++) road = EntityManager.GetComponentData<Owner>(road).m_Owner;

            if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Curve>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Temp>(road)) return deps;

            if (_drag.Active)
            {
                PlanSectionRoute(road, position);
                return deps;
            }

            World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RetainPaintTarget(road);
            bool hideCenter=_selected?.name==CenterlineVisibility.AssetName;byte centerStyle=CenterlineUpgradePolicy.Style(_selected?.name);

            bool left = RoadSideUpgrades.IsLeft(EntityManager, road, hit.m_Hit.m_HitPosition);

            var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>(); var buffer = overlay.GetBuffer(out var pending); pending.Complete();
            if(!dividerTool)
            {
                var color=new UnityEngine.Color(.15f,1f,.3f,.8f);
                if(hideCenter||centerStyle!=0)buffer.DrawCurve(color,EntityManager.GetComponentData<Curve>(road).m_Bezier,2f);
                else
                {
                    byte previewSides=RoadSideUpgrades.PreviewSides(_selected?.name,left,Mod.Settings.DecalsBothSides);
                    if((previewSides&1)!=0)buffer.DrawCurve(color,RoadSideUpgrades.Preview(EntityManager,road,true),2f);
                    if((previewSides&2)!=0)buffer.DrawCurve(color,RoadSideUpgrades.Preview(EntityManager,road,false),2f);
                }
            }

            return deps;
        }

        private void PlanSectionRoute(Entity end, float3 position)
        {
            if (!_drag.RouteTo(EntityManager, new UpgradeDragTarget(end), position)) return;
            string asset = _selected.name;
            bool streetLights = asset == StreetLightingSystem.AssetName;
            byte decalSelection = UpgradeAssetsSystem.Mask(asset);
            bool bySide = EdgeUpgradePolicy.Style(asset) != 0 || decalSelection != 0
                || asset == ParkingBanPolicy.AssetName || asset == VisualPowerlineSystem.AssetName
                || asset == ParkingHatchPolicy.AssetName || asset == ParkingHatchPolicy.Yellow
                || (asset == RoadSideUpgrades.DecalAsset && !Mod.Settings.DecalsBothSides);
            var steps = _drag.Steps;
            var emitter = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            byte edgeStyle = EdgeUpgradePolicy.Style(asset);
            byte dividerStyle = LaneDividerPolicy.Style(asset);
            byte centerStyle = CenterlineUpgradePolicy.Style(asset);
            if (_drag.Removing && SegmentUpgradePolicy.Known(asset) && SegmentUpgradePolicy.Selection(asset) == 0) return;
            var palette = World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            if (!_drag.Removing && edgeStyle != 0
                && palette.Explicit(EdgeUpgradePolicy.Paired(edgeStyle) ? (byte)2 : (byte)1, EdgeUpgradePolicy.IsYellow(edgeStyle)) == Entity.Null) return;
            if (_drag.StartsOnNode) AddJunction(_drag.Anchor.Owner);
            bool startLeft = bySide && steps.Count != 0 && RoadSideUpgrades.IsLeft(EntityManager, steps[0].Road, _drag.AnchorPosition);

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (!_drag.StartsOnNode || streetLights)
                {
                    byte side = bySide ? RoadSideUpgrades.SideMask(step.CarrySide(startLeft, steps[0])) : (byte)0;
                    if (decalSelection != 0)
                    {
                        var current = RoadSideUpgrades.Decals(EntityManager, step.Road);
                        if (!DecalPolicy.Changes(side == 1 ? current.Left : current.Right, decalSelection, _drag.Removing)) continue;
                    }
                    if (edgeStyle != 0 && !_drag.Removing && (!RoadSurfacePolicy.IsPaved(EntityManager, step.Road)
                        || emitter.EdgePreview(step.Road, side == 1, edgeStyle).Count == 0)) continue;
                    if (dividerStyle != 0 && !_drag.Removing && !emitter.TryDividerPlan(step.Road, out _)) continue;
                    if (centerStyle != 0 && !_drag.Removing
                        && !emitter.TryCenterUpgradePlan(step.Road, centerStyle, out _, out _)) continue;
                    var preview = side == 0 ? EntityManager.GetComponentData<Curve>(step.Road).m_Bezier
                        : RoadSideUpgrades.Preview(EntityManager, step.Road, side == 1);
                    _drag.AddRoad(step.Road, side, preview);
                }
                if (_drag.StartsOnNode || (streetLights && (i + 1 < steps.Count || step.To == end)))
                    AddJunction(step.To);
            }

            void AddJunction(Entity node)
            {
                var paths = edgeStyle != 0 ? emitter.JunctionEdgePreview(node, edgeStyle)
                    : centerStyle != 0 ? emitter.NodeCenterPlan(node, centerStyle)
                    : dividerStyle != 0 ? emitter.NodeDividerPreview(node, dividerStyle) : null;
                if (!_drag.Removing && (edgeStyle != 0 || centerStyle != 0 || dividerStyle != 0)
                    && (paths == null || paths.Count == 0 || (dividerStyle != 0 && palette.Explicit(1, LaneDividerPolicy.IsYellow(dividerStyle)) == Entity.Null))) return;
                if (paths == null || paths.Count == 0)
                {
                    _drag.AddNode(EntityManager, node);
                    return;
                }
                if (!_drag.Add(new UpgradeDragTarget(node), default, 0f)) return;
                foreach (var path in paths) _drag.AddHighlight(path, .55f);
            }
        }

        private void ApplyDragTarget(UpgradeDragTarget target, bool remove)
        {
            var owner = target.Owner;
            if (!UpgradeDragTarget.Live(EntityManager, owner)) return;
            string asset = _selected?.name;
            if (asset == LineShapePolicy.Visibility || LineShapePolicy.Style(asset) != 0)
            {
                ApplyHoverLine(target, remove);
                return;
            }

            byte edgeStyle = EdgeUpgradePolicy.Style(asset);
            byte dividerStyle = LaneDividerPolicy.Style(asset);
            byte centerStyle = CenterlineUpgradePolicy.Style(asset);
            bool hideCenter = asset == CenterlineVisibility.AssetName;
            if (EntityManager.HasComponent<Node>(owner))
            {
                var emitter = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                if (asset == StreetLightingSystem.AssetName)
                    World.GetOrCreateSystemManaged<StreetLightingSystem>().ApplyNode(owner, !remove);
                else if (SegmentUpgradePolicy.Selection(asset) != 0 || hideCenter || asset == SegmentUpgradePolicy.ResetLines)
                    World.GetOrCreateSystemManaged<SegmentUpgradeSystem>().ApplyNode(owner, asset, remove);
                else if (centerStyle != 0) emitter.ApplyNodeCenter(owner, centerStyle, remove);
                else if (edgeStyle != 0) emitter.ApplyEdge(owner, true, true, edgeStyle, remove);
                else if (dividerStyle != 0) emitter.ApplyNodeDividers(owner, remove, dividerStyle);
                return;
            }

            var road = owner;
            if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Curve>(road)) return;
            bool left = target.Role == 1;
            bool dividerTool = dividerStyle != 0;
            bool wear = asset == RoadSideUpgrades.DecalAsset;
            bool ban = asset == ParkingBanPolicy.AssetName;
            if(_selected?.name==ParkingHatchPolicy.AssetName||_selected?.name==ParkingHatchPolicy.Yellow){World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().ApplyHatching(road,left,remove,_selected?.name==ParkingHatchPolicy.Yellow);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);return;}
            if(SegmentUpgradePolicy.Known(_selected?.name)) {World.GetOrCreateSystemManaged<SegmentUpgradeSystem>().Apply(road,_selected.name,remove);return;}
            if(edgeStyle!=0){World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().ApplyEdge(road,false,left,edgeStyle,remove);return;}
            if(dividerTool)
            {
                var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                if(!remove&&!emitter.TryDividerPlan(road,out _)){UpgradeFeedback.Report(_selected.name,"No adjacent same-direction traffic lanes on this section.");return;}
                World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
                if(!EntityManager.HasComponent<MGRLaneDivider>(road))EntityManager.AddComponent<MGRLaneDivider>(road);
                EntityManager.SetComponentData(road,new MGRLaneDivider{Value=remove?(byte)2:dividerStyle});
                CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                UpgradeFeedback.Report(_selected.name,remove?"Interior lane lines removed; center and edge lines kept.":LaneDividerPolicy.Label(dividerStyle)+" applied; center and edge lines kept.");return;
            }
            if(centerStyle!=0)
            {
                World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
                byte old=CenterlineUpgradePolicy.Mode(EntityManager,road);
                if(!remove&&!World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().TryCenterUpgradePlan(road,centerStyle,out _,out var reason)){UpgradeFeedback.Report(_selected.name,reason);return;}
                byte next=remove?CenterlineUpgradePolicy.Removed:centerStyle;
                if(next!=old){if(next==0)EntityManager.RemoveComponent<MGRCenterlineStyle>(road);else {if(!EntityManager.HasComponent<MGRCenterlineStyle>(road))EntityManager.AddComponent<MGRCenterlineStyle>(road);EntityManager.SetComponentData(road,new MGRCenterlineStyle{Value=next});}}
                if(EntityManager.HasComponent<MGRHideCenterline>(road))EntityManager.RemoveComponent<MGRHideCenterline>(road);
                CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                UpgradeFeedback.Report(_selected.name,remove?"Centerline removed. Use Restore line defaults to restore native markings.":CenterlineUpgradePolicy.Pattern(centerStyle)==2?"Double centerline replacement queued.":"Single centerline replacement queued.");return;
            }
            if(_selected?.name==StreetLightingSystem.AssetName){World.GetOrCreateSystemManaged<StreetLightingSystem>().Apply(road,!remove);return;}
            if(hideCenter)
            {
                if(!remove&&EntityManager.HasComponent<MGRCenterlineStyle>(road))EntityManager.RemoveComponent<MGRCenterlineStyle>(road);
                bool has=EntityManager.HasComponent<MGRHideCenterline>(road);
                if(has==remove)
                {
                    if(remove)EntityManager.RemoveComponent<MGRHideCenterline>(road);else EntityManager.AddComponent<MGRHideCenterline>(road);
                    CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                }
                return;
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

                    FixedParkingSystem.QueueRoad(World,road);CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                }
                UpgradeFeedback.Report(_selected.name,remove?"No parking removed on this side; prior parking style restored.":"No parking applied on this side; bay parking is suspended here.");
                return;
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
                return;

            }

            if (wear)

            {

                byte oldSides = EntityManager.HasComponent<MGRCurbDecalSides>(road) ? EntityManager.GetComponentData<MGRCurbDecalSides>(road).Mask : (byte)0;

                byte sides = RoadSideUpgrades.SelectDecals(oldSides, left, remove, Mod.Settings.DecalsBothSides);

                if (sides != oldSides)

                {

                    if (sides == 0) EntityManager.RemoveComponent<MGRCurbDecalSides>(road);

                    else { if (!EntityManager.HasComponent<MGRCurbDecalSides>(road)) EntityManager.AddComponent<MGRCurbDecalSides>(road); EntityManager.SetComponentData(road, new MGRCurbDecalSides { Mask = sides }); }

                }
                // A repeated explicit click can retry an unchanged intent after
                // the user frees capacity or changes the decal budget.
                if(sides!=oldSides||!remove)World.GetOrCreateSystemManaged<CurbDecalSystem>().QueueRoad(road);
                UpgradeFeedback.Report(_selected.name,Mod.Settings.DecalsBothSides?"Road wear targets both roadsides.":"Road wear targets the highlighted roadside.");
                return;

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

                DecalVisibilitySystem.QueueRoad(World,road);

            }

        }
    }
}
