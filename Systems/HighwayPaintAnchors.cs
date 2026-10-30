using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal struct HighwayPaintAnchor
    {
        internal float3 Native,Paint,Outward;
        internal bool Yellow;
    }
    internal static class HighwayPaintAnchorPolicy
    {
        internal static bool Find(IList<HighwayPaintAnchor> anchors,float3 point,out HighwayPaintAnchor result)
        {
            result=default;bool found=false;
            foreach(var anchor in anchors)
            {
                // Match actual shared native endpoints, never a nearby gore or lane.
                if(math.distancesq(anchor.Native,point)>.0016f)continue;
                if(found&&(result.Yellow!=anchor.Yellow||math.distancesq(result.Paint,anchor.Paint)>.0016f))return false;
                result=anchor;found=true;
            }
            return found;
        }
        internal static Bezier4x3 Join(Bezier4x3 path,IList<HighwayPaintAnchor> anchors)
        {
            if(Find(anchors,path.a,out var first)&&math.distancesq(first.Native,first.Paint)>.000001f){var delta=first.Paint-path.a;float length=math.length(path.b-path.a);path.a+=delta;path.b=math.lengthsq(first.Outward)>.01f?path.a+math.normalizesafe(first.Outward)*length:path.b+delta;}
            if(Find(anchors,path.d,out var last)&&math.distancesq(last.Native,last.Paint)>.000001f){var delta=last.Paint-path.d;float length=math.length(path.d-path.c);path.d+=delta;path.c=math.lengthsq(last.Outward)>.01f?path.d+math.normalizesafe(last.Outward)*length:path.c+delta;}
            return path;
        }
        internal static bool Yellow(Bezier4x3 path,IList<HighwayPaintAnchor> anchors)
        {
            bool a=Find(anchors,path.a,out var first),b=Find(anchors,path.d,out var last);
            // A gore tip has no segment endpoint. Its attached traffic-left edge
            // still retains its yellow role; a known outer edge cannot inherit it.
            return (a||b)&&(!a||first.Yellow)&&(!b||last.Yellow);
        }
    }
    public partial class CurbLineEmissionSystem
    {
        private List<HighwayPaintAnchor> HighwayPaintAnchors(Entity node)
        {
            var result=new List<HighwayPaintAnchor>();
            if(!EntityManager.HasBuffer<ConnectedEdge>(node))return result;
            int roads=0;
            foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(node,true))
            {
                if(++roads>16)break;var road=connection.m_Edge;
                if(!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasBuffer<Game.Net.SubLane>(road)||EntityManager.HasComponent<Deleted>(road)||EntityManager.HasComponent<Game.Tools.Temp>(road)||!RoadSurfacePolicy.IsHighway(EntityManager,road))continue;
                var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;if(!start&&edge.m_End!=node)continue;
                var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);int count=0;
                foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
                {
                    if(++count>256)break;var source=entry.m_SubLane;
                    if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))continue;
                    if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab))continue;
                    int role=HighwayNodePaintPolicy.Role(prefab.name,false);if(role!=1&&role!=2)continue;
                    var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                    bool yellow=role==1||IsOpposingBoundary(road,path);
                    bool indexed=EntityManager.HasComponent<EdgeLane>(source);var delta=indexed?EntityManager.GetComponentData<EdgeLane>(source).m_EdgeDelta:default;
                    for(int end=0;end<2;end++)
                    {
                        var point=end==0?path.a:path.d;bool atStart;
                        if(indexed){float at=end==0?delta.x:delta.y;if(at>.001f&&at<.999f)continue;atStart=at<=.001f;}
                        else if(!NodeLinePlacement.TryRoadEnd(point,geometry.m_Start.m_Left.a,geometry.m_Start.m_Right.a,geometry.m_End.m_Left.d,geometry.m_End.m_Right.d,out atStart))continue;
                        if(atStart!=start)continue;
                        var paint=point;float best=4f;JoinCap chosen=default;bool found=false;
                        if(_caps.TryGetValue(road,out var caps))foreach(var cap in caps)
                        {
                            if(cap.Node!=node||(yellow?!HighwayLinePolicy.CenterCap(cap.Kind):cap.Kind!=0))continue;
                            if(math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(path,end).xz),math.normalizesafe(cap.Outward.xz)))<.9f)continue;
                            float distance=math.distancesq(point,cap.Point);if(distance>=best)continue;
                            best=distance;chosen=cap;found=true;
                        }
                        if(found)
                        {
                            paint=chosen.Point;
                            // Explicit double centerlines have one cap per stripe.
                            // Their shared axis is the midpoint, not either stripe.
                            if(chosen.Kind==3){float3 sum=paint;int n=1;foreach(var cap in caps)if(cap.Node==node&&cap.Kind==3&&cap.Boundary==chosen.Boundary&&math.distancesq(cap.Point,chosen.Point)>.0001f&&math.distancesq(cap.Point,chosen.Point)<.36f){sum+=cap.Point;n++;}if(n==2)paint=sum/n;}
                        }
                        result.Add(new HighwayPaintAnchor{Native=point,Paint=paint,Outward=found?chosen.Outward:default,Yellow=yellow});
                    }
                }
            }
            return result;
        }
    }
}

