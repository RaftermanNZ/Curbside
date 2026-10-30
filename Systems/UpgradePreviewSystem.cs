using System.Collections.Generic;
using Colossal.Mathematics;using Game;using Game.Common;using Game.Net;using Game.Prefabs;using Game.Tools;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class UpgradePreviewPolicy
    {
        internal static bool Known(string name)
        {
            foreach(var entry in CurbsidePanelSystem.Entries)if(entry.Asset==name)return true;return false;
        }
        internal static byte Parking(string name)=>name==CrosswalkUpgradeSystem.MarkedName?(byte)2:name==CrosswalkUpgradeSystem.UnmarkedName?(byte)1:name==CrosswalkUpgradeSystem.TOnlyName?(byte)3:name==CrosswalkUpgradeSystem.NotchedName?(byte)4:name==CrosswalkUpgradeSystem.FullTName?(byte)5:name==CrosswalkUpgradeSystem.DividerOnlyName?(byte)6:(byte)0;
    }
    // Read-only geometry guides. No scene objects, prefab changes or upgrade intent.
    public partial class UpgradePreviewSystem:GameSystemBase
    {
        private Entity cachedCornerNode;private float nextCornerPreview;private List<Bezier4x3> cornerPreview=new List<Bezier4x3>();
        private Entity cachedParkingRoad;private bool cachedParkingLeft;private byte cachedParkingMode;private float nextParkingPreview,cachedBayLength;private int cachedGeometry;private List<Bezier4x3> parkingPreview=new List<Bezier4x3>();
        private static readonly UnityEngine.Color White=new UnityEngine.Color(1,1,1,.9f),Yellow=new UnityEngine.Color(1,.8f,.15f,.9f),Red=new UnityEngine.Color(1,.25f,.25f,.85f),Blue=new UnityEngine.Color(.3f,.8f,1,.8f);
        private void Line(Game.Rendering.OverlayRenderSystem.Buffer buffer,float3 a,float3 b,UnityEngine.Color color,float width=.12f)=>buffer.DrawLine(color,new Line3.Segment(a,b),width,true);
        private void Cross(Game.Rendering.OverlayRenderSystem.Buffer buffer,float3 point,float size)
        {Line(buffer,point+new float3(-size,0,-size),point+new float3(size,0,size),Red);Line(buffer,point+new float3(-size,0,size),point+new float3(size,0,-size),Red);}
        protected override void OnUpdate()
        {
            if(Mod.Saving||Mod.Settings==null||UpgradeInputPolicy.PointerBlocked)return;
            var tools=World.GetOrCreateSystemManaged<ToolSystem>();string name=tools.activeTool?.GetPrefab()?.name;
            if(LineShapePolicy.Style(name)!=0||name==LineShapePolicy.Visibility)return;
            if(!UpgradePreviewPolicy.Known(name)||name==CrosswalkUpgradeSystem.LineName||name==CrosswalkUpgradeSystem.InvisibleName)return;
            var ray=World.GetOrCreateSystemManaged<ToolRaycastSystem>();if(!ray.GetRaycastResult(out var hit))return;
            if(name==StreetLightingSystem.AssetName&&StreetLightingSystem.TryLightTarget(EntityManager,EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity)?hit.m_Hit.m_HitEntity:hit.m_Owner,out var lightTarget)&&EntityManager.HasComponent<Node>(lightTarget))return;
            if(SurfaceUpgradePolicy.Slot(name)!=0&&StreetLightingSystem.TryLightTarget(EntityManager,EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity)?hit.m_Hit.m_HitEntity:hit.m_Owner,out var surfaceTarget)&&EntityManager.HasComponent<Node>(surfaceTarget))return;
            if((EdgeUpgradePolicy.Style(name)!=0||LaneDividerPolicy.Style(name)!=0||CenterlineUpgradePolicy.Style(name)!=0||SegmentUpgradePolicy.Selection(name)!=0||name==CenterlineVisibility.AssetName||name==SegmentUpgradePolicy.ResetLines)&&StreetLightingSystem.TryLightTarget(EntityManager,EntityManager.HasComponent<Node>(hit.m_Hit.m_HitEntity)?hit.m_Hit.m_HitEntity:hit.m_Owner,out var nodeTarget)&&EntityManager.HasComponent<Node>(nodeTarget))return;
            Dependency.Complete();if(!SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var road))return;
            if(!EntityManager.HasComponent<Road>(road)||!EntityManager.HasComponent<Curve>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||EntityManager.HasComponent<Deleted>(road)||EntityManager.HasComponent<Temp>(road))return;
            var overlay=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var pending);pending.Complete();
            var emission=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            emission.RetainPaintTarget(road);
            bool left=RoadSideUpgrades.IsLeft(EntityManager,road,hit.m_Hit.m_HitPosition);
            if(name==RoadFeatureInspector.AssetName){overlay.DrawCurve(Blue,RoadSideUpgrades.Preview(EntityManager,road,true),.2f);overlay.DrawCurve(Blue,RoadSideUpgrades.Preview(EntityManager,road,false),.2f);return;}
            if(SegmentUpgradePolicy.Known(name))
            {
                byte visibility=SegmentUpgradePolicy.Selection(name);
                if(visibility!=0)DrawSegmentVisibility(road,visibility,overlay,emission);
                else {overlay.DrawCurve(Blue,RoadSideUpgrades.Preview(EntityManager,road,true),.22f);overlay.DrawCurve(Blue,RoadSideUpgrades.Preview(EntityManager,road,false),.22f);}
                return;
            }
            byte edgeStyle=EdgeUpgradePolicy.Style(name);
            if(edgeStyle!=0){foreach(var path in emission.EdgePreview(road,left,edgeStyle))overlay.DrawCurve(EdgeUpgradePolicy.IsYellow(edgeStyle)?Yellow:White,path,.15f);return;}
            byte center=CenterlineUpgradePolicy.Style(name);
            if(LaneDividerPolicy.Style(name)!=0){if(emission.TryDividerPlan(road,out var dividers))foreach(var path in dividers)foreach(var stroke in LaneDividerPolicy.Strokes(path,LaneDividerPolicy.Style(name)))overlay.DrawCurve(LaneDividerPolicy.IsYellow(LaneDividerPolicy.Style(name))?Yellow:White,stroke,.12f);else Cross(overlay,hit.m_Hit.m_HitPosition,1);return;}
            if(center!=0)
            {
                if(emission.TryCenterUpgradePlan(road,center,out var paths,out _))foreach(var path in paths)overlay.DrawCurve(CenterlineUpgradePolicy.YellowPaint(center,Mod.Settings.YellowToolPaint)?Yellow:White,path,.15f);else Cross(overlay,hit.m_Hit.m_HitPosition,1);
                return;
            }
            if(name==ParkingHatchPolicy.AssetName||name==ParkingHatchPolicy.Yellow){foreach(var path in emission.ParkingHatchPlan(road,left))overlay.DrawCurve(name==ParkingHatchPolicy.Yellow?Yellow:White,path,.12f);return;}
            byte parking=UpgradePreviewPolicy.Parking(name);
            if(parking!=0)
            {
                int geometry=EntityManager.GetComponentData<EdgeGeometry>(road).GetHashCode();float now=UnityEngine.Time.realtimeSinceStartup;
                if(cachedParkingRoad!=road||cachedParkingLeft!=left||cachedParkingMode!=parking||cachedBayLength!=Mod.Settings.FixedParkingBayLength||cachedGeometry!=geometry||now>=nextParkingPreview){parkingPreview=emission.ParkingUpgradePreview(road,left,parking);cachedParkingRoad=road;cachedParkingLeft=left;cachedParkingMode=parking;cachedBayLength=Mod.Settings.FixedParkingBayLength;cachedGeometry=geometry;nextParkingPreview=now+.25f;}
                foreach(var path in parkingPreview)overlay.DrawCurve(parking==1?Blue:White,path,parking==1?.07f:.12f);return;
            }
            if(name==RoadSideUpgrades.DecalAsset)
            {
                var s=Mod.Settings;var footprint=DecalStripPolicy.Footprint(s.CurbDecalWidth,s.CurbDecalLength,s.CurbDecalRotation);var paths=new List<Bezier4x3>();var sides=new List<bool>();
                if(emission.TryDecalCurves(road,RoadSurfacePolicy.DecalOffset(EntityManager,road),footprint.x,s.CurbDecalSetback,paths,sides,RoadSideUpgrades.DecalTarget(left,s.DecalsBothSides)))
                foreach(var path in paths){var a=NetUtils.OffsetCurveLeftSmooth(path,footprint.x*.5f);var b=NetUtils.OffsetCurveLeftSmooth(path,-footprint.x*.5f);overlay.DrawCurve(Blue,a,.1f);overlay.DrawCurve(Blue,b,.1f);Line(overlay,a.a,b.a,Blue);Line(overlay,a.d,b.d,Blue);}return;
            }
            if(name==VisualPowerlineSystem.AssetName||name==StreetLightingSystem.AssetName)
            {
                var paths=new List<Bezier4x3>();var sides=new List<bool>();
                if(emission.TryVisualPowerlineCurves(road,name==StreetLightingSystem.AssetName?(byte)3:RoadSideUpgrades.SideMask(left),paths,sides))foreach(var path in paths)
                {
                    int count=math.clamp((int)(MathUtils.Length(path)/math.max(15,Mod.Settings.PowerlinePoleSpacing))+1,1,8);float3 previous=default;
                    for(int i=0;i<count;i++){float t=(i+.5f)/count;var foot=MathUtils.Position(path,t);var head=foot+new float3(0,name==StreetLightingSystem.AssetName?7:PoleHeightPolicy.Requested(Mod.Settings.PowerlineHeight),0);Line(overlay,foot,head,Blue,.14f);if(name==StreetLightingSystem.AssetName)overlay.DrawCircle(Yellow,head,.6f);else if(i>0)overlay.DrawCurve(Blue,VisualPowerlinePolicy.Span(previous,head,.025f),.08f);previous=head;}
                }return;
            }
            var edge=EntityManager.GetComponentData<Edge>(road);bool end=NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(road),hit.m_Hit.m_HitPosition);var node=end?edge.m_End:edge.m_Start;
            if(name==NodeLineUpgradeTool.EdgeAssetName){if(cachedCornerNode!=node||UnityEngine.Time.realtimeSinceStartup>=nextCornerPreview){cachedCornerNode=node;nextCornerPreview=UnityEngine.Time.realtimeSinceStartup+.25f;cornerPreview=emission.JunctionEdgePreview(node);}foreach(var path in cornerPreview)overlay.DrawCurve(White,path,.15f);return;}
            if(name==ApproachStopTool.AssetName||name==ApproachStopTool.SingleAssetName||name==ApproachStopTool.YieldAssetName||name==ApproachStopTool.SingleYieldAssetName||name==FarSideLightUpgradeTool.AssetName)
            {
                if(!EntityManager.HasBuffer<ConnectedEdge>(node))return;var direction=ApproachStopSystem.Away(EntityManager,road,node);
                bool pair=name==ApproachStopTool.AssetName||name==ApproachStopTool.YieldAssetName;bool yield=name==ApproachStopTool.YieldAssetName||name==ApproachStopTool.SingleYieldAssetName;
                foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(EntityManager.HasComponent<Curve>(c.m_Edge)&&(c.m_Edge==road||name==FarSideLightUpgradeTool.AssetName||pair&&ApproachStopPolicy.Opposite(direction,ApproachStopSystem.Away(EntityManager,c.m_Edge,node))))
                {
                    var e=EntityManager.GetComponentData<Edge>(c.m_Edge);var path=RoadSideUpgrades.Preview(EntityManager,c.m_Edge,true);var p=MathUtils.Position(path,e.m_Start==node?.05f:.95f);int vertices=yield?3:name==FarSideLightUpgradeTool.AssetName?4:8;
                    for(int i=0;i<vertices;i++){float a=math.PI*2*i/vertices,b=math.PI*2*(i+1)/vertices;Line(overlay,p+new float3(math.sin(a),0,math.cos(a))*.8f,p+new float3(math.sin(b),0,math.cos(b))*.8f,yield?Yellow:Red);}
                }return;
            }
            if(name==CenterlineVisibility.AssetName||name==NodeLineUpgradeTool.AssetName)
            {
                DrawRemovedPaint(road,name==CenterlineVisibility.AssetName,overlay,emission);
                if(name==NodeLineUpgradeTool.AssetName){DrawRemovedPaint(node,false,overlay,emission);if(EntityManager.HasBuffer<ConnectedEdge>(node))foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(c.m_Edge!=road)DrawRemovedPaint(c.m_Edge,false,overlay,emission);}return;
            }
            if(name==ParkingBanPolicy.AssetName){var path=RoadSideUpgrades.Preview(EntityManager,road,left);overlay.DrawCurve(White,path,.12f);Cross(overlay,MathUtils.Position(path,.5f),.8f);return;}
            if(UpgradeAssetsSystem.Mask(name)!=0)
            {
                foreach(var p in World.GetOrCreateSystemManaged<DecalVisibilitySystem>().PreviewPositions(road,left,UpgradeAssetsSystem.Mask(name)))Cross(overlay,p,.7f);
            }
        }
        private void DrawSegmentVisibility(Entity road,byte mask,Game.Rendering.OverlayRenderSystem.Buffer overlay,CurbLineEmissionSystem emission)
        {
            var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();int count=0;
            if(EntityManager.HasBuffer<Game.Net.SubLane>(road))foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
            {
                var lane=item.m_SubLane;if(count>=128||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                if(EntityManager.HasComponent<Game.Net.CarLane>(lane)||EntityManager.HasComponent<Game.Net.ParkingLane>(lane)||EntityManager.HasComponent<Game.Net.PedestrianLane>(lane)||EntityManager.HasComponent<Game.Net.TrackLane>(lane))continue;
                if(!prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab,out var prefab)||!NodeLinePolicy.IsLineGeometry(prefab))continue;
                var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;bool center=prefab.name.IndexOf("Center Line",System.StringComparison.OrdinalIgnoreCase)>=0||!RoadSurfacePolicy.IsAlleyForEdges(EntityManager,road)&&emission.IsOpposingBoundary(road,curve);bool edge=!center&&prefab.name.IndexOf("Parking",System.StringComparison.OrdinalIgnoreCase)<0&&prefab.name.IndexOf("Bay",System.StringComparison.OrdinalIgnoreCase)<0&&(prefab.name.IndexOf("Edge Line",System.StringComparison.OrdinalIgnoreCase)>=0||emission.IsOuterEdgePaint(road,curve));
                if((mask&SegmentUpgradePolicy.HideNative)!=0||SegmentUpgradePolicy.Hides(mask,center,edge)){overlay.DrawCurve(Red,curve,.18f);count++;}
            }
            // Include a visible section guide even if paint is currently hidden.
            overlay.DrawCurve(Red,RoadSideUpgrades.Preview(EntityManager,road,true),.08f);overlay.DrawCurve(Red,RoadSideUpgrades.Preview(EntityManager,road,false),.08f);
        }
        private void DrawRemovedPaint(Entity owner,bool center,Game.Rendering.OverlayRenderSystem.Buffer overlay,CurbLineEmissionSystem emission)
        {
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return;var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();int count=0;
            foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
            {
                var lane=item.m_SubLane;if(count>=64||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                if(!prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLaneLine(prefab.name))continue;
                var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                if(center&&prefab.name.IndexOf("Center Line",System.StringComparison.OrdinalIgnoreCase)<0&&!emission.IsOpposingBoundary(owner,curve))continue;
                overlay.DrawCurve(Red,curve,.18f);count++;
            }
        }
    }
    public partial class CurbLineEmissionSystem
    {
        internal List<Bezier4x3> ParkingUpgradePreview(Entity road,bool left,byte mode)
        {
            var output=new List<Bezier4x3>();if(!EntityManager.HasBuffer<Game.Net.SubLane>(road))return output;
            foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
            {
                var lane=item.m_SubLane;if(!EntityManager.HasComponent<Game.Net.ParkingLane>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                var curve=EntityManager.GetComponentData<Curve>(lane);if(RoadSideUpgrades.IsLeft(EntityManager,road,MathUtils.Position(curve.m_Bezier,.5f))!=left)continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;if(!EntityManager.HasComponent<NetLaneData>(prefab)||!EntityManager.HasComponent<ParkingLaneData>(prefab))continue;
                var data=EntityManager.GetComponentData<ParkingLaneData>(prefab);if(math.abs(data.m_SlotAngle)>.25f)continue;
                float width=EntityManager.GetComponentData<NetLaneData>(prefab).m_Width;float length=math.max(7,Mod.Settings.FixedParkingBayLength);
                data.m_SlotInterval=ParkingBayPolicy.NativeInterval(length);data.m_SlotSize=new float2(width,length);data.m_MaxCarLength=math.max(0,length-.5f);
                var parking=EntityManager.GetComponentData<Game.Net.ParkingLane>(lane);int count=NetUtils.GetParkingSlotCount(curve,parking,data);if(count<1||count>128)continue;
                float interval=NetUtils.GetParkingSlotInterval(curve,parking,data,count);float offset=(curve.m_Length-count*interval)*.5f;
                if((parking.m_Flags&ParkingLaneFlags.StartingLane)!=0)offset=curve.m_Length-count*interval;else if((parking.m_Flags&ParkingLaneFlags.EndingLane)!=0)offset=0;
                var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);var center=(geometry.m_Start.m_Left.d+geometry.m_Start.m_Right.d)*.5f;
                bool inward=math.dot(MathUtils.Left(math.normalizesafe(MathUtils.Tangent(curve.m_Bezier,.5f).xz)),center.xz-MathUtils.Position(curve.m_Bezier,.5f).xz)>0;
                var boundary=NetUtils.OffsetCurveLeftSmooth(curve.m_Bezier,inward?width*.5f:-width*.5f);
                var parameters=new float[count+1];var available=new bool[count];
                offset=math.max(0,offset);
                for(int i=0;i<=count;i++)parameters[i]=ParkingMarkPlacement.SlotParameter(curve.m_Bezier,offset+i*interval);
                bool banned=ParkingBanPolicy.Banned(EntityManager,road,MathUtils.Position(curve.m_Bezier,.5f));
                GetStopRange(boundary,ReadStopMarks(road),LaneForward(road,lane,curve.m_Bezier),out float stopLower,out float stopUpper);
                bool closeStart=ParkingClosesAt(road,boundary.a),closeEnd=ParkingClosesAt(road,boundary.d);
                bool usablePaint=ParkingSetback.TryLimits(curve.m_Bezier,stopLower,stopUpper,closeStart,closeEnd,Mod.Settings.ParkingIntersectionSetback,out float paintFirst,out float paintLast);
                for(int i=0;i<count;i++)available[i]=ParkingPaintAvailable(road,lane,curve.m_Bezier,parameters[i],parameters[i+1],usablePaint,paintFirst,paintLast);
                var markers=ParkingMarkPlacement.BayMarkers(parameters,available,0,1);
                foreach(var stroke in ParkingMarkPlacement.BuildBayStyle(boundary,0,1,markers,!inward,(ParkingBayStyle)(mode-2),width,closeStart,closeEnd,Mod.Settings.CloseParkingAtBuildingEntrances))output.Add(stroke);
            }return output;
        }
    }
}
