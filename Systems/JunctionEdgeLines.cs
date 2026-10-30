using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class JunctionEdgePolicy
    {
        internal static bool TryGuideOffsets(Bezier4x3 guide,Bezier4x3 unit,float3 a,float3 b,out float first,out float last)
        {
            first=last=0;
            var na=(unit.a-guide.a).xz;var nb=(unit.d-guide.d).xz;
            float la=math.lengthsq(na),lb=math.lengthsq(nb);
            if(!math.isfinite(la)||!math.isfinite(lb)||la<.01f||lb<.01f)return false;
            first=math.dot((a-guide.a).xz,na)/la;last=math.dot((b-guide.d).xz,nb)/lb;
            return math.isfinite(first)&&math.isfinite(last)&&first*last>0&&math.abs(first)>=.1f&&math.abs(last)>=.1f&&math.abs(first)<=16&&math.abs(last)<=16;
        }
        internal static bool TryCorner(float3 a,float3 exitA,float3 b,float3 exitB,out Bezier4x3 curve)
        {
            curve=default;
            if(!NodeLinePlacement.CanJoin(a,exitA,b,exitB,0,-1,1))return false;
            // Conservative fallback pulls the arc into the paved junction.
            // Prefer a matching native sidewalk guide when one is available.
            float turn=math.acos(math.clamp(math.dot(math.normalizesafe(exitA.xz),-math.normalizesafe(exitB.xz)),-1,1));
            float cosine=math.cos(turn*.25f);
            float handle=1.15f*math.distance(a.xz,b.xz)/(3*cosine*cosine);
            curve=new Bezier4x3(a,a+math.normalizesafe(exitA)*handle,b+math.normalizesafe(exitB)*handle,b);return true;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        private struct CornerApproach
        {
            internal float Angle;
            internal JoinCap Left,Right;
            internal bool Hidden;
        }
        internal List<Bezier4x3> JunctionEdgePreview(Entity node,byte requestedStyle=0)
        {
            var roads=new List<Entity>();var marks=new List<MarkCurve>();var result=new List<Bezier4x3>();
            if(EntityManager.HasBuffer<ConnectedEdge>(node))foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))roads.Add(c.m_Edge);
            AddJunctionEdges(node,roads,marks,requestedStyle);
            foreach(var mark in marks)result.Add(mark.Curve);return result;
        }
        private bool TryNativeCorner(Entity node,JoinCap a,JoinCap b,out Bezier4x3 result)
        {
            result=default;if(!EntityManager.HasBuffer<Game.Net.SubLane>(node))return false;
            float best=4.5f;bool found=false;int inspected=0;
            foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                if(++inspected>256)break;var lane=item.m_SubLane;
                if(!EntityManager.HasComponent<Game.Net.PedestrianLane>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                if((EntityManager.GetComponentData<Game.Net.PedestrianLane>(lane).m_Flags&PedestrianLaneFlags.Crosswalk)!=0)continue;
                var guide=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                if(math.distance(guide.d,a.Point)+math.distance(guide.a,b.Point)<math.distance(guide.a,a.Point)+math.distance(guide.d,b.Point))
                    guide=new Bezier4x3(guide.d,guide.c,guide.b,guide.a);
                // Endpoints come from composed pavement/curb anchors. A grass strip
                // changes their distance to the walking guide without changing lane width.
                var unit=NetUtils.OffsetCurveLeftSmooth(guide,1);
                if(!JunctionEdgePolicy.TryGuideOffsets(guide,unit,a.Point,b.Point,out float startOffset,out float endOffset))continue;
                var start=NetUtils.OffsetCurveLeftSmooth(guide,startOffset);
                var end=NetUtils.OffsetCurveLeftSmooth(guide,endOffset);
                var candidate=new Bezier4x3(start.a,math.lerp(start.b,end.b,1f/3),math.lerp(start.c,end.c,2f/3),end.d);
                float score=math.distance(candidate.a,a.Point)+math.distance(candidate.d,b.Point);
                if(score>=best||math.distance(candidate.a,a.Point)>2.25f||math.distance(candidate.d,b.Point)>2.25f)continue;
                if(math.dot(math.normalizesafe(MathUtils.Tangent(candidate,0)),a.Outward)<.7f||math.dot(math.normalizesafe(MathUtils.Tangent(candidate,1)),-b.Outward)<.7f)continue;
                var first=a.Point-candidate.a;var last=b.Point-candidate.d;
                candidate=new Bezier4x3(a.Point,candidate.b+math.lerp(first,last,1f/3),candidate.c+math.lerp(first,last,2f/3),b.Point);
                bool clear=true;
                float clearance=math.min(math.abs(startOffset),math.abs(endOffset));
                for(int sample=0;sample<=12;sample++)
                {
                    var point=MathUtils.Position(candidate,sample/12f);float distance=MathUtils.Distance(guide,point,out float at);
                    if(distance+.02f<clearance||math.abs(point.y-MathUtils.Position(guide,at).y)>.5f){clear=false;break;}
                }
                if(!clear||!Valid(candidate))continue;best=score;result=candidate;found=true;
            }return found;
        }
        private void AddJunctionEdges(Entity node,List<Entity> roads,List<MarkCurve> output,byte requestedStyle=0)
        {
            if(SpecialPaintNode(node)&&AddNativeNodeEdges(node,output,requestedStyle))return;
            if(roads.Exists(r=>RoadSurfacePolicy.IsHighway(EntityManager,r)))
            {
                var paths=new List<Bezier4x3>();if(!HighwayEdgePlan(node,roads,paths)){AddNativeNodeEdges(node,output,requestedStyle);return;}
                byte mode=requestedStyle!=0?requestedStyle:EdgeUpgradePolicy.Mode(EntityManager,node,true);if(mode==EdgeUpgradePolicy.Removed)return;
                var paint=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(EdgeUpgradePolicy.Paired(mode)?(byte)2:(byte)1,EdgeUpgradePolicy.IsYellow(mode));if(paint==Entity.Null)return;
                output.RemoveAll(m=>(m.Curb||m.Median&&!m.CenterUpgrade)&&!m.Protected);
                foreach(var path in paths)output.Add(new MarkCurve(path,paint,0,false,true));return;
            }
            // Highway merge/gore geometry has its own continuity rules.
            if(roads.Count<2||roads.Count>8||output.Exists(m=>m.Median)&&roads.Exists(r=>RoadSurfacePolicy.IsHighway(EntityManager,r)))return;
            byte edgeMode=requestedStyle!=0?requestedStyle:EdgeUpgradePolicy.Mode(EntityManager,node,true);
            Entity asset=edgeMode>0&&edgeMode!=EdgeUpgradePolicy.Removed?World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(EdgeUpgradePolicy.Paired(edgeMode)?(byte)2:(byte)1,EdgeUpgradePolicy.IsYellow(edgeMode)):_linePrefab;if(asset==Entity.Null)return;
            var approaches=new List<CornerApproach>();
            var visited=new HashSet<Entity>();
            foreach(var road in roads)
            {
                if(!visited.Add(road))return; // Self-loop endpoints need distinct geometry.
                if(!EntityManager.HasComponent<Road>(road)||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road)||EntityManager.HasComponent<Deleted>(road)||EntityManager.HasComponent<Game.Tools.Temp>(road))return;
                if(!RoadSurfacePolicy.IsPaved(EntityManager,road))return;
                var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;if(!start&&edge.m_End!=node)return;
                var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
                if(!EntityManager.HasComponent<NetCompositionData>(composition)||!EntityManager.HasBuffer<NetCompositionLane>(composition))return;
                float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
                var lanes=EntityManager.GetBuffer<NetCompositionLane>(composition,true);
                var anchors=ReadCurbAnchors(composition,lanes);if(anchors.Count<2)anchors=ReadDecalAnchors(composition,width,lanes);
                if(anchors.Count<2)return;anchors.Sort((a,b)=>a.x.CompareTo(b.x));
                var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);var segment=start?geometry.m_Start:geometry.m_End;
                var direction=ApproachStopSystem.Away(EntityManager,road,node);if(math.lengthsq(direction)<.5f)return;
                var approach=new CornerApproach{Angle=math.atan2(direction.x,direction.z),Hidden=false};
                for(int side=0;side<2;side++)
                {
                    bool left=side==0;var anchor=anchors[left?0:anchors.Count-1];
                    if(!CurbPlacement.TryMapComposed(width,anchor,left,Mod.Settings.CurbLineOffset,out var position,out float fraction))return;
                    var curve=MathUtils.Lerp(segment.m_Left,segment.m_Right,fraction);
                    var point=start?curve.a:curve.d;point.y+=position.y;
                    var exit=math.normalizesafe(MathUtils.Tangent(curve,start?0:1))*(start?-1:1);
                    var cap=new JoinCap{Node=node,Point=point,Outward=exit,Kind=0,Side=start?(left?1:-1):(left?-1:1),Prefab=asset};
                    // Match the actual visible road-line endpoint (including
                    // stop-bar trimming), its offset and its selected asset.
                    float best=144;
                    if(_marks.TryGetValue(road,out var marks))foreach(var mark in marks.Curves)
                    {
                        if((!mark.Curb&&!mark.Median)||mark.Protected)continue;
                        float lateral=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(mark.Curve,.5f),width);
                        if(!math.isfinite(lateral)||(lateral<0)!=left||math.abs(lateral-position.x)>.6f)continue;
                        for(int end=0;end<2;end++)
                        {
                            var candidate=end==0?mark.Curve.a:mark.Curve.d;float distance=math.distancesq(candidate,point);
                            if(distance>=best)continue;best=distance;cap.Point=candidate;cap.Outward=math.normalizesafe(MathUtils.Tangent(mark.Curve,end))*(end==0?-1:1);cap.Prefab=mark.Prefab;
                        }
                    }
                    if(best==144&&_caps.TryGetValue(road,out var caps))foreach(var native in caps)
                    {
                        if(native.Node!=node||native.Kind!=0||native.Side!=cap.Side)continue;
                        float distance=math.distancesq(native.Point,point);if(distance>=best)continue;best=distance;cap=native;
                    }
                    if(cap.Side==1)approach.Left=cap;else approach.Right=cap;
                }
                approaches.Add(approach);
            }
            approaches.Sort((a,b)=>a.Angle.CompareTo(b.Angle));
            for(int i=0;i<approaches.Count;i++)
            {
                var first=approaches[i];var next=approaches[(i+1)%approaches.Count];if(first.Hidden||next.Hidden)continue;
                var a=first.Right;var b=next.Left;
                bool guided=TryNativeCorner(node,a,b,out var curve);
                if(!guided&&!JunctionEdgePolicy.TryCorner(a.Point,a.Outward,b.Point,b.Outward,out curve))continue;
                // A fallback without a curb guide must remain conservative.
                // Inset the interior of the arc into the paved junction, keeping
                // section endpoints fixed for continuity. Pairs need extra room.
                if(!guided){var center=EntityManager.GetComponentData<Node>(node).m_Position;float inset=.35f+(EdgeUpgradePolicy.Paired(edgeMode)?EdgeSpacing(edgeMode):0);curve.b+=math.normalizesafe(new float3(center.x-curve.b.x,0,center.z-curve.b.z))*inset;curve.c+=math.normalizesafe(new float3(center.x-curve.c.x,0,center.z-curve.c.z))*inset;}
                if(!Valid(curve))continue;
                if(edgeMode>0){a.Prefab=asset;b.Prefab=asset;}
                if(a.Prefab==b.Prefab)output.Add(new MarkCurve(curve,a.Prefab,.15f,false,true));
                else {output.Add(new MarkCurve(MathUtils.Cut(curve,new float2(0,.5f)),a.Prefab,.15f,false,true));output.Add(new MarkCurve(MathUtils.Cut(curve,new float2(.5f,1)),b.Prefab,.15f,false,true));}
            }
        }
    }
}
