using System.Collections.Generic;using Colossal.Mathematics;
using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        internal List<Bezier4x3> NodeCenterPlan(Entity node,byte style)
        {
            byte requestedStyle=style;bool selectedYellow=CenterlineUpgradePolicy.YellowPaint(style,Mod.Settings.YellowToolPaint);style=CenterlineUpgradePolicy.Pattern(style);var result=new List<Bezier4x3>();if(style<1||style>2||!EntityManager.HasBuffer<ConnectedEdge>(node))return result;
            var roads=new List<Entity>();foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(EntityManager.HasComponent<Road>(entry.m_Edge)&&!EntityManager.HasComponent<Deleted>(entry.m_Edge)&&!EntityManager.HasComponent<Game.Tools.Temp>(entry.m_Edge))roads.Add(entry.m_Edge);
            // Two-section transitions use composed pavement boundaries and the
            // same ordered-stripe planner as automatic median transitions.
            if(roads.Count==2&&!SpecialPaintNode(node))
            {
                if(!HighwayRoute(node,roads[0],roads[1])&&!HighwayRoute(node,roads[1],roads[0]))return result;
                var caps=new List<List<JoinCap>>();var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();var single=palette.Explicit(1,selectedYellow);if(single==Entity.Null)return result;
                foreach(var road in roads)
                {
                    if(!TryCenterUpgradePlan(road,requestedStyle,out var paths,out _))return result;
                    var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;
                    var group=new List<JoinCap>();int stride=style==2?4:2;
                    for(int i=0;i<paths.Count;i+=2)
                    {
                        var path=paths[i+(start?0:1)];var point=start?path.a:path.d;var outward=math.normalizesafe(MathUtils.Tangent(path,start?0:1))*(start?-1:1);
                        group.Add(new JoinCap{Node=node,Point=point,Outward=outward,Kind=3,Side=0,Boundary=i/stride+1,Prefab=single});
                    }caps.Add(group);
                }
                var marks=new List<MarkCurve>();if(TryCenterTransitions(node,caps[0],caps[1],marks))foreach(var mark in marks)result.Add(mark.Curve);
                return result;
            }
            // Intersections may only replace an existing non-routing paint path;
            // never guess a centerline across a crossing, island or turn lane.
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(node))return result;int inspected=0;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                if(++inspected>256)break;var source=entry.m_SubLane;
                if(!NativeNodeCenterSource(node,source))continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                if(style==1)result.Add(path);
                else {result.Add(NetUtils.OffsetCurveLeftSmooth(path,-.15f));result.Add(NetUtils.OffsetCurveLeftSmooth(path,.15f));}
            }return result;
        }
        private bool NativeNodeCenterSource(Entity node,Entity source)
        {
            if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))return false;
            if(EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)||EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||EntityManager.HasComponent<Game.Net.TrackLane>(source))return false;
            if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab)||NodeLinePolicy.IsProtected(prefab.name))return false;
            return HighwayNodePaintPolicy.Role(prefab.name,IsYellowNodeBoundary(node,EntityManager.GetComponentData<Curve>(source).m_Bezier))==1;
        }
        internal bool OwnsExplicitNodeCenter(Entity node)=>CenterlineUpgradePolicy.Mode(EntityManager,node)==CenterlineUpgradePolicy.Removed||CenterlineUpgradePolicy.Mode(EntityManager,node)!=0&&_joins.TryGetValue(node,out var marks)&&marks.Curves.Exists(m=>m.CenterUpgrade);
        internal void ApplyNodeCenter(Entity node,byte style,bool remove)
        {
            if(Mod.Saving)return;
            string asset=CenterlineUpgradePolicy.Asset(style);
            if(!remove&&NodeCenterPlan(node,style).Count==0){UpgradeFeedback.Report(asset,"Node: no supported center marking path is ready. Nothing changed.");return;}
            if(!EntityManager.HasComponent<MGRCenterlineStyle>(node))EntityManager.AddComponent<MGRCenterlineStyle>(node);EntityManager.SetComponentData(node,new MGRCenterlineStyle{Value=remove?CenterlineUpgradePolicy.Removed:style});
            QueueNodeRefresh(node);RefreshNodePaint(node);
            UpgradeFeedback.Report(asset,remove?"Node: center paint removed, including native lines. Section choices kept.":"Node: center marking applied. Section choices kept.");
        }
        private void AddNodeCenter(Entity node,List<MarkCurve> output)
        {
            byte style=CenterlineUpgradePolicy.Mode(EntityManager,node);if(style==0)return;
            if(style==CenterlineUpgradePolicy.Removed){output.RemoveAll(m=>m.Median&&!m.Protected);return;}
            var paths=NodeCenterPlan(node,style);var single=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,CenterlineUpgradePolicy.YellowPaint(style,Mod.Settings.YellowToolPaint));
            if(paths.Count==0||single==Entity.Null)return;
            output.RemoveAll(m=>m.Median&&!m.Protected);
            if(style>=14){AddPassingMarks(node,true,0,paths,single,output);return;}
            foreach(var path in paths)foreach(var stroke in style>=8?LaneDividerPolicy.Strokes(path,LineShapePolicy.Dash((byte)(style-3))):new List<Bezier4x3>{path}){var mark=new MarkCurve(stroke,single,0,true);mark.CenterUpgrade=true;output.Add(mark);}
        }
        internal void RefreshNodePaint(Entity node)
        {
            JunctionMarkingSystem.RequestNodeRefresh(node);
            if(EntityManager.HasBuffer<Game.Net.SubLane>(node))foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))IntersectionPaintSystem.RequestSource(entry.m_SubLane);
        }
    }
}
