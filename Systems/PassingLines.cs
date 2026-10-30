using System.Collections.Generic;using Colossal.Mathematics;using Game.Net;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class PassingLinePolicy
    {
        internal const string White="MGR White Passing Line",Yellow="MGR Yellow Passing Line";
        internal static List<Bezier4x3> Pair(Bezier4x3 first,Bezier4x3 second,float3 inward)
        {
            bool dashFirst=math.distancesq(MathUtils.Position(first,.5f),inward)<=math.distancesq(MathUtils.Position(second,.5f),inward);
            var result=new List<Bezier4x3>{dashFirst?second:first};result.AddRange(LaneDividerPolicy.Strokes(dashFirst?first:second,1));return result;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        private float3 PassingReference(Entity target,Bezier4x3 path)
        {
            var middle=MathUtils.Position(path,.5f);
            if(EntityManager.HasComponent<Curve>(target)){var center=EntityManager.GetComponentData<Curve>(target).m_Bezier;MathUtils.Distance(center,middle,out float t);return MathUtils.Position(center,t);}
            return EntityManager.HasComponent<Node>(target)?EntityManager.GetComponentData<Node>(target).m_Position:middle;
        }
        internal List<Bezier4x3> PassingPreview(Entity target,bool node,byte role,List<Bezier4x3> paths)
        {
            var result=new List<Bezier4x3>();int stride=!node&&role==0?4:2;
            for(int i=0;i<paths.Count;i+=stride){if(i+stride>paths.Count){for(int j=i;j<paths.Count;j++)result.Add(paths[j]);break;}for(int half=0;half<stride/2;half++){var a=paths[i+half];var b=paths[i+half+stride/2];result.AddRange(PassingLinePolicy.Pair(a,b,PassingReference(target,MathUtils.Lerp(a,b,.5f))));}}
            return result;
        }
        private void AddPassingMarks(Entity target,bool node,byte role,List<Bezier4x3> paths,Entity asset,List<MarkCurve> output)
        {foreach(var stroke in PassingPreview(target,node,role,paths)){var mark=new MarkCurve(stroke,asset,0,role==0,role==1);mark.CenterUpgrade=role==0;mark.DividerUpgrade=role==4;output.Add(mark);}}
    }
}
