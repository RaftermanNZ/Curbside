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
            internal Entity Road;
            internal float Angle;
            internal JoinCap Left,Right;
            internal Bezier4x3 LeftCurb,RightCurb;
            internal Bezier4x3 LeftSection,RightSection;
        }
        internal List<Bezier4x3> JunctionEdgePreview(Entity node,byte requestedStyle=0)
        {
            var roads=new List<Entity>();var marks=new List<MarkCurve>();var result=new List<Bezier4x3>();
            if(EntityManager.HasBuffer<ConnectedEdge>(node))foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))roads.Add(c.m_Edge);
            AddJunctionEdges(node,roads,marks,requestedStyle);
            foreach(var mark in marks)result.Add(mark.Curve);return result;
        }
        private bool TryNativeCorner(Entity node,JoinCap a,JoinCap b,out Bezier4x3 result)
            => TryNativeCorner(ReadNativeCornerGuides(node),a,b,out result);
        private void AddJunctionEdges(Entity node,List<Entity> roads,List<MarkCurve> output,byte requestedStyle=0, List<Bezier4x3> curbReferences = null)
        {
            if(SpecialPaintNode(node)&&AddNativeNodeEdges(node,output,requestedStyle))return;
            if(roads.Exists(r=>RoadSurfacePolicy.IsHighway(EntityManager,r)))
            {
                var paths=new List<Bezier4x3>();if(!HighwayEdgePlan(node,roads,paths)){AddNativeNodeEdges(node,output,requestedStyle);return;}
                byte mode=requestedStyle!=0?requestedStyle:EdgeUpgradePolicy.Mode(EntityManager,node,true);if(mode==EdgeUpgradePolicy.Removed)return;
                var paint=EdgePaintPrefab(mode);if(paint==Entity.Null)return;
                output.RemoveAll(m=>(m.Curb||m.Median&&!m.CenterUpgrade)&&!m.Protected);
                foreach(var path in paths)output.Add(new MarkCurve(path,paint,0,false,true,JunctionPaintOrigin.HighwayJoin));return;
            }
            // Highway merge/gore geometry has its own continuity rules.
            if(roads.Count<2)return;
            byte edgeMode=requestedStyle!=0?requestedStyle:EdgeUpgradePolicy.Mode(EntityManager,node,true);
            Entity asset=edgeMode>0&&edgeMode!=EdgeUpgradePolicy.Removed?EdgePaintPrefab(edgeMode):_linePrefab;if(asset==Entity.Null)return;
            var approaches=new List<CornerApproach>();
            var visited=new HashSet<Entity>();
            foreach(var road in roads)
            {
                if (!visited.Add(road) || !EntityManager.HasComponent<Edge>(road)) continue;
                if (!EntityManager.HasComponent<Road>(road) || EntityManager.HasComponent<Deleted>(road)
                    || EntityManager.HasComponent<Game.Tools.Temp>(road)) continue;
                var edge = EntityManager.GetComponentData<Edge>(road);
                bool start = edge.m_Start == node;
                if (start && edge.m_End == node)
                {
                    AddNativeNodeEdges(node, output, requestedStyle);
                    return;
                }
                if (!start && edge.m_End != node) continue;
                var direction = ApproachStopSystem.Away(EntityManager, road, node);
                if (math.lengthsq(direction.xz) < .25f) continue;
                var approach = new CornerApproach { Road = road, Angle = math.atan2(direction.x, direction.z) };
                if (!EntityManager.HasComponent<EdgeGeometry>(road) || !RoadSurfacePolicy.IsPaved(EntityManager, road))
                {
                    // Keep this approach in angular order so neighbors cannot join across it.
                    approaches.Add(approach);
                    continue;
                }
                var composition = EntityManager.GetComponentData<Composition>(road).m_Edge;
                var data = EntityManager.GetComponentData<NetCompositionData>(composition);
                float width = data.m_Width;
                var anchors = ReadAuthoredCurbAnchors(composition);
                if ((!CurbPlacement.TryOuterAnchor(anchors, width, data.m_MiddleOffset, true, out _)
                    || !CurbPlacement.TryOuterAnchor(anchors, width, data.m_MiddleOffset, false, out _))
                    && EntityManager.HasBuffer<NetCompositionLane>(composition))
                    foreach (var anchor in ReadDecalAnchors(composition, width, EntityManager.GetBuffer<NetCompositionLane>(composition, true)))
                    {
                        // An authored curb outranks a drivable-width estimate on its side.
                        bool leftSide = anchor.x < data.m_MiddleOffset;
                        if (!CurbPlacement.TryOuterAnchor(anchors, width, data.m_MiddleOffset, leftSide, out _))
                            AddDistinctCurb(anchors, anchor);
                    }
                var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);
                var segment = start ? geometry.m_Start : geometry.m_End;
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    if (!CurbPlacement.TryOuterAnchor(anchors, width, data.m_MiddleOffset, left, out var anchor)
                        || !CurbPlacement.TryMapComposed(width, anchor, left, Mod.Settings.CurbLineOffset, out var position, out float fraction))
                        continue;
                    var curve=MathUtils.Lerp(segment.m_Left,segment.m_Right,fraction);
                    var point=start?curve.a:curve.d;point.y+=position.y;
                    var exit=math.normalizesafe(MathUtils.Tangent(curve,start?0:1))*(start?-1:1);
                    var cap=new JoinCap{Node=node,Point=point,Outward=exit,Kind=0,Side=start?(left?1:-1):(left?-1:1),Prefab=asset};
                    // Match the actual visible road-line endpoint (including
                    // stop-bar trimming), its offset and its selected asset.
                    float best=144;
                    if(_marks.TryGetValue(road,out var marks))foreach(var mark in marks.Curves)
                    {
                        if(!mark.Curb||mark.Protected)continue;
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
                    var section = start ? MathUtils.Invert(curve) : curve;
                    AddPaintHeight(ref section, position.y);
                    if(cap.Side==1){approach.Left=cap;approach.LeftSection=section;}
                    else{approach.Right=cap;approach.RightSection=section;}
                }
                ReadPaintCurbs(road, node, out approach.LeftCurb, out approach.RightCurb, includeAlleyPavement: true);
                if (Valid(approach.LeftSection) && Valid(approach.RightSection))
                    NativePaintCornerGeometry.MatchAvailableMouths(approach.LeftSection.d, approach.RightSection.d,
                        ref approach.LeftCurb, ref approach.RightCurb);
                approaches.Add(approach);
            }
            approaches.Sort((a,b)=>a.Angle.CompareTo(b.Angle));
            if (approaches.Count < 2) return;
            List<Bezier4x3> nativeGuides = null;
            var nodeCenter = EntityManager.GetComponentData<Node>(node).m_Position;
            for(int i=0;i<approaches.Count;i++)
            {
                var first=approaches[i];var next=approaches[(i+1)%approaches.Count];
                var a=first.Right;var b=next.Left;
                if (a.Prefab == Entity.Null && b.Prefab == Entity.Null) continue;
                if (edgeMode > 0)
                {
                    if (a.Prefab != Entity.Null) a.Prefab = asset;
                    if (b.Prefab != Entity.Null) b.Prefab = asset;
                }
                float radius = .075f + (EdgeUpgradePolicy.Paired(edgeMode) ? EdgeSpacing(edgeMode) : 0f);
                var curbA = first.RightCurb;
                var curbB = next.LeftCurb;
                if (Valid(curbA) && Valid(curbB) && !NativePaintCornerGeometry.NativeSeamMatches(curbA, curbB))
                {
                    ReadRenderedPaintCurbs(first.Road, node, out var outerA, out var renderedA);
                    ReadRenderedPaintCurbs(next.Road, node, out var renderedB, out var outerB);
                    NativePaintCornerGeometry.MatchAvailableMouths(first.LeftSection.d, first.RightSection.d,
                        ref outerA, ref renderedA);
                    NativePaintCornerGeometry.MatchAvailableMouths(next.LeftSection.d, next.RightSection.d,
                        ref renderedB, ref outerB);
                    if (Valid(renderedA) && Valid(renderedB))
                    {
                        if (NativePaintCornerGeometry.NativeSeamMatches(renderedA, renderedB))
                        {
                            curbA = renderedA;
                            curbB = renderedB;
                        }
                        else if (NativePaintCornerGeometry.TryContinueMixedCurb(renderedA, renderedB, out var tracedA, out var tracedB))
                        {
                            curbA = tracedA;
                            curbB = tracedB;
                        }
                    }
                }
                if (AddNativePaintCorner(curbA, curbB,
                    Valid(first.LeftSection) ? first.LeftSection.d : nodeCenter,
                    Valid(next.RightSection) ? next.RightSection.d : nodeCenter,
                    first.RightSection, next.LeftSection,
                    a, b, radius, output, curbReferences)) continue;
                if (a.Prefab == Entity.Null || b.Prefab == Entity.Null) continue;
                // This loop does not write ECS or schedule jobs. Copy eligible
                // numeric guides once for all corners, preserving native order
                // and the original first-256-member inspection limit.
                if (nativeGuides == null) nativeGuides = ReadNativeCornerGuides(node);
                bool guided=TryNativeCorner(nativeGuides,a,b,out var curve);
                if(!guided&&!JunctionEdgePolicy.TryCorner(a.Point,a.Outward,b.Point,b.Outward,out curve))continue;
                // A fallback without a curb guide must remain conservative.
                // Inset the interior of the arc into the paved junction, keeping
                // section endpoints fixed for continuity. Pairs need extra room.
                if(!guided){var center=nodeCenter;float inset=.35f+(EdgeUpgradePolicy.Paired(edgeMode)?EdgeSpacing(edgeMode):0);curve.b+=math.normalizesafe(new float3(center.x-curve.b.x,0,center.z-curve.b.z))*inset;curve.c+=math.normalizesafe(new float3(center.x-curve.c.x,0,center.z-curve.c.z))*inset;}
                if(!Valid(curve))continue;
                var origin = guided ? JunctionPaintOrigin.SidewalkLaneFallback : JunctionPaintOrigin.TangentFallback;
                if(a.Prefab==b.Prefab)output.Add(new MarkCurve(curve,a.Prefab,.15f,false,true,origin));
                else {output.Add(new MarkCurve(MathUtils.Cut(curve,new float2(0,.5f)),a.Prefab,.15f,false,true,origin));output.Add(new MarkCurve(MathUtils.Cut(curve,new float2(.5f,1)),b.Prefab,.15f,false,true,origin));}
            }
        }
    }
}
