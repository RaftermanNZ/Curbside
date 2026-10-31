using System.Collections.Generic;
using Colossal.Mathematics;using Colossal.Serialization.Entities;
using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRLaneDivider:IComponentData,ISerializable
    {
        public byte Value; // 1 explicit dashed dividers, 2 remove divider paint.
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Value);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Value);
    }
    internal static class LaneDividerPolicy
    {
        internal const string AssetName="MGR Lane Dividers";
        internal const string Short="MGR Short Dash Dividers",Long="MGR Long Dash Dividers";
        internal const string Yellow="MGR Yellow Dash Dividers",YellowShort="MGR Yellow Short Dash Dividers",YellowLong="MGR Yellow Long Dash Dividers";
        internal static byte Style(string name)=>name==AssetName?(byte)1:name==Short?(byte)3:name==Long?(byte)4:name==Yellow?(byte)5:name==YellowShort?(byte)6:name==YellowLong?(byte)7:(byte)0;
        internal static bool Solid(byte mode)=>mode>=8&&mode<=13;
        internal static byte Pattern(byte mode)=>mode==6?(byte)3:mode==7?(byte)4:mode==5?(byte)1:mode;
        internal static bool IsYellow(byte mode)=>mode>=5&&mode<=7||mode==9||mode==11||mode==13;
        internal static string Label(byte mode)=>Solid(mode)?(IsYellow(mode)?"Yellow":"White")+(mode>=10?" double":" solid")+" dividers":(IsYellow(mode)?"Yellow ":"White ")+(Pattern(mode)==3?"short-dash":Pattern(mode)==4?"long-dash":"standard-dash")+" dividers";
        internal static bool Applied(byte mode)=>mode==1||mode>=3&&mode<=13;
        internal static List<Bezier4x3> Strokes(Bezier4x3 path,byte style)
        {
            var result=new List<Bezier4x3>();float length=MathUtils.Length(path);if(!math.isfinite(length)||length<.1f||length>2000)return result;
            style=Pattern(style);float dash=style==3?1:style==4?6:3,gap=style==3?2:style==4?9:6;
            for(float at=0;at<length&&result.Count<512;at+=dash+gap){float a=ParkingMarkPlacement.SlotParameter(path,at),b=ParkingMarkPlacement.SlotParameter(path,math.min(length,at+dash));if(b>a)result.Add(MathUtils.Cut(path,new float2(a,b)));}return result;
        }
        internal static byte Mode(EntityManager em,Entity road)=>em.HasComponent<MGRLaneDivider>(road)?em.GetComponentData<MGRLaneDivider>(road).Value:(byte)0;
        internal static List<float2> Boundaries(IList<NorthAmericanLinePolicy.Traffic> input)
        {
            var lanes=new List<NorthAmericanLinePolicy.Traffic>(input);lanes.Sort((a,b)=>a.X.CompareTo(b.X));var result=new List<float2>();
            for(int i=1;i<lanes.Count;i++)
            {
                var a=lanes[i-1];var b=lanes[i];float first=a.X+a.Width*.5f,last=b.X-b.Width*.5f,gap=last-first;
                if(a.Reverse!=b.Reverse||!math.isfinite(gap)||!math.isfinite(a.Y)||!math.isfinite(b.Y)||a.Width<=0||b.Width<=0||gap<-.1f||gap>.5f||math.abs(a.Y-b.Y)>.2f)continue;
                result.Add(new float2((first+last)*.5f,(a.Y+b.Y)*.5f));
            }return result;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool TryDividerPlan(Entity road,out List<Bezier4x3> paths)
        {
            paths=new List<Bezier4x3>();
            if(!RoadSurfacePolicy.IsPaved(EntityManager,road)||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;if(!EntityManager.HasComponent<NetCompositionData>(composition))return false;
            float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;if(!math.isfinite(width)||width<=0)return false;
            if(NativeHighwayEditableEdges(road,out var nativePieces,3)){paths=nativePieces;return true;}
            if(NativeHighwaySectionPlan(road,3,out var nativeDividers)){paths=nativeDividers;return true;}
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);var stops=ReadStopMarks(road);
            foreach(var boundary in LaneDividerPolicy.Boundaries(TrafficLanes(composition)))
            {
                int before=paths.Count;
                float fraction=boundary.x/width+.5f;if(fraction<=0||fraction>=1)continue;
                for(int half=0;half<2;half++)
                {
                    var side=half==0?geometry.m_Start:geometry.m_End;var path=MathUtils.Lerp(side.m_Left,side.m_Right,fraction);
                    path.a.y+=boundary.y+.01f;path.b.y+=boundary.y+.01f;path.c.y+=boundary.y+.01f;path.d.y+=boundary.y+.01f;
                    GetStopRange(path,stops,true,out float lower,out float upper);
                    if(StopLinePlacement.TrimRange(path,lower,upper,.55f,out var clipped)&&Valid(clipped))paths.Add(clipped);
                }
                if(paths.Count!=before+2)paths.RemoveRange(before,paths.Count-before);
            }return paths.Count>0;
        }
        internal bool IsInteriorLanePaint(Entity road,Bezier4x3 curve)
        {
            if(!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;if(!EntityManager.HasComponent<NetCompositionData>(composition))return false;
            float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);float x=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(curve,.5f),width);
            foreach(var boundary in LaneDividerPolicy.Boundaries(TrafficLanes(composition)))if(math.abs(x-boundary.x)<.35f)return true;
            return false;
        }
        internal bool HasDividerMarks(Entity road)=>_marks.TryGetValue(road,out var marks)&&marks.Curves.Exists(c=>c.DividerUpgrade);
        internal bool RemovesNodeDivider(Entity node,Bezier4x3 curve)
        {
            if(!TryNodeBoundaryRoads(node,curve,out var first,out var last,false)||first==last)return false;
            return LaneDividerPolicy.Mode(EntityManager,first)==2&&IsInteriorLanePaint(first,new Bezier4x3(curve.a,curve.a,curve.a,curve.a))
                ||LaneDividerPolicy.Mode(EntityManager,last)==2&&IsInteriorLanePaint(last,new Bezier4x3(curve.d,curve.d,curve.d,curve.d));
        }
        private void AddDividerMarks(Entity road,List<StopMark> stops,List<MarkCurve> output)
        {
            byte mode=LaneDividerPolicy.Mode(EntityManager,road);if(!LaneDividerPolicy.Applied(mode)||!TryDividerPlan(road,out var paths))return;
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();var asset=palette.Explicit(mode>=10?(byte)2:(byte)1,LaneDividerPolicy.IsYellow(mode));if(asset==Entity.Null)return;
            if(mode>=12){var pairs=new List<Bezier4x3>();foreach(var path in paths){pairs.Add(NetUtils.OffsetCurveLeftSmooth(path,-EdgeSpacing(mode==12?(byte)3:(byte)4)));pairs.Add(NetUtils.OffsetCurveLeftSmooth(path,EdgeSpacing(mode==12?(byte)3:(byte)4)));}AddPassingMarks(road,false,4,pairs,palette.Explicit(1,LaneDividerPolicy.IsYellow(mode)),output);return;}
            foreach(var path in paths){var strokes=LaneDividerPolicy.Solid(mode)?new List<Bezier4x3>{path}:LaneDividerPolicy.Strokes(path,mode);if(mode==10||mode==11){byte edgeStyle=mode==10?(byte)3:(byte)4;strokes=new List<Bezier4x3>{NetUtils.OffsetCurveLeftSmooth(path,-EdgeSpacing(edgeStyle)),NetUtils.OffsetCurveLeftSmooth(path,EdgeSpacing(edgeStyle))};}foreach(var stroke in strokes){var mark=new MarkCurve(stroke,mode>=10?palette.Explicit(1,LaneDividerPolicy.IsYellow(mode)):asset);mark.DividerUpgrade=true;output.Add(mark);}}
            // Explicit dash strokes keep length/color consistent with junctions and previews.
        }
    }
}
