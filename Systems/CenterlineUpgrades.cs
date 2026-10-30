using System.Collections.Generic;
using Colossal.Mathematics;using Colossal.Serialization.Entities;
using Game.Common;using Game.Net;using Game.Prefabs;
using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRCenterlineStyle:IComponentData,ISerializable
    {
        public byte Value;
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Value);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Value);
    }
    internal static class CenterlineUpgradePolicy
    {
        internal const byte Removed=3;
        internal const string Single="MGR Single Yellow Centerline",Double="MGR Double Yellow Centerline";
        internal const string WhiteSingle="MGR White Single Centerline",WhiteDouble="MGR White Double Centerline",YellowSingle="MGR Explicit Yellow Single Centerline",YellowDouble="MGR Explicit Yellow Double Centerline";
        internal static byte Style(string name)=>name==Single?(byte)1:name==Double?(byte)2:name==WhiteSingle?(byte)4:name==WhiteDouble?(byte)5:name==YellowSingle?(byte)6:name==YellowDouble?(byte)7:(byte)0;
        internal static byte Pattern(byte style)=>style>=8&&style<=13||style==1||style==4||style==6?(byte)1:style==2||style==5||style==7||style==14||style==15?(byte)2:(byte)0;
        internal static bool YellowPaint(byte style,bool themeYellow)=>style==15||style>=11&&style<=13||style==6||style==7||(style==1||style==2)&&themeYellow;
        internal static string Asset(byte style)=>style==4?WhiteSingle:style==5?WhiteDouble:style==6?YellowSingle:style==7?YellowDouble:style==2?Double:Single;
        internal static string Label(byte style)=>style>=14?(style==15?"Yellow":"White")+" passing centerline":style>=8&&style<=13?(YellowPaint(style,false)?"Yellow":"White")+" dashed centerline":style==3?"Remove centerline":(Pattern(style)==2?"Double":"Single")+" centerline"+(style>=4?(YellowPaint(style,false)?" / yellow":" / white"):" / theme");
        internal static byte Mode(EntityManager em,Entity road)=>em.HasComponent<MGRCenterlineStyle>(road)?em.GetComponentData<MGRCenterlineStyle>(road).Value:(byte)0;
        internal static bool TryBoundary(IList<NorthAmericanLinePolicy.Traffic> lanes,out float x,out float y)
        {
            x=y=0;if(lanes.Count==0)return false;var sorted=new List<NorthAmericanLinePolicy.Traffic>(lanes);sorted.Sort((a,b)=>a.X.CompareTo(b.X));
            bool found=false;float best=float.MaxValue;
            for(int i=1;i<sorted.Count;i++)
            {
                var a=sorted[i-1];var b=sorted[i];if(a.Reverse==b.Reverse||math.abs(a.Y-b.Y)>.2f)continue;
                float candidate=(a.X+a.Width*.5f+b.X-b.Width*.5f)*.5f;
                if(!math.isfinite(candidate)||math.abs(candidate)>=best)continue;
                x=candidate;y=(a.Y+b.Y)*.5f;best=math.abs(x);found=true;
            }
            if(found)return true;
            bool reverse=sorted[0].Reverse;foreach(var lane in sorted)if(lane.Reverse!=reverse)return false;
            // A one-way carriageway's yellow boundary belongs on its traffic-left edge.
            var outside=reverse?sorted[sorted.Count-1]:sorted[0];
            x=outside.X+(reverse?outside.Width*.5f:-outside.Width*.5f)+(reverse?-.15f:.15f);y=outside.Y;return math.isfinite(x)&&math.isfinite(y);
        }
        internal static bool TrySharedLane(LaneFlags flags,float3 position,out float x,out float y)
        {
            x=y=0;if((flags&(LaneFlags.Road|LaneFlags.Master|LaneFlags.Underground|LaneFlags.Twoway))!=(LaneFlags.Road|LaneFlags.Twoway)||!math.all(math.isfinite(position)))return false;
            x=position.x;y=position.y;return true;
        }
        internal static float HalfSpacing(float pairedWidth,float singleWidth)
        {float difference=(pairedWidth-singleWidth)*.5f;return math.isfinite(difference)&&difference>=.05f&&difference<=.5f?difference:.15f;}
        internal static float MedianCenter(float curb,bool left,byte style,float offset,float halfSpacing)=>curb+(left?-1:1)*(offset+(Pattern(style)==2?halfSpacing:0));
        internal static float Offset(byte style,int stripe)=>Pattern(style)==2?(stripe==0?-.15f:.15f):0;
        internal static bool RaisedMedian(NetPieceFlags flags,float height,float offsetY,float roadY)
            =>(flags&NetPieceFlags.BlockTraffic)!=0||(flags&NetPieceFlags.HasMesh)!=0&&math.isfinite(height)&&math.isfinite(offsetY)&&height>.08f&&offsetY+height>roadY+.08f;
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool HasExplicitCenterMarks(Entity road)=>_marks.TryGetValue(road,out var marks)&&marks.Curves.Exists(m=>m.CenterUpgrade);
        internal bool TryCenterUpgradePlan(Entity road,byte style,out List<Bezier4x3> paths,out string reason)
        {
            paths=new List<Bezier4x3>();reason="";bool selectedYellow=CenterlineUpgradePolicy.YellowPaint(style,Mod.Settings.YellowToolPaint);style=CenterlineUpgradePolicy.Pattern(style);
            if(style<1||style>2||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!RoadSurfacePolicy.IsPaved(EntityManager,road)){reason="Select a paved road with traffic lanes.";return false;}
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasComponent<NetCompositionData>(composition)){reason="Road composition is still loading.";return false;}
            bool boundary=CenterlineUpgradePolicy.TryBoundary(TrafficLanes(composition),out float x,out float y);
            // Shared two-way lanes (including paved alleys) have no pair of opposing lane edges.
            if(!boundary&&EntityManager.HasBuffer<NetCompositionLane>(composition))foreach(var lane in EntityManager.GetBuffer<NetCompositionLane>(composition,true))if(CenterlineUpgradePolicy.TrySharedLane(lane.m_Flags,lane.m_Position,out x,out y)){boundary=true;break;}
            if(!boundary&&RoadSurfacePolicy.IsAlley(EntityManager,road)){x=0;y=0;boundary=true;}
            if(!boundary){reason="No supported traffic boundary or shared two-way lane.";return false;}
            var data=EntityManager.GetComponentData<NetCompositionData>(composition);
            if(!math.isfinite(data.m_Width)||data.m_Width<=0){reason="Road width is unavailable.";return false;}
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);var stops=ReadStopMarks(road);
            float halfSpacing=.15f;var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();bool yellow=selectedYellow;
            var pairAsset=palette.Explicit(2,yellow);var singleAsset=palette.Explicit(1,yellow);
            if(EntityManager.HasComponent<NetLaneGeometryData>(pairAsset)&&EntityManager.HasComponent<NetLaneGeometryData>(singleAsset))halfSpacing=CenterlineUpgradePolicy.HalfSpacing(EntityManager.GetComponentData<NetLaneGeometryData>(pairAsset).m_Size.x,EntityManager.GetComponentData<NetLaneGeometryData>(singleAsset).m_Size.x);
            if(NativeHighwaySectionPlan(road,1,out var nativeCenters))
            {
                for(int pair=0;pair<nativeCenters.Count;pair+=2)for(int stripe=0;stripe<(style==2?2:1);stripe++)for(int half=0;half<2;half++)
                {
                    var path=nativeCenters[pair+half];
                    if(style==2)path=NetUtils.OffsetCurveLeftSmooth(path,stripe==0?-halfSpacing:halfSpacing);
                    paths.Add(path);
                }
                return true;
            }
            var targets=new List<float2>();var raised=new List<float2>();
            if(EntityManager.HasBuffer<NetCompositionPiece>(composition))foreach(var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition,true))
            {
                if((piece.m_SectionFlags&(NetSectionFlags.Median|NetSectionFlags.Hidden|NetSectionFlags.Underground|NetSectionFlags.Overhead))!=NetSectionFlags.Median||(piece.m_PieceFlags&NetPieceFlags.HasRoadLanes)!=0)continue;
                float width=piece.m_Size.x;if(width<=0&&EntityManager.HasComponent<NetPieceData>(piece.m_Piece))width=EntityManager.GetComponentData<NetPieceData>(piece.m_Piece).m_Width;
                if(width>.05f&&CenterlineUpgradePolicy.RaisedMedian(piece.m_PieceFlags,piece.m_Size.y,piece.m_Offset.y,y))raised.Add(new float2(piece.m_Offset.x-width*.5f,piece.m_Offset.x+width*.5f));
            }
            var traffic=TrafficLanes(composition);
            foreach(var span in NorthAmericanLinePolicy.Merge(raised))for(int side=0;side<2;side++)
            {
                // Keep the closest stripe at the median offset; a pair expands into pavement.
                float cx=CenterlineUpgradePolicy.MedianCenter(side==0?span.x:span.y,side==0,style,Mod.Settings.MedianLineOffset,halfSpacing);
                foreach(var lane in traffic)if(cx>lane.X-lane.Width*.5f&&cx<lane.X+lane.Width*.5f){targets.Add(new float2(cx,lane.Y));break;}
            }
            if(raised.Count==0)targets.Add(new float2(x,y));
            if(targets.Count==0){reason="No pavement beside the physical median can fit this marking.";return false;}
            foreach(var target in targets)for(int stripe=0;stripe<(style==2?2:1);stripe++)
            {
                float offset=style==2?(stripe==0?-halfSpacing:halfSpacing):0;float fraction=(target.x+offset)/data.m_Width+.5f;
                if(!math.isfinite(fraction)||fraction<=0||fraction>=1){reason="Center marking lies outside the pavement.";paths.Clear();return false;}
                for(int half=0;half<2;half++)
                {
                    var segment=half==0?geometry.m_Start:geometry.m_End;
                    var path=MathUtils.Lerp(segment.m_Left,segment.m_Right,target.x/data.m_Width+.5f);if(style==2)path=NetUtils.OffsetCurveLeftSmooth(path,offset);
                    path.a.y+=target.y+.01f;path.b.y+=target.y+.01f;path.c.y+=target.y+.01f;path.d.y+=target.y+.01f;
                    GetStopRange(path,stops,true,out float lower,out float upper);
                    if(StopLinePlacement.TrimRange(path,lower,upper,.55f,out var trimmed)&&Valid(trimmed))paths.Add(trimmed);else{reason="Road section has insufficient room before its stop bars.";paths.Clear();return false;}
                }
            }
            return paths.Count>0;
        }
        private void AddCenterUpgrade(Entity road,List<StopMark> stops,List<MarkCurve> curves)
        {
            byte style=CenterlineUpgradePolicy.Mode(EntityManager,road);
            if(style==0||!TryCenterUpgradePlan(road,style,out var paths,out _))return;
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();bool yellow=CenterlineUpgradePolicy.YellowPaint(style,Mod.Settings.YellowToolPaint);byte requested=style;style=CenterlineUpgradePolicy.Pattern(style);
            var asset=palette.Explicit(style,yellow);if(asset==Entity.Null)return;
            // Commit a complete replacement only after geometry and exact asset are ready.
            // Median avenues use the same replacement at each median pavement edge.
            curves.RemoveAll(m=>m.Median);if(_caps.TryGetValue(road,out var oldCaps))oldCaps.RemoveAll(c=>c.Kind==2||c.Kind==3);
            int stride=style==2?4:2;var stripeAsset=palette.Explicit(1,yellow);if(stripeAsset==Entity.Null)return;
            if(requested>=14){AddPassingMarks(road,false,0,paths,stripeAsset,curves);return;}
            foreach(var path in paths)foreach(var stroke in requested>=8?LaneDividerPolicy.Strokes(path,LineShapePolicy.Dash((byte)(requested-3))):new List<Bezier4x3>{path}){var mark=new MarkCurve(stroke,stripeAsset,0,true);mark.CenterUpgrade=true;curves.Add(mark);}
            var single=palette.Explicit(1,yellow);if(requested<8&&single!=Entity.Null)for(int stripe=0;stripe<paths.Count/2;stripe++)AddCaps(road,paths[stripe*2],paths[stripe*2+1],stops,3,single,paths.Count>stride?stripe*2/stride+1:0);
        }
    }
}
