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
        private bool NativeHighwayPathsReady(Entity node)
        {
            if(!HighwayNode(node)||!EntityManager.HasBuffer<Game.Net.SubLane>(node))return false;
            int count=0;foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {if(++count>256)break;int role=HighwayNodeSourceRole(node,entry.m_SubLane);if((role==1||role==2)&&Valid(EntityManager.GetComponentData<Curve>(entry.m_SubLane).m_Bezier))return true;}
            return false;
        }
        private void AddHighwayNodePaint(Entity node,List<MarkCurve> output)
        {
            bool automatic=AutomaticMarkingPolicy.Allows(EntityManager,node)&&!AutomaticMarkingPolicy.PreservesNode(EntityManager,node);
            var anchors=HighwayPaintAnchors(node);
            var native=new List<MarkCurve>();var seen=new List<Colossal.Mathematics.Bezier4x3>();int count=0;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                if(++count>256)break;var source=entry.m_SubLane;int role=HighwayNodeSourceRole(node,source);if(role==0)continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(path)||seen.Exists(p=>GranularHighwayLinePolicy.Matches(p,path)))continue;seen.Add(path);
                if(role==2&&HighwayPaintAnchorPolicy.Yellow(path,anchors))role=1;
                var joined=HighwayPaintAnchorPolicy.Join(path,anchors);
                byte legacy=role==1?CenterlineUpgradePolicy.Mode(EntityManager,node):role==2?EdgeUpgradePolicy.Mode(EntityManager,node,true):LaneDividerPolicy.Mode(EntityManager,node);
                if(role==2&&legacy==0&&EntityManager.HasComponent<MGRJunctionEdgeLines>(node))legacy=1;
                byte style=GranularHighwayLinePolicy.LegacyStyle(role,legacy,Mod.Settings.YellowToolPaint);
                if(style==GranularHighwayLinePolicy.Removed)continue;
                if(style==0&&(!automatic||SegmentUpgradePolicy.Hides(SegmentUpgradePolicy.Mode(EntityManager,node),role==1,role==2)||role==1&&CenterlineVisibility.Hidden(EntityManager,node)||role==2&&EntityManager.HasComponent<MGRRemoveJunctionEdges>(node)))continue;
                var prefab=EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(source).m_Prefab;
                if(style!=0)prefab=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,LineShapePolicy.Yellow(style));
                else if(role==1&&Mod.Settings.MedianEdgeLines){prefab=AutomaticMedianAsset();if(!string.IsNullOrEmpty(Mod.Settings.MedianLineStyle)&&_nativeLineDefinitions.TryGetValue(Mod.Settings.MedianLineStyle,out var medianStyle))prefab=medianStyle;if(Mod.Settings.EuropeanMarkings&&prefab!=Entity.Null)prefab=ThemeEquivalent(prefab);}else if(Mod.Settings.EuropeanMarkings)prefab=ThemeEquivalent(prefab);
                if(prefab==Entity.Null)continue;
                var paths=style==0?new List<Colossal.Mathematics.Bezier4x3>{joined}:HighwayLinePaths(node,joined,style,role);
                foreach(var stroke in paths){var mark=new MarkCurve(stroke,prefab,0,role==1,role==2){DividerUpgrade=role==3,NativeSource=source};native.Add(mark);}
            }
            // Native topology identifies each path; shared segment paint anchors
            // retain configured segment offsets at its endpoints. Legacy
            // styles are mapped per path instead of reconstructed across a fork.
            output.RemoveAll(m=>!m.Protected&&(m.Median||m.Curb||m.DividerUpgrade));
            output.AddRange(native);
        }
        internal bool OwnsHighwayNodePaint(Entity node,Entity source)
        {
            if(!HighwayNode(node))return false;
            if(TryHighwayLineIntent(node,source,out var intent))
            {if(intent.Style==GranularHighwayLinePolicy.Native)return false;if(intent.Style==GranularHighwayLinePolicy.Removed)return true;}
            return _joins.TryGetValue(node,out var marks)&&marks.Curves.Exists(m=>m.NativeSource==source||m.NativeSource!=Entity.Null&&EntityManager.HasComponent<Curve>(source)&&GranularHighwayLinePolicy.Matches(m.Curve,EntityManager.GetComponentData<Curve>(source).m_Bezier));
        }
    }
}
