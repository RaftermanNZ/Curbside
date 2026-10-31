using System.Collections.Generic;
using Colossal.Mathematics;using Colossal.Serialization.Entities;
using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Independent bytes preserve explicit colour/style and each road side.
    public struct MGREdgePaint:IComponentData,ISerializable
    {
        public byte Left,Right;
        public void Serialize<T>(T w)where T:IWriter{w.Write(Left);w.Write(Right);}
        public void Deserialize<T>(T r)where T:IReader{r.Read(out Left);r.Read(out Right);}
    }
    internal static class EdgeUpgradePolicy
    {
        internal const string Yellow="MGR Yellow Edge Lines",DoubleWhite="MGR Double White Edge Lines",DoubleYellow="MGR Double Yellow Edge Lines";
        internal const byte Removed=5;
        internal static byte Style(string name)=>name==SectionEdgePolicy.AssetName?(byte)1:name==Yellow?(byte)2:name==DoubleWhite?(byte)3:name==DoubleYellow?(byte)4:(byte)0;
        internal static string Asset(byte style)=>style==1?SectionEdgePolicy.AssetName:style==2?Yellow:style==3?DoubleWhite:DoubleYellow;
        internal static bool Paired(byte style)=>style==3||style==4||style==12||style==13;
        internal static bool IsYellow(byte style)=>style==2||style==4||style==13||style>=9&&style<=11;
        internal static byte Mode(EntityManager em,Entity target,bool left)=>em.HasComponent<MGREdgePaint>(target)?(left?em.GetComponentData<MGREdgePaint>(target).Left:em.GetComponentData<MGREdgePaint>(target).Right):(byte)0;
        internal static MGREdgePaint Set(MGREdgePaint value,bool left,byte style){if(left)value.Left=style;else value.Right=style;return value;}
        internal static string Label(byte style)=>style>=12?(style==13?"Yellow":"White")+" passing edge":style>=6&&style<=11?(IsYellow(style)?"Yellow":"White")+" dashed edge":style==1?"White edge":style==2?"Yellow edge":style==3?"Double white edge":style==4?"Double yellow edge":"Remove edge";
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool EdgeSide(Entity road,Bezier4x3 curve)
        {
            var comp=EntityManager.GetComponentData<Composition>(road).m_Edge;
            return RoadMarkingPolicy.Lateral(EntityManager.GetComponentData<EdgeGeometry>(road),MathUtils.Position(curve,.5f),EntityManager.GetComponentData<NetCompositionData>(comp).m_Width)<0;
        }
        internal float EdgeSpacing(byte style)
        {
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            var paired=palette.Explicit(2,EdgeUpgradePolicy.IsYellow(style));var single=palette.Explicit(1,EdgeUpgradePolicy.IsYellow(style));
            return EntityManager.HasComponent<NetLaneGeometryData>(paired)&&EntityManager.HasComponent<NetLaneGeometryData>(single)?CenterlineUpgradePolicy.HalfSpacing(EntityManager.GetComponentData<NetLaneGeometryData>(paired).m_Size.x,EntityManager.GetComponentData<NetLaneGeometryData>(single).m_Size.x):.15f;
        }
        internal List<Bezier4x3> EdgePreview(Entity road,bool left,byte style)
        {
            var result=new List<Bezier4x3>();if(!TrySectionEdgePlan(road,out var paths,true))return result;
            foreach(var path in paths)if(EdgeSide(road,path)==left)
            {
                var center=ExplicitEdgeCenter(road,path,style);
                if(!EdgeUpgradePolicy.Paired(style))result.Add(center);
                else {result.Add(NetUtils.OffsetCurveLeftSmooth(center,-EdgeSpacing(style)));result.Add(NetUtils.OffsetCurveLeftSmooth(center,EdgeSpacing(style)));}
            }return result;
        }
        private Bezier4x3 ExplicitEdgeCenter(Entity road,Bezier4x3 path,byte style)
        {
            if(!EdgeUpgradePolicy.Paired(style))return path;
            float spacing=EdgeSpacing(style);var first=NetUtils.OffsetCurveLeftSmooth(path,-spacing);var last=NetUtils.OffsetCurveLeftSmooth(path,spacing);
            var comp=EntityManager.GetComponentData<Composition>(road).m_Edge;float width=EntityManager.GetComponentData<NetCompositionData>(comp).m_Width;var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);
            return math.abs(RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(first,.5f),width))<math.abs(RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(last,.5f),width))?first:last;
        }
        private void AddExplicitEdges(Entity road,List<StopMark> stops,List<MarkCurve> output)
        {
            if(!EntityManager.HasComponent<MGREdgePaint>(road)||!TrySectionEdgePlan(road,out var paths,true))return;
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            for(int side=0;side<2;side++)
            {
                bool left=side==0;byte mode=EdgeUpgradePolicy.Mode(EntityManager,road,left);if(mode==0)continue;
                var asset=mode==EdgeUpgradePolicy.Removed?Entity.Null:palette.Explicit(EdgeUpgradePolicy.Paired(mode)?(byte)2:(byte)1,EdgeUpgradePolicy.IsYellow(mode));
                if(mode!=EdgeUpgradePolicy.Removed&&asset==Entity.Null)continue;
                // Only this outer pavement edge is owned, never a physical median.
                output.RemoveAll(m=>!m.Protected&&(m.Curb||RoadSurfacePolicy.IsHighway(EntityManager,road)&&m.Median)&&EdgeSide(road,m.Curve)==left);
                if(_caps.TryGetValue(road,out var caps))caps.RemoveAll(c=>(c.Kind==0||RoadSurfacePolicy.IsHighway(EntityManager,road)&&(c.Kind==2||c.Kind==3))&&CapOnSide(road,c,left));
                if(mode==EdgeUpgradePolicy.Removed)continue;

                var selected=new List<Bezier4x3>();foreach(var path in paths)if(EdgeSide(road,path)==left)selected.Add(ExplicitEdgeCenter(road,path,mode));
                var single=palette.Explicit(1,EdgeUpgradePolicy.IsYellow(mode));
                if(mode>=12){AddPassingMarks(road,false,1,EdgePreview(road,left,mode),single,output);continue;}
                foreach(var path in EdgePreview(road,left,mode))output.Add(new MarkCurve(path,single,0,false,true));
                if(mode<6&&!RoadSurfacePolicy.IsHighway(EntityManager,road))for(int i=0;i+1<selected.Count;i+=2)AddCaps(road,selected[i],selected[i+1],stops,0,asset);
            }
        }
        private bool CapOnSide(Entity road,JoinCap cap,bool left)=>EdgeSide(road,new Bezier4x3(cap.Point,cap.Point,cap.Point,cap.Point))==left;
        internal bool OwnsExplicitEdge(Entity road,Bezier4x3 curve)
        {
            if(!EntityManager.HasComponent<MGREdgePaint>(road)||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            bool left=EdgeSide(road,curve);byte mode=EdgeUpgradePolicy.Mode(EntityManager,road,left);if(mode==0)return false;
            if(mode!=EdgeUpgradePolicy.Removed&&(!_marks.TryGetValue(road,out var marks)||!marks.Curves.Exists(m=>m.Curb&&EdgeSide(road,m.Curve)==left)))return false;
            if(NativeHighwayEditableEdges(road,out var nativeEdges)&&nativeEdges.Exists(p=>GranularHighwayLinePolicy.Matches(p,curve)))return true;
            return IsOuterEdgePaint(road,curve)||RoadSurfacePolicy.IsHighway(EntityManager,road)&&IsOpposingBoundary(road,curve);
        }
        internal void ApplyEdge(Entity target,bool node,bool left,byte style,bool remove)
        {
            if(Mod.Saving)return;
            // Highway gores retain their native-route-validated joins. Urban
            // curb-corner reconstruction must not be substituted for those paths.
            if(node&&EntityManager.HasBuffer<ConnectedEdge>(target))
            {
                var roads=new List<Entity>();foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(target,true))roads.Add(connection.m_Edge);
                if(roads.Exists(r=>RoadSurfacePolicy.IsHighway(EntityManager,r))&&!HighwayEdgePlan(target,roads,new List<Bezier4x3>())&&NativeNodePaintPaths(target,2).Count==0){UpgradeFeedback.Report(EdgeUpgradePolicy.Asset(style),"This highway node has no supported two-section shoulder continuation. Merge/gore layouts were kept; section-side upgrades remain available.");return;}
            }
            if(!remove&&World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(EdgeUpgradePolicy.Paired(style)?(byte)2:(byte)1,EdgeUpgradePolicy.IsYellow(style))==Entity.Null){UpgradeFeedback.Report(EdgeUpgradePolicy.Asset(style),"Edge paint is loading; no upgrade applied.");return;}
            if(!node&&!remove&&(!RoadSurfacePolicy.IsPaved(EntityManager,target)||EdgePreview(target,left,style).Count==0)){UpgradeFeedback.Report(EdgeUpgradePolicy.Asset(style),"No supported paved edge on this side.");return;}
            if(node&&!remove&&JunctionEdgePreview(target,style).Count==0){UpgradeFeedback.Report(EdgeUpgradePolicy.Asset(style),"Node: no supported edge paths ready; no upgrade applied.");return;}
            if(!node)World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(target);
            var value=EntityManager.HasComponent<MGREdgePaint>(target)?EntityManager.GetComponentData<MGREdgePaint>(target):default;
            value=EdgeUpgradePolicy.Set(value,left,remove?EdgeUpgradePolicy.Removed:style);
            if(node)value.Right=value.Left;
            if(!EntityManager.HasComponent<MGREdgePaint>(target))EntityManager.AddComponent<MGREdgePaint>(target);EntityManager.SetComponentData(target,value);
            if(node)
            {
                if(remove){if(EntityManager.HasComponent<MGRJunctionEdgeLines>(target))EntityManager.RemoveComponent<MGRJunctionEdgeLines>(target);if(!EntityManager.HasComponent<MGRRemoveJunctionEdges>(target))EntityManager.AddComponent<MGRRemoveJunctionEdges>(target);}
                else {if(EntityManager.HasComponent<MGRRemoveJunctionEdges>(target))EntityManager.RemoveComponent<MGRRemoveJunctionEdges>(target);if(!EntityManager.HasComponent<MGRJunctionEdgeLines>(target))EntityManager.AddComponent<MGRJunctionEdgeLines>(target);}
                QueueNodeRefresh(target);JunctionMarkingSystem.RequestNodeRefresh(target);
                if(EntityManager.HasBuffer<ConnectedEdge>(target))foreach(var e in EntityManager.GetBuffer<ConnectedEdge>(target,true))SegmentUpgradePolicy.QueuePaint(EntityManager,e.m_Edge);
            }
            else {QueueRoad(target);SegmentUpgradePolicy.QueuePaint(EntityManager,target);}
            UpgradeFeedback.Report(EdgeUpgradePolicy.Asset(style),(node?"Node: ":left?"Left side: ":"Right side: ")+(remove?"edge paint removed, including native lines.":EdgeUpgradePolicy.Label(style)+" applied."));
        }
    }
}
