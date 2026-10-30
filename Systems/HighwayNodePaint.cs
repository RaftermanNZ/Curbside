using System;using System.Collections.Generic;using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class HighwayNodePaintPolicy
    {
        internal static int Role(string name,bool opposing)
        {
            if(NodeLinePolicy.IsProtected(name))return 0;
            if(opposing||name.IndexOf("Center Line",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Centre Line",StringComparison.OrdinalIgnoreCase)>=0)return 1;
            if(name.IndexOf("Edge Line",StringComparison.OrdinalIgnoreCase)>=0)return 2;
            if(name.IndexOf("Lane Line",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Divider",StringComparison.OrdinalIgnoreCase)>=0)return 3;
            return 0;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        private bool HighwayNode(Entity node)
        {
            if(!EntityManager.HasBuffer<ConnectedEdge>(node))return false;int count=0;
            foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(node,true))
            {
                var road=entry.m_Edge;if(!EntityManager.HasComponent<Road>(road)||EntityManager.HasComponent<Deleted>(road)||EntityManager.HasComponent<Game.Tools.Temp>(road))continue;
                if(!RoadSurfacePolicy.IsHighway(EntityManager,road))return false;count++;
            }return count>=2;
        }
        private int HighwayNodeSourceRole(Entity node,Entity source)
        {
            if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))return 0;
            if(EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)||EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||EntityManager.HasComponent<Game.Net.TrackLane>(source))return 0;
            if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab))return 0;
            return HighwayNodePaintPolicy.Role(prefab.name,IsYellowNodeBoundary(node,EntityManager.GetComponentData<Curve>(source).m_Bezier));
        }
        private static bool HasHighwayRole(List<MarkCurve> curves,int role)=>curves.Exists(m=>role==1?m.Median:role==2?m.Curb:m.DividerUpgrade);
        private void AddHighwayNodePaint(Entity node,List<MarkCurve> output)
        {
            if(!HighwayNode(node)||!EntityManager.HasBuffer<Game.Net.SubLane>(node))return;
            // Generated shoulder/gore geometry takes precedence. For unsupported
            // layouts, retain authored paths as owned paint rather than erase a role.
            bool center=HasHighwayRole(output,1),edge=HasHighwayRole(output,2);
            int count=0;foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                if(++count>256)break;var source=entry.m_SubLane;int role=HighwayNodeSourceRole(node,source);if(role==0||role==1&&center||role==2&&edge||role==3&&LaneDividerPolicy.Mode(EntityManager,node)!=0)continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(path))continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if(role==1)prefab=ResolveCenterStyle(prefab);else if(Mod.Settings.EuropeanMarkings)prefab=ThemeEquivalent(prefab);
                if(prefab==Entity.Null)continue;
                var mark=new MarkCurve(path,prefab,0,role==1,role==2);mark.DividerUpgrade=role==3;output.Add(mark);
            }
        }
        internal bool OwnsHighwayNodePaint(Entity node,Entity source)
        {
            if(!HighwayNode(node)||!_joins.TryGetValue(node,out var marks))return false;
            int role=HighwayNodeSourceRole(node,source);return role!=0&&HasHighwayRole(marks.Curves,role);
        }
    }
}
