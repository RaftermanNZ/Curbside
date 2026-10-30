using System.Collections.Generic;using Colossal.Mathematics;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class LineShapePolicy
    {
        internal const string Visibility="MGR Hover Line Visibility";
        internal static byte Style(string name){if(name==PassingLinePolicy.White)return 11;if(name==PassingLinePolicy.Yellow)return 12;byte solid=HoverLinePolicy.Style(name);if(solid!=0)return solid;byte divider=LaneDividerPolicy.Style(name);return divider==1?(byte)5:divider==3?(byte)6:divider==4?(byte)7:divider==5?(byte)8:divider==6?(byte)9:divider==7?(byte)10:(byte)0;}
        internal static bool Yellow(byte style)=>style==2||style==4||style>=8&&style!=11;
        internal static bool Dashed(byte style)=>style>=5&&style<=10;
        internal static byte Dash(byte style)=>style==6||style==9?(byte)3:style==7||style==10?(byte)4:(byte)1;
        internal static bool Passing(byte style)=>style==11||style==12;
        internal static byte Center(byte style)=>Passing(style)?(byte)(style+3):Dashed(style)?(byte)(style+3):HoverLinePolicy.Center(style);
        internal static byte Edge(byte style)=>Passing(style)?(byte)(style+1):Dashed(style)?(byte)(style+1):style;
        internal static byte Divider(byte style)=>Passing(style)?(byte)(style+1):style==1?(byte)8:style==2?(byte)9:style==3?(byte)10:style==4?(byte)11:style==5?(byte)1:style==6?(byte)3:style==7?(byte)4:style==8?(byte)5:style==9?(byte)6:(byte)7;
        internal static List<Bezier4x3> Strokes(List<Bezier4x3> paths,byte style){if(!Dashed(style))return paths;var result=new List<Bezier4x3>();foreach(var path in paths)result.AddRange(LaneDividerPolicy.Strokes(path,Dash(style)));return result;}
    }
    public partial class CurbLineEmissionSystem
    {
        private void ApplyDashedEdges(Entity target,List<MarkCurve> marks,bool node)
        {
            if(!EntityManager.HasComponent<MGREdgePaint>(target))return;
            var result=new List<MarkCurve>();
            foreach(var mark in marks){byte mode=mark.Curb&&!mark.Protected?EdgeUpgradePolicy.Mode(EntityManager,target,node||EdgeSide(target,mark.Curve)):(byte)0;
                if(mode<6||mode>11){result.Add(mark);continue;}
                foreach(var stroke in LaneDividerPolicy.Strokes(mark.Curve,LineShapePolicy.Dash((byte)(mode-1))))result.Add(new MarkCurve(stroke,mark.Prefab,0,false,true));}
            marks.Clear();marks.AddRange(result);
        }
    }
}
