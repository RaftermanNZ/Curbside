using System.Collections.Generic;using Colossal.Mathematics;using Game.Common;using Game.Net;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        internal bool HighwayEdgeSource(Entity node,Bezier4x3 source)
        {
            if(!EntityManager.HasBuffer<ConnectedEdge>(node))return false;
            var roads=new List<Entity>();foreach(var edge in EntityManager.GetBuffer<ConnectedEdge>(node,true))roads.Add(edge.m_Edge);
            var paths=new List<Bezier4x3>();if(!HighwayEdgePlan(node,roads,paths))return false;
            foreach(var path in paths)
            {
                bool matches=true;for(int i=0;i<=4;i++)if(MathUtils.Distance(path,MathUtils.Position(source,i/4f),out _)>.6f){matches=false;break;}
                if(matches&&math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(path,.5f)),math.normalizesafe(MathUtils.Tangent(source,.5f))))>.8f)return true;
            }return false;
        }
        private bool HighwayEdgePlan(Entity node,List<Entity> roads,List<Bezier4x3> result)
        {
            if(roads.Count!=2||!roads.TrueForAll(r=>RoadSurfacePolicy.IsHighway(EntityManager,r))||!HighwayRoute(node,roads[0],roads[1])&&!HighwayRoute(node,roads[1],roads[0]))return false;
            var ends=new List<JoinCap[]>();
            foreach(var road in roads)
            {
                if(!TrySectionEdgePlan(road,out var paths)||paths.Count!=4)return false;
                bool start=EntityManager.GetComponentData<Edge>(road).m_Start==node;var caps=new JoinCap[2];
                for(int side=0;side<2;side++){var path=paths[side*2+(start?0:1)];caps[side]=new JoinCap{Point=start?path.a:path.d,Outward=math.normalizesafe(MathUtils.Tangent(path,start?0:1))*(start?-1:1)};}ends.Add(caps);
            }
            float direct=math.distancesq(ends[0][0].Point,ends[1][0].Point)+math.distancesq(ends[0][1].Point,ends[1][1].Point),cross=math.distancesq(ends[0][0].Point,ends[1][1].Point)+math.distancesq(ends[0][1].Point,ends[1][0].Point);
            for(int side=0;side<2;side++)
            {
                var a=ends[0][side];var b=ends[1][cross<direct?1-side:side];
                if(!NodeLinePlacement.CanJoin(a.Point,a.Outward,b.Point,b.Outward,2,0,0)){result.Clear();return false;}
                var path=NodeLinePlacement.Connect(a.Point,a.Outward,b.Point,b.Outward);if(!Valid(path)){result.Clear();return false;}result.Add(path);
            }return true;
        }
    }
}
