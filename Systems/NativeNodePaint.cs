using System.Collections.Generic;using Colossal.Mathematics;using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        internal bool SpecialPaintNode(Entity node)=>EntityManager.HasComponent<Roundabout>(node)||EntityManager.HasBuffer<ConnectedEdge>(node)&&EntityManager.GetBuffer<ConnectedEdge>(node,true).Length==1;
        internal List<Bezier4x3> NativeNodePaintPaths(Entity node,int role)
        {
            var result=new List<Bezier4x3>();if(!EntityManager.HasBuffer<Game.Net.SubLane>(node))return result;int count=0;
            foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true)){
                if(++count>256)break;var source=item.m_SubLane;
                if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))continue;
                if(EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)||EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||EntityManager.HasComponent<Game.Net.TrackLane>(source))continue;
                if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab))continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(path))continue;
                if(HighwayNodePaintPolicy.Role(prefab.name,IsYellowNodeBoundary(node,path))==role)result.Add(path);
            }return result;
        }
        private bool AddNativeNodeEdges(Entity node,List<MarkCurve> output,byte style)
        {
            var paths=NativeNodePaintPaths(node,2);if(paths.Count==0)return false;
            byte mode=style!=0?style:EdgeUpgradePolicy.Mode(EntityManager,node,true);if(mode==EdgeUpgradePolicy.Removed)return true;
            var asset=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(EdgeUpgradePolicy.Paired(mode)?(byte)2:(byte)1,EdgeUpgradePolicy.IsYellow(mode));if(asset==Entity.Null)return false;
            output.RemoveAll(m=>m.Curb&&!m.Protected);foreach(var path in paths)output.Add(new MarkCurve(path,asset,0,false,true));return true;
        }
    }
}
