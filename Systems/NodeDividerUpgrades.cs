using System.Collections.Generic;using Colossal.Mathematics;
using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        private readonly ExternalPaintOwnership _nodeDividerExternal=new ExternalPaintOwnership();
        internal bool NodeDividerSource(Entity node,Entity source)
        {
            if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source))return false;
            if(EntityManager.HasComponent<Game.Net.TrackLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)||EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.PedestrianLane>(source))return false;
            if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||prefab==null||!NodeLinePolicy.IsLineGeometry(prefab)||_nodeDividerExternal.Contains(EntityManager,source))return false;
            if(IsYellowNodeBoundary(node,EntityManager.GetComponentData<Curve>(source).m_Bezier))return false;
            string name=prefab.name;
            return !NodeLinePolicy.IsProtected(name)&&name.IndexOf("Edge",System.StringComparison.OrdinalIgnoreCase)<0&&name.IndexOf("Center",System.StringComparison.OrdinalIgnoreCase)<0&&name.IndexOf("Centre",System.StringComparison.OrdinalIgnoreCase)<0&&(name.IndexOf("Lane Line",System.StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Divider",System.StringComparison.OrdinalIgnoreCase)>=0);
        }
        internal List<Bezier4x3> NodeDividerPlan(Entity node)
        {
            var result=NativeNodePaintPaths(node,3);if(result.Count>0)return result;if(!EntityManager.HasBuffer<Game.Net.SubLane>(node))return result;
            int count=0;foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true)){if(++count>256)break;if(NodeDividerSource(node,item.m_SubLane))result.Add(EntityManager.GetComponentData<Curve>(item.m_SubLane).m_Bezier);}return result;
        }
        internal List<Bezier4x3> NodeDividerPreview(Entity node,byte style)
        {var paths=NodeDividerPlan(node);if(LaneDividerPolicy.Solid(style))return paths;var strokes=new List<Bezier4x3>();foreach(var path in paths)strokes.AddRange(LaneDividerPolicy.Strokes(path,style));return strokes;}
        internal bool OwnsNodeDivider(Entity node,Entity source)=>NodeDividerSource(node,source)&&(LaneDividerPolicy.Mode(EntityManager,node)==2||LaneDividerPolicy.Applied(LaneDividerPolicy.Mode(EntityManager,node))&&_joins.TryGetValue(node,out var marks)&&marks.Curves.Exists(m=>m.DividerUpgrade));
        internal void ApplyNodeDividers(Entity node,bool remove,byte style=1)
        {
            if(Mod.Saving)return;
            if(!remove&&World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,LaneDividerPolicy.IsYellow(style))==Entity.Null){UpgradeFeedback.Report(LaneDividerPolicy.AssetName,"Divider paint is loading; no upgrade applied.");return;}
            if(!remove&&NodeDividerPlan(node).Count==0){UpgradeFeedback.Report(LaneDividerPolicy.AssetName,"Node: no authored interior divider paths to replace. No upgrade applied.");return;}
            if(!EntityManager.HasComponent<MGRLaneDivider>(node))EntityManager.AddComponent<MGRLaneDivider>(node);EntityManager.SetComponentData(node,new MGRLaneDivider{Value=remove?(byte)2:style});
            QueueNodeRefresh(node);JunctionMarkingSystem.RequestNodeRefresh(node);
            if(EntityManager.HasBuffer<Game.Net.SubLane>(node))foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))IntersectionPaintSystem.RequestSource(item.m_SubLane);
            UpgradeFeedback.Report(LaneDividerPolicy.AssetName,remove?"Node: interior divider paint removed; edges and centerlines kept.":"Node: "+LaneDividerPolicy.Label(style)+" applied to authored divider paths.");
        }
        private void AddNodeDividers(Entity node,List<MarkCurve> output)
        {
            byte mode=LaneDividerPolicy.Mode(EntityManager,node);if(!LaneDividerPolicy.Applied(mode))return;
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();var asset=palette.Explicit(mode>=10?(byte)2:(byte)1,LaneDividerPolicy.IsYellow(mode));if(asset==Entity.Null)return;
            output.RemoveAll(m=>m.DividerUpgrade);
            if(mode>=12){var pairs=new List<Bezier4x3>();foreach(var path in NodeDividerPlan(node)){pairs.Add(NetUtils.OffsetCurveLeftSmooth(path,-EdgeSpacing(mode==12?(byte)3:(byte)4)));pairs.Add(NetUtils.OffsetCurveLeftSmooth(path,EdgeSpacing(mode==12?(byte)3:(byte)4)));}AddPassingMarks(node,true,4,pairs,palette.Explicit(1,LaneDividerPolicy.IsYellow(mode)),output);return;}
            foreach(var path in NodeDividerPreview(node,mode)){var strokes=mode>=10?new List<Bezier4x3>{NetUtils.OffsetCurveLeftSmooth(path,-EdgeSpacing(mode==10?(byte)3:(byte)4)),NetUtils.OffsetCurveLeftSmooth(path,EdgeSpacing(mode==10?(byte)3:(byte)4))}:new List<Bezier4x3>{path};foreach(var stroke in strokes){var mark=new MarkCurve(stroke,mode>=10?palette.Explicit(1,LaneDividerPolicy.IsYellow(mode)):asset);mark.DividerUpgrade=true;output.Add(mark);}}
        }
    }
}
