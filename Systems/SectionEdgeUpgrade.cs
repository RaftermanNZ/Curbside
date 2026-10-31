using System.Collections.Generic;using Colossal.Mathematics;using Colossal.Serialization.Entities;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRSectionEdges:IComponentData,ISerializable
    {
        public byte Value; // 1 add both pavement edges; 2 remove native and generated edges.
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Value);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Value);
    }
    internal static class SectionEdgePolicy
    {
        internal const string AssetName="MGR Section Edge Lines";
        internal static byte Mode(EntityManager em,Entity road)=>em.HasComponent<MGRSectionEdges>(road)?em.GetComponentData<MGRSectionEdges>(road).Value:(byte)0;
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool TrySectionEdgePlan(Entity road,out List<Bezier4x3> paths,bool editing=false)
        {
            paths=new List<Bezier4x3>();
            if(!RoadSurfacePolicy.IsPaved(EntityManager,road)||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasComponent<NetCompositionData>(composition)||!EntityManager.HasBuffer<NetCompositionLane>(composition))return false;
            float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            if(!math.isfinite(width)||width<=0)return false;
            if(editing&&NativeHighwayEditableEdges(road,out var editable)){paths=editable;return true;}
            if(NativeHighwaySectionPlan(road,2,out var nativeEdges)&&nativeEdges.Count==4){paths=nativeEdges;return true;}
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);
            var lanes=EntityManager.GetBuffer<NetCompositionLane>(composition,true);var anchors=ReadCurbAnchors(composition,lanes);
            if(anchors.Count<2)anchors=ReadDecalAnchors(composition,width,lanes);
            var targets=new List<float2>();
            if(RoadSurfacePolicy.IsHighway(EntityManager,road))
            {
                foreach(var group in HighwayTrafficGroups(composition).Values)
                {
                    if(!HighwayLinePolicy.TryLeftEdge(group,Mod.Settings.MedianLineOffset,out float x,out float y))continue;
                    targets.Add(new float2(x,y+.01f));bool reverse=group[0].Reverse;float outside=reverse?float.MaxValue:float.MinValue;float height=0;
                    foreach(var lane in group){float edge=lane.X+(reverse?-1:1)*lane.Width*.5f;if(reverse?edge<outside:edge>outside){outside=edge;height=lane.Y;}}
                    targets.Add(new float2(outside+(reverse?.15f:-.15f),height+.01f));
                }
            }
            else
            {
                if(anchors.Count<2)return false;anchors.Sort((a,b)=>a.x.CompareTo(b.x));
                for(int side=0;side<2;side++)if(CurbPlacement.TryMapComposed(width,anchors[side==0?0:anchors.Count-1],side==0,Mod.Settings.CurbLineOffset,out var mapped,out _))targets.Add(new float2(mapped.x,mapped.y));
            }
            var stops=ReadStopMarks(road);
            foreach(var position in targets)
            {
                float fraction=position.x/width+.5f;if(fraction<=0||fraction>=1)continue;
                for(int half=0;half<2;half++)
                {
                    var segment=half==0?geometry.m_Start:geometry.m_End;var path=MathUtils.Lerp(segment.m_Left,segment.m_Right,fraction);
                    path.a.y+=position.y;path.b.y+=position.y;path.c.y+=position.y;path.d.y+=position.y;
                    GetStopRange(path,stops,true,out float lower,out float upper);
                    if(StopLinePlacement.TrimRange(path,lower,upper,.55f,out var clipped)&&Valid(clipped))paths.Add(clipped);
                }
            }return paths.Count>0;
        }
    }
}
