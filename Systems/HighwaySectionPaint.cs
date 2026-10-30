using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Managed value copies only; no borrowed native lane views survive this read.
    internal struct HighwaySectionPath
    {
        internal Bezier4x3 Curve;
        internal float Start,End;
        internal bool Left;
    }
    internal static class HighwaySectionPaintPolicy
    {
        internal static HighwaySectionPath Forward(Bezier4x3 path,float start,float end,bool left)
        {
            return start<=end?new HighwaySectionPath{Curve=path,Start=start,End=end,Left=left}
                :new HighwaySectionPath{Curve=new Bezier4x3(path.d,path.c,path.b,path.a),Start=end,End=start,Left=left};
        }
        internal static bool Pairs(List<HighwaySectionPath> source,out List<Bezier4x3> paths)
        {
            paths=new List<Bezier4x3>();
            if(source.Count==0||source.Count>32)return false;
            // A native lane may cover the whole section or arrive as two halves.
            // Splitting a whole Bezier changes its representation, not its shape.
            var normalized=new List<HighwaySectionPath>();
            foreach(var item in source)
            {
                var p=item.Curve;
                if(!math.isfinite(item.Start)||!math.isfinite(item.End)||!math.all(math.isfinite(p.a))||!math.all(math.isfinite(p.b))||!math.all(math.isfinite(p.c))||!math.all(math.isfinite(p.d)))return false;
                if(math.abs(item.Start)<.001f&&math.abs(item.End-1)<.001f)
                {
                    normalized.Add(new HighwaySectionPath{Curve=MathUtils.Cut(p,new float2(0,.5f)),Start=0,End=.5f,Left=item.Left});
                    normalized.Add(new HighwaySectionPath{Curve=MathUtils.Cut(p,new float2(.5f,1)),Start=.5f,End=1,Left=item.Left});
                }
                else normalized.Add(item);
            }
            source=normalized;if(source.Count%2!=0)return false;
            source.Sort((a,b)=>a.Left!=b.Left?(a.Left?-1:1):a.Start.CompareTo(b.Start));
            var used=new bool[source.Count];
            for(int i=0;i<source.Count;i++)
            {
                var first=source[i];if(used[i])continue;
                if(math.abs(first.Start)>.001f||math.abs(first.End-.5f)>.001f){paths.Clear();return false;}
                int partner=-1;
                for(int j=0;j<source.Count;j++)
                {
                    var last=source[j];
                    if(j==i||used[j]||last.Left!=first.Left||math.abs(last.Start-.5f)>.001f||math.abs(last.End-1)>.001f||math.distancesq(first.Curve.d,last.Curve.a)>.0016f)continue;
                    if(partner>=0){paths.Clear();return false;}partner=j;
                }
                if(partner<0){paths.Clear();return false;}
                used[i]=used[partner]=true;paths.Add(first.Curve);paths.Add(source[partner].Curve);
            }
            return paths.Count==source.Count;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool NativeHighwaySectionPlan(Entity road,int role,out List<Bezier4x3> paths)
        {
            paths=new List<Bezier4x3>();
            if(!RoadSurfacePolicy.IsHighway(EntityManager,road)||!RoadSurfacePolicy.IsPaved(EntityManager,road)||!EntityManager.HasBuffer<Game.Net.SubLane>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasComponent<NetCompositionData>(composition))return false;
            float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            if(!math.isfinite(width)||width<=0)return false;
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);
            var sourcePaths=new List<HighwaySectionPath>();int count=0;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
            {
                if(++count>256)return false;var source=entry.m_SubLane;
                if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<EdgeLane>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<Game.Tools.Temp>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))continue;
                if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab))continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(path))continue;
                int sourceRole=HighwayNodePaintPolicy.Role(prefab.name,false);
                if(role==1?sourceRole!=1&&!(sourceRole==2&&IsOpposingBoundary(road,path)):sourceRole!=role)continue;
                if(sourcePaths.Exists(p=>GranularHighwayLinePolicy.Matches(p.Curve,path)))continue;
                var delta=EntityManager.GetComponentData<EdgeLane>(source).m_EdgeDelta;
                bool left=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(path,.5f),width)<0;
                sourcePaths.Add(HighwaySectionPaintPolicy.Forward(path,delta.x,delta.y,left));
            }
            return HighwaySectionPaintPolicy.Pairs(sourcePaths,out paths);
        }
        internal bool UsesNativeOneWayHighwayEdges(Entity road)
        {
            if(!NativeHighwaySectionPlan(road,2,out var paths)||paths.Count!=4)return false;
            var traffic=TrafficLanes(EntityManager.GetComponentData<Composition>(road).m_Edge);if(traffic.Count==0)return false;
            bool reverse=traffic[0].Reverse;foreach(var lane in traffic)if(lane.Reverse!=reverse)return false;
            return true;
        }
    }
}
