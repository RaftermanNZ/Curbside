using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        private void AddHighwayMarks(Entity road,Entity composition,NetCompositionData data,EdgeGeometry geometry,List<StopMark> stops,List<MarkCurve> output,List<float2> unmarked)
        {
            if(!Mod.Settings.MedianEdgeLines&&SectionEdgePolicy.Mode(EntityManager,road)!=1||!RoadSurfacePolicy.IsHighway(EntityManager,road)||!RoadSurfacePolicy.IsPaved(EntityManager,road))return;
            Entity asset=AutomaticMedianAsset();
            if(!string.IsNullOrEmpty(Mod.Settings.MedianLineStyle))_nativeLineDefinitions.TryGetValue(Mod.Settings.MedianLineStyle,out asset);
            if(Mod.Settings.EuropeanMarkings&&asset!=Entity.Null)asset=ThemeEquivalent(asset);
            if(asset==Entity.Null)return;
            if(AddNativeHighwayMarks(road,asset,geometry,data,stops,output,unmarked))return;
            var groups=HighwayTrafficGroups(composition);
            foreach(var group in groups.Values)
            {
                if(!HighwayLinePolicy.TryLeftEdge(group,Mod.Settings.MedianLineOffset,out float x,out float y)||RoadMarkingPolicy.Suppressed(x,unmarked))continue;
                float fraction=x/data.m_Width+.5f;if(fraction<=0||fraction>=1)continue;
                var first=MathUtils.Lerp(geometry.m_Start.m_Left,geometry.m_Start.m_Right,fraction);
                var last=MathUtils.Lerp(geometry.m_End.m_Left,geometry.m_End.m_Right,fraction);
                first.a.y+=y+.01f;first.b.y+=y+.01f;first.c.y+=y+.01f;first.d.y+=y+.01f;
                last.a.y+=y+.01f;last.b.y+=y+.01f;last.c.y+=y+.01f;last.d.y+=y+.01f;
                bool duplicate=false;
                foreach(var mark in output)if(mark.Median&&math.distancesq(mark.Curve.a,first.a)<.04f){duplicate=true;break;}
                if(!duplicate&&SectionEdgePolicy.Mode(EntityManager,road)!=2&&!output.Exists(m=>m.CenterUpgrade)&&CenterlineUpgradePolicy.Mode(EntityManager,road)!=CenterlineUpgradePolicy.Removed){
                AddCaps(road,first,last,stops,2,asset);
                if(TrimMedian(first,stops,out var a))output.Add(new MarkCurve(a,asset,0,true));
                if(TrimMedian(last,stops,out var b))output.Add(new MarkCurve(b,asset,0,true));}
                if(SectionEdgePolicy.Mode(EntityManager,road)==2||!_nativeLineDefinitions.TryGetValue("EU Highway Edge Line",out var white))continue;
                bool reverse=group[0].Reverse;float outside=reverse?float.MaxValue:float.MinValue;float whiteY=0;
                foreach(var lane in group){float edge=lane.X+(reverse?-1:1)*lane.Width*.5f;if(reverse?edge<outside:edge>outside){outside=edge;whiteY=lane.Y;}}
                float whiteX=outside+(reverse?.15f:-.15f);if(RoadMarkingPolicy.Suppressed(whiteX,unmarked))continue;
                float whiteFraction=whiteX/data.m_Width+.5f;if(whiteFraction<=0||whiteFraction>=1)continue;
                var wf=MathUtils.Lerp(geometry.m_Start.m_Left,geometry.m_Start.m_Right,whiteFraction);var wl=MathUtils.Lerp(geometry.m_End.m_Left,geometry.m_End.m_Right,whiteFraction);
                wf.a.y+=whiteY+.01f;wf.b.y+=whiteY+.01f;wf.c.y+=whiteY+.01f;wf.d.y+=whiteY+.01f;
                wl.a.y+=whiteY+.01f;wl.b.y+=whiteY+.01f;wl.c.y+=whiteY+.01f;wl.d.y+=whiteY+.01f;
                AddCaps(road,wf,wl,stops,0,white);
                if(TrimMedian(wf,stops,out var wa))output.Add(new MarkCurve(wa,white,0,false,true));
                if(TrimMedian(wl,stops,out var wb))output.Add(new MarkCurve(wb,white,0,false,true));

            }
        }
        private bool AddNativeHighwayMarks(Entity road,Entity yellowAsset,EdgeGeometry geometry,NetCompositionData data,List<StopMark> stops,List<MarkCurve> output,List<float2> unmarked)
        {
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(road)||!EntityManager.HasComponent<Edge>(road))return false;
            var native=new List<MarkCurve>();var caps=new List<JoinCap>();var seen=new List<Bezier4x3>();int count=0;
            var edge=EntityManager.GetComponentData<Edge>(road);
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
            {
                if(++count>256)break;var source=entry.m_SubLane;
                if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))continue;
                var sourcePrefab=EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(sourcePrefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab)||HighwayNodePaintPolicy.Role(prefab.name,false)!=2)continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                if(!Valid(path)||seen.Exists(p=>GranularHighwayLinePolicy.Matches(p,path)))continue;seen.Add(path);
                float lateral=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(path,.5f),data.m_Width);
                if(RoadMarkingPolicy.Suppressed(lateral,unmarked))continue;
                bool yellow=IsOpposingBoundary(road,path);
                if(SectionEdgePolicy.Mode(EntityManager,road)==2||yellow&&(output.Exists(m=>m.CenterUpgrade)||CenterlineUpgradePolicy.Mode(EntityManager,road)==CenterlineUpgradePolicy.Removed))continue;
                var asset=yellow?yellowAsset:(_nativeLineDefinitions.TryGetValue("EU Highway Edge Line",out var white)?white:sourcePrefab);
                if(asset==Entity.Null)continue;
                if(TrimMedian(path,stops,out var trimmed))native.Add(new MarkCurve(trimmed,asset,0,yellow,!yellow){NativeSource=source});
                bool indexed=EntityManager.HasComponent<EdgeLane>(source);var delta=indexed?EntityManager.GetComponentData<EdgeLane>(source).m_EdgeDelta:default;
                for(int end=0;end<2;end++)
                {
                    var point=end==0?path.a:path.d;bool start;
                    if(indexed){float at=end==0?delta.x:delta.y;if(at>.001f&&at<.999f)continue;start=at<=.001f;}
                    else if(!NodeLinePlacement.TryRoadEnd(point,geometry.m_Start.m_Left.a,geometry.m_Start.m_Right.a,geometry.m_End.m_Left.d,geometry.m_End.m_Right.d,out start))continue;
                    bool stopped=false;foreach(var stop in stops)if(stop.AtStart==start){stopped=true;break;}if(stopped)continue;
                    caps.Add(new JoinCap{Node=start?edge.m_Start:edge.m_End,Point=point,Outward=(end==0?-1:1)*math.normalizesafe(MathUtils.Tangent(path,end)),Kind=yellow?2:0,Side=start?-(lateral<0?-1:1):(lateral<0?-1:1),Prefab=asset});
                }
            }
            // Vanilla's secondary-lane system already resolves skewed boundaries,
            // merge tips and split segment halves. Reuse its final paint geometry
            // throughout, instead of interpolating road-surface edges separately.
            if(seen.Count<2)return false;
            output.RemoveAll(m=>!m.Protected&&!m.CenterUpgrade&&!m.DividerUpgrade&&(m.Median||m.Curb));
            if(_caps.TryGetValue(road,out var existing)){existing.RemoveAll(c=>c.Kind==0||c.Kind==2);existing.AddRange(caps);}
            else _caps[road]=caps;
            output.AddRange(native);_dirtyNodes.Add(edge.m_Start);_dirtyNodes.Add(edge.m_End);return true;
        }
        private Dictionary<byte,List<NorthAmericanLinePolicy.Traffic>> HighwayTrafficGroups(Entity composition)
        {
            var groups=new Dictionary<byte,List<NorthAmericanLinePolicy.Traffic>>();
            foreach(var lane in EntityManager.GetBuffer<NetCompositionLane>(composition,true))
            {
                if((lane.m_Flags&(LaneFlags.Road|LaneFlags.Master|LaneFlags.Underground|LaneFlags.Twoway))!=LaneFlags.Road||!EntityManager.HasComponent<NetLaneData>(lane.m_Lane))continue;
                float width=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Width;
                if(!math.isfinite(width)||width<=0)continue;
                if(!groups.TryGetValue(lane.m_Carriageway,out var traffic))groups[lane.m_Carriageway]=traffic=new List<NorthAmericanLinePolicy.Traffic>();
                traffic.Add(new NorthAmericanLinePolicy.Traffic(lane.m_Position.x,width,lane.m_Position.y,(lane.m_Flags&LaneFlags.Invert)!=0));
            }
            return groups;
        }
        private static MarkCurve HighwayJoinMark(Bezier4x3 curve,JoinCap cap)=>new MarkCurve(curve,cap.Prefab,HighwayLinePolicy.CenterCap(cap.Kind)?0:.15f,HighwayLinePolicy.CenterCap(cap.Kind),cap.Kind==0);
        private bool HighwayIncoming(Entity road,Entity node,out bool incoming)
        {
            incoming=false;if(!RoadSurfacePolicy.IsHighway(EntityManager,road)||!EntityManager.HasComponent<Composition>(road))return false;
            var traffic=TrafficLanes(EntityManager.GetComponentData<Composition>(road).m_Edge);if(traffic.Count==0)return false;
            bool reverse=traffic[0].Reverse;foreach(var lane in traffic)if(lane.Reverse!=reverse)return false;
            bool start=EntityManager.GetComponentData<Edge>(road).m_Start==node;incoming=start?reverse:!reverse;return true;
        }
        private bool HighwayRoute(Entity node,Entity from,Entity to)
        {
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(node))return false;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                var lane=entry.m_SubLane;if(EntityManager.HasComponent<Deleted>(lane)||!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasComponent<Game.Net.Lane>(lane))continue;
                if((EntityManager.GetComponentData<Game.Net.CarLane>(lane).m_Flags&CarLaneFlags.Forbidden)!=0)continue;
                var path=EntityManager.GetComponentData<Game.Net.Lane>(lane);
                if(path.m_StartNode.GetOwnerIndex()==from.Index&&path.m_EndNode.GetOwnerIndex()==to.Index)return true;
            }return false;
        }
        private bool OuterCap(Entity road,Entity node,bool incoming,bool left,out JoinCap chosen)
        {
            chosen=default;bool found=false;
            if(!_caps.TryGetValue(road,out var caps))return false;
            foreach(var cap in caps)
            {
                if(cap.Node!=node||(cap.Kind!=0&&!HighwayLinePolicy.CenterCap(cap.Kind))||((incoming?cap.Side<0:cap.Side>0)!=left))continue;
                if(!found||HighwayLinePolicy.CenterCap(cap.Kind)){chosen=cap;found=true;}
            }return found;
        }
        private void AddHighwayJoins(Entity node,List<Entity> roads,List<MarkCurve> output)
        {
            if(roads.Count!=3||EntityManager.HasComponent<TrafficLights>(node))return;
            var inputs=new List<Entity>();var exits=new List<Entity>();
            foreach(var road in roads){if(!HighwayIncoming(road,node,out bool incoming)||ReadStopMarks(road).Count!=0)return;(incoming?inputs:exits).Add(road);}
            bool split=inputs.Count==1&&exits.Count==2,merge=inputs.Count==2&&exits.Count==1;if(!split&&!merge)return;
            var trunk=split?inputs[0]:exits[0];var branches=split?exits:inputs;
            if(!HighwayRoute(node,split?trunk:branches[0],split?branches[0]:trunk)||!HighwayRoute(node,split?trunk:branches[1],split?branches[1]:trunk))return;
            if(!OuterCap(trunk,node,split,true,out var tl)||!OuterCap(trunk,node,split,false,out var tr))return;
            if(!OuterCap(branches[0],node,!split,true,out var al)||!OuterCap(branches[0],node,!split,false,out var ar)||!OuterCap(branches[1],node,!split,true,out var bl)||!OuterCap(branches[1],node,!split,false,out var br))return;
            var forward=math.normalizesafe((tl.Outward+tr.Outward)*(split?1:-1));var right=new float3(forward.z,0,-forward.x);
            if(math.dot((al.Point+ar.Point)-(bl.Point+br.Point),right)>0){var swap=al;al=bl;bl=swap;swap=ar;ar=br;br=swap;}
            var left=NodeLinePlacement.Connect(tl.Point,tl.Outward,al.Point,al.Outward);var outerRight=NodeLinePlacement.Connect(tr.Point,tr.Outward,br.Point,br.Outward);
            if(!Valid(left)||!Valid(outerRight)||!HighwayJoinPolicy.TryGore(ar.Point,ar.Outward,bl.Point,bl.Outward,(tl.Point+tr.Point)*.5f,forward,split,out var goreA,out var goreB))return;
            output.Add(HighwayJoinMark(left,tl));output.Add(HighwayJoinMark(outerRight,tr));
            output.Add(HighwayJoinMark(goreA,ar));output.Add(HighwayJoinMark(goreB,bl));
        }
    }
    internal static class HighwayJoinPolicy
    {
        internal static bool TryGore(float3 a,float3 exitA,float3 b,float3 exitB,float3 trunk,float3 forward,bool split,out Bezier4x3 first,out Bezier4x3 second)
        {
            first=second=default;
            if(!math.all(math.isfinite(a))||!math.all(math.isfinite(b))||!math.all(math.isfinite(exitA))||!math.all(math.isfinite(exitB)))return false;
            float lengthA=math.length(exitA.xz),lengthB=math.length(exitB.xz);if(lengthA<.01f||lengthB<.01f)return false;
            var ea=exitA/lengthA;var eb=exitB/lengthB;var da=ea.xz;var db=eb.xz;var delta=b.xz-a.xz;float cross=da.x*db.y-da.y*db.x;
            if(math.abs(cross)<.02f)return false;
            float ta=(delta.x*db.y-delta.y*db.x)/cross,tb=(delta.x*da.y-delta.y*da.x)/cross;
            if(ta<0||tb<0||ta>80||tb>80)return false;
            float heightA=a.y+ea.y*ta,heightB=b.y+eb.y*tb;if(math.abs(heightA-heightB)>.5f)return false;
            var xz=a.xz+da*ta;var tip=new float3(xz.x,(heightA+heightB)*.5f,xz.y);
            if(math.distance(tip.xz,trunk.xz)>80||math.dot((tip-trunk).xz,forward.xz)*(split?1:-1)<-.5f)return false;
            first=new Bezier4x3(a,a+ea*ta/3,tip-ea*ta/3,tip);
            second=new Bezier4x3(b,b+eb*tb/3,tip-eb*tb/3,tip);return true;
        }
    }

    internal static class HighwayLinePolicy
    {
        // Explicit replacements keep the same highway shoulder role as automatic median paint.
        internal static bool CenterCap(int kind)=>kind==2||kind==3;
        internal static bool TryLeftEdge(IList<NorthAmericanLinePolicy.Traffic> lanes,float inset,out float x,out float y)
        {
            x=y=0;if(lanes.Count==0||!math.isfinite(inset)||inset<0)return false;
            bool reverse=lanes[0].Reverse;float edge=reverse?float.MinValue:float.MaxValue;
            foreach(var lane in lanes)
            {
                if(lane.Reverse!=reverse||!math.isfinite(lane.X)||!math.isfinite(lane.Width)||lane.Width<=0)return false;
                float value=lane.X+(reverse?1:-1)*lane.Width*.5f;
                if(reverse?value>edge:value<edge){edge=value;y=lane.Y;}
            }
            x=edge+(reverse?-inset:inset);return math.isfinite(x)&&math.isfinite(y);
        }
    }
}
