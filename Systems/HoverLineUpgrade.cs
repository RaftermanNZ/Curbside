using System.Collections.Generic;
using Colossal.Mathematics;using Game.Common;using Game.Net;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal sealed class HoverLineChoice
    {
        internal Entity Source; internal byte Role; // 0 center, 1 left pavement edge, 2 right pavement edge, 3 whole node edge.
        internal List<Bezier4x3> Paths,Guides;
    }
    internal static class HoverLinePolicy
    {
        internal static byte Style(string name)=>name==CenterlineUpgradePolicy.WhiteSingle?(byte)1:name==CenterlineUpgradePolicy.YellowSingle?(byte)2:name==CenterlineUpgradePolicy.WhiteDouble?(byte)3:name==CenterlineUpgradePolicy.YellowDouble?(byte)4:(byte)0;
        internal static byte Center(byte style)=>style==1?(byte)4:style==2?(byte)6:style==3?(byte)5:(byte)7;
        // Stable ties prefer a pavement edge, important on one-way highways where
        // the opposing-traffic boundary and shoulder can occupy the same position.
        internal static bool Closer(float candidate,float best)=>math.isfinite(candidate)&&candidate<best-.03f;
        internal static float Distance(Bezier4x3 path,float3 point)=>MathUtils.Distance(path,point,out _);
        internal static int Pick(IList<HoverLineChoice> choices,float3 point)
        {
            int selected=-1;float best=3f;
            if(!math.all(math.isfinite(point)))return -1;
            for(int i=0;i<choices.Count;i++)foreach(var path in choices[i].Guides??choices[i].Paths){float distance=Distance(path,point);if(Closer(distance,best)){selected=i;best=distance;}}
            return selected;
        }
    }
    public partial class DecalUpgradeTool
    {
        private Entity _lineHoverTarget;private byte _lineHoverStyle;private float _lineHoverUntil;
        private readonly List<HoverLineChoice> _lineHoverChoices=new List<HoverLineChoice>();
        private bool HandleHoverLine(Entity raw,float3 position,bool remove)
        {
            bool visibility=_selected?.name==LineShapePolicy.Visibility;byte style=visibility?(byte)1:LineShapePolicy.Style(_selected?.name);if(style==0)return false;byte centerStyle=LineShapePolicy.Center(style),edgeStyle=LineShapePolicy.Edge(style),dividerStyle=LineShapePolicy.Divider(style);byte cacheStyle=visibility?(byte)255:style;
            var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            Entity target=Entity.Null;bool node=StreetLightingSystem.TryLightTarget(EntityManager,raw,out target)&&EntityManager.HasComponent<Node>(target);
            if(!node&&!SectionUpgradeTargeting.TryRoad(EntityManager,raw,position,out target))return true;
            if(EntityManager.HasComponent<Deleted>(target)||EntityManager.HasComponent<Game.Tools.Temp>(target))return true;
            emitter.RetainPaintTarget(target);
            float now=UnityEngine.Time.realtimeSinceStartup;
            if(_lineHoverTarget!=target||_lineHoverStyle!=cacheStyle||now>=_lineHoverUntil)
            {
                _lineHoverTarget=target;_lineHoverStyle=cacheStyle;_lineHoverUntil=now+.25f;_lineHoverChoices.Clear();
                bool individual=node&&emitter.HighwayLineChoices(target,style,visibility,_lineHoverChoices);
                if(!individual){
                if(node)
                {
                    var edges=emitter.JunctionEdgePreview(target,edgeStyle);var strokes=new List<Bezier4x3>();
                    foreach(var path in edges)if(EdgeUpgradePolicy.Paired(edgeStyle)){strokes.Add(NetUtils.OffsetCurveLeftSmooth(path,-emitter.EdgeSpacing(edgeStyle)));strokes.Add(NetUtils.OffsetCurveLeftSmooth(path,emitter.EdgeSpacing(edgeStyle)));}else strokes.Add(path);
                    if(strokes.Count>0)_lineHoverChoices.Add(new HoverLineChoice{Role=3,Paths=strokes});
                    var center=emitter.NodeCenterPlan(target,centerStyle);if(center.Count>0)_lineHoverChoices.Add(new HoverLineChoice{Role=0,Paths=center});
                }
                else
                {
                    for(byte side=1;side<=2;side++){var paths=emitter.EdgePreview(target,side==1,edgeStyle);if(paths.Count>0)_lineHoverChoices.Add(new HoverLineChoice{Role=side,Paths=paths});}
                    // On a one-way highway the traffic-left boundary is also the pavement edge, not a second selectable centerline.
                    if(!emitter.UsesNativeOneWayHighwayEdges(target)&&emitter.TryCenterUpgradePlan(target,centerStyle,out var pathsCenter,out _))_lineHoverChoices.Add(new HoverLineChoice{Role=0,Paths=pathsCenter});
                }
                var dividerPaths=node?emitter.NodeDividerPlan(target):new List<Bezier4x3>();if(!node)emitter.TryDividerPlan(target,out dividerPaths);
                if(dividerPaths.Count>0){var stripes=new List<Bezier4x3>();foreach(var path in dividerPaths)if(style==3||style==4||LineShapePolicy.Passing(style)){stripes.Add(NetUtils.OffsetCurveLeftSmooth(path,-emitter.EdgeSpacing(edgeStyle)));stripes.Add(NetUtils.OffsetCurveLeftSmooth(path,emitter.EdgeSpacing(edgeStyle)));}else stripes.Add(path);_lineHoverChoices.Add(new HoverLineChoice{Role=4,Paths=stripes});}
                foreach(var choice in _lineHoverChoices){choice.Guides=choice.Paths;choice.Paths=visibility?choice.Paths:LineShapePolicy.Passing(style)?emitter.PassingPreview(target,node,choice.Role,choice.Paths):LineShapePolicy.Strokes(choice.Paths,style);}
                }
            }
            int index=HoverLinePolicy.Pick(_lineHoverChoices,position);
            HoverLineChoice selected=index>=0?_lineHoverChoices[index]:null;
            var overlay=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var job);job.Complete();
            if(selected==null){overlay.DrawCircle(new UnityEngine.Color(1,.25f,.25f,.8f),position,1f);return true;}
            var color=LineShapePolicy.Yellow(style)?new UnityEngine.Color(1,.8f,.15f,.9f):new UnityEngine.Color(1,1,1,.9f);
            foreach(var path in selected.Paths)overlay.DrawCurve(color,path,.16f);
            if(!applyAction.WasPressedThisFrame()&&!remove)return true;
            if(ModDiagnostics.TraceBuffer.Enabled&&selected.Paths.Count>0)ModDiagnostics.TraceBuffer.Record("line.hover.apply","target="+target+" node="+node+" role="+selected.Role+" source="+selected.Source+" choices="+_lineHoverChoices.Count+" hit="+position+" style="+style+" remove="+remove+" first="+selected.Paths[0].a+" last="+selected.Paths[selected.Paths.Count-1].d);
            if(!visibility&&!remove&&World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(EdgeUpgradePolicy.Paired(edgeStyle)?(byte)2:(byte)1,LineShapePolicy.Yellow(style))==Entity.Null){UpgradeFeedback.Report(_selected.name,"Paint is loading; no upgrade applied.");return true;}
            if(selected.Source!=Entity.Null)
            {
                byte intent=visibility?(remove?GranularHighwayLinePolicy.Native:GranularHighwayLinePolicy.Removed):remove?GranularHighwayLinePolicy.Removed:style;
                emitter.ApplyHighwayLine(target,selected.Source,intent);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);_lineHoverUntil=0;return true;
            }
            if(visibility){ApplyHoverVisibility(target,node,selected.Role,remove);_lineHoverUntil=0;return true;}
            if(selected.Role==4){if(node){emitter.ApplyNodeDividers(target,remove,dividerStyle);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);}else {World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(target);if(!EntityManager.HasComponent<MGRLaneDivider>(target))EntityManager.AddComponent<MGRLaneDivider>(target);EntityManager.SetComponentData(target,new MGRLaneDivider{Value=remove?(byte)2:dividerStyle});CurbLineEmissionSystem.QueueRoad(target);SegmentUpgradePolicy.QueuePaint(EntityManager,target);UpgradeFeedback.Report(_selected.name,remove?"Divider lines removed.":"Divider lines applied.");}_lineHoverUntil=0;return true;}
            if(selected.Role!=0){emitter.ApplyEdge(target,node,selected.Role==1,edgeStyle,remove);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);_lineHoverUntil=0;return true;}
            else if(node){emitter.ApplyNodeCenter(target,centerStyle,remove);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);_lineHoverUntil=0;return true;}
            else
            {
                if(!remove&&(!emitter.TryCenterUpgradePlan(target,centerStyle,out _,out var reason))){UpgradeFeedback.Report(_selected.name,reason);return true;}
                if(!remove&&World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(EdgeUpgradePolicy.Paired(edgeStyle)?(byte)2:(byte)1,LineShapePolicy.Yellow(style))==Entity.Null){UpgradeFeedback.Report(_selected.name,"Paint is loading; no upgrade applied.");return true;}
                World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(target);
                byte next=remove?CenterlineUpgradePolicy.Removed:centerStyle;
                if(!EntityManager.HasComponent<MGRCenterlineStyle>(target))EntityManager.AddComponent<MGRCenterlineStyle>(target);
                EntityManager.SetComponentData(target,new MGRCenterlineStyle{Value=next});
                if(EntityManager.HasComponent<MGRHideCenterline>(target))EntityManager.RemoveComponent<MGRHideCenterline>(target);
                CurbLineEmissionSystem.QueueRoad(target);SegmentUpgradePolicy.QueuePaint(EntityManager,target);
            }
            _lineHoverUntil=0;
            UpgradeFeedback.Report(_selected.name,(node?"Node: ":"Section: ")+(selected.Role==0?"center marking":selected.Role==3?"junction edges":selected.Role==1?"left edge":"right edge")+(remove?" removed, including native paint.":" applied."));
            return true;
        }
    }
}
