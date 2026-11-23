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
    // Geometry guides temporarily suppress matching paint without changing upgrade intent.
    public partial class UpgradePreviewSystem:GameSystemBase
    {
        private Entity cachedCornerNode;private float nextCornerPreview;private List<Bezier4x3> cornerPreview=new List<Bezier4x3>();
        private Entity cachedParkingRoad;private bool cachedParkingLeft;private byte cachedParkingMode;private float nextParkingPreview,cachedBayLength;private int cachedGeometry;private List<Bezier4x3> parkingPreview=new List<Bezier4x3>();
        private static readonly UnityEngine.Color White=new UnityEngine.Color(1,1,1,.9f),Yellow=new UnityEngine.Color(1,.8f,.15f,.9f),Red=new UnityEngine.Color(1,.25f,.25f,.85f),Blue=new UnityEngine.Color(.3f,.8f,1,.8f);
        private void Line(Game.Rendering.OverlayRenderSystem.Buffer buffer,float3 a,float3 b,UnityEngine.Color color,float width=.12f)=>buffer.DrawLine(color,new Line3.Segment(a,b),width,true);
        private void Cross(Game.Rendering.OverlayRenderSystem.Buffer buffer,float3 point,float size)
        {Line(buffer,point+new float3(-size,0,-size),point+new float3(size,0,size),Red);Line(buffer,point+new float3(-size,0,size),point+new float3(size,0,-size),Red);}
        private void PaintPreview(Entity owner, Game.Rendering.OverlayRenderSystem.Buffer buffer, UnityEngine.Color color, Bezier4x3 path, float width)
        {
            World.GetOrCreateSystemManaged<MarkingPreviewSystem>().Request(owner, path);
            buffer.DrawCurve(color, path, width);
        }
        protected override void OnUpdate()
        { // Native-preview-only isolation test.
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
