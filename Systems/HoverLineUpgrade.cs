using System.Collections.Generic;
using Colossal.Mathematics;using Game.Common;using Game.Net;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal sealed class HoverLineChoice
    {
        internal Entity Source, Target; internal bool Node, Individual; internal byte Role; // 0 center, 1 left pavement edge, 2 right pavement edge, 3 whole node edge.
        internal List<Bezier4x3> Paths,Guides;
        internal int Slot;
        internal bool Placed;
        internal VanillaDividerPolicy.LanePosition DividerPosition;
        internal Bezier4x3 FirstBoundary, LastBoundary;
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
        private void CollectHoverLines(Entity target,bool node,byte style,bool visibility,List<HoverLineChoice> output = null)
        {
            if(!EntityManager.Exists(target)||EntityManager.HasComponent<Deleted>(target)||EntityManager.HasComponent<Game.Tools.Temp>(target))return;
            var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            byte centerStyle = LineShapePolicy.Center(style), edgeStyle = LineShapePolicy.Edge(style);
            var choices = new List<HoverLineChoice>();
            if (!node || !emitter.HighwayLineChoices(target, style, visibility, choices))
            {
                if (node)
                {
                    var edges = emitter.JunctionEdgePreview(target, edgeStyle);
                    AddWidePaths(edges, 3, EdgeUpgradePolicy.Paired(edgeStyle));
                    var center = emitter.NodeCenterPlan(target, centerStyle);
                    if (center.Count > 0) choices.Add(new HoverLineChoice { Role = 0, Paths = center });
                }
                else
                {
                    for (byte side = 1; side <= 2; side++)
                    {
                        var paths = emitter.EdgePreview(target, side == 1, edgeStyle);
                        if (paths.Count > 0) choices.Add(new HoverLineChoice { Role = side, Paths = paths });
                    }
                    // A one-way highway shoulder is already selectable as an edge.
                    if (!emitter.IsOneWayHighway(target) && emitter.TryCenterUpgradePlan(target, centerStyle, out var center, out _))
                        choices.Add(new HoverLineChoice { Role = 0, Paths = center });
                }
                bool hasDivider = node ? emitter.IndividualDividerChoices(target, style, visibility, choices)
                    : emitter.RoadDividerChoices(target, style, visibility, choices);
                if (!hasDivider)
                {
                    var paths = node ? emitter.NodeDividerPlan(target) : null;
                    if (!node) emitter.TryDividerPlan(target, out paths);
                    AddWidePaths(paths, 4, style == 3 || style == 4 || LineShapePolicy.Passing(style));
                }
                foreach (var choice in choices)
                {
                    if (choice.Individual) continue;
                    choice.Guides = choice.Paths;
                    if (!visibility)
                        choice.Paths = LineShapePolicy.Passing(style) ? emitter.PassingPreview(target, node, choice.Role, choice.Paths)
                            : LineShapePolicy.Strokes(choice.Paths, style);
                }
            }
            foreach (var choice in choices)
            {
                choice.Target = target;
                choice.Node = node;
                (output ?? _lineHoverChoices).Add(choice);
            }

            void AddWidePaths(List<Bezier4x3> paths, byte role, bool paired)
            {
                if (paths.Count == 0) return;
                var strokes = paths;
                if (paired)
                {
                    float spacing = emitter.EdgeSpacing(edgeStyle);
                    strokes = new List<Bezier4x3>(paths.Count * 2);
                    foreach (var path in paths)
                    {
                        strokes.Add(NetUtils.OffsetCurveLeftSmooth(path, -spacing));
                        strokes.Add(NetUtils.OffsetCurveLeftSmooth(path, spacing));
                    }
                }
                choices.Add(new HoverLineChoice { Role = role, Paths = strokes });
            }
        }

        private bool HandleHoverLine(Entity raw,float3 position)
        {
            bool visibility = _selected?.name == LineShapePolicy.Visibility;
            byte style = visibility ? (byte)1 : LineShapePolicy.Style(_selected?.name);
            if (style == 0) return false;
            byte cacheStyle = visibility ? (byte)255 : style;
            var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            Entity target=Entity.Null;bool node=StreetLightingSystem.TryLightTarget(EntityManager,raw,out target)&&EntityManager.HasComponent<Node>(target);
            if(!node&&!SectionUpgradeTargeting.TryRoad(EntityManager,raw,position,out target))return true;
            if(EntityManager.HasComponent<Deleted>(target)||EntityManager.HasComponent<Game.Tools.Temp>(target))return true;
            if (_drag.Anchored)
            {
                if (_drag.StartsOnNode && !node)
                    target = new UpgradeNetworkGraph(EntityManager).NearestNode(target, position);
                if (target != Entity.Null && _drag.RouteTo(EntityManager, new UpgradeDragTarget(target), position))
                    PlanHoverLineRoute(style, visibility);
                return true;
            }
            emitter.RetainPaintTarget(target);
            float now=UnityEngine.Time.realtimeSinceStartup;
            if(_lineHoverTarget!=target||_lineHoverStyle!=cacheStyle||now>=_lineHoverUntil)
            {
                _lineHoverTarget=target;_lineHoverStyle=cacheStyle;_lineHoverUntil=now+.25f;_lineHoverChoices.Clear();
                CollectHoverLines(target,node,style,visibility);

            }
            int index=HoverLinePolicy.Pick(_lineHoverChoices,position);
            HoverLineChoice selected=index>=0?_lineHoverChoices[index]:null;
            var overlay=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var job);job.Complete();

            if(selected==null){overlay.DrawCircle(new UnityEngine.Color(1,.25f,.25f,.8f),position,1f);return true;}
            target=selected.Target;node=selected.Node;
            if(!EntityManager.Exists(target)||EntityManager.HasComponent<Deleted>(target)||EntityManager.HasComponent<Game.Tools.Temp>(target))
            {_lineHoverTarget=Entity.Null;_lineHoverUntil=0;return true;}
            emitter.RetainPaintTarget(target);
            var color=LineShapePolicy.Yellow(style)?new UnityEngine.Color(1,.8f,.15f,.9f):new UnityEngine.Color(1,1,1,.9f);
            foreach(var path in selected.Paths)overlay.DrawCurve(color,path,.16f);
            if (_drag.Active && _drag.RouteTo(EntityManager, LineTarget(selected), position))
                PlanHoverLineRoute(style, visibility);
            return true;
        }

        private readonly List<HoverLineChoice> _routeLineChoices = new List<HoverLineChoice>();
        private readonly Dictionary<UpgradeDragTarget, (Bezier4x3 First, Bezier4x3 Last)> _placedDividerDragPaths =
            new Dictionary<UpgradeDragTarget, (Bezier4x3, Bezier4x3)>();
        private static UpgradeDragTarget LineTarget(HoverLineChoice choice)
            => new UpgradeDragTarget(choice.Target, choice.Source, choice.Role, individual: choice.Individual, slot: choice.Slot);

        private void PlanHoverLineRoute(byte style, bool visibility)
        {
            _placedDividerDragPaths.Clear();
            if (!visibility && !_drag.Removing && !HoverPaintReady(style)) return;
            var anchor = _drag.Anchor;
            bool node = _drag.StartsOnNode;
            bool alignDivider = !node && VanillaDividerPolicy.Eligible(anchor);
            var steps = _drag.Steps;
            var snapped = SelectLine(anchor.Owner, anchor.Role, _drag.AnchorPosition, exact: true);
            if (alignDivider && snapped == null) return;
            if (steps.Count == 0) return;

            if (node)
            {
                foreach (var step in steps)
                {
                    var roadPath = EntityManager.GetComponentData<Curve>(step.Road).m_Bezier;
                    var approach = MathUtils.Position(roadPath, step.Forward ? .98f : .02f);
                    SelectLine(step.To, anchor.Role, approach, exact: false);
                }
                return;
            }

            var start = _drag.AnchorPosition;
            if (alignDivider)
            {
                float nearest = float.PositiveInfinity;
                foreach (var path in snapped.Guides ?? snapped.Paths)
                {
                    float gap = MathUtils.Distance(path, _drag.AnchorPosition, out float t);
                    if (gap >= nearest) continue;
                    nearest = gap;
                    start = MathUtils.Position(path, t);
                }
            }
            float lateral = RouteLineLateral(anchor.Owner, start);
            for (int i = 1; i < steps.Count; i++)
            {
                var step = steps[i];
                bool sameOrientation = step.Forward == steps[0].Forward;
                byte role = UpgradeLineRoute.RoleOnStep(anchor.Role, steps[0], step);
                var line = EntityManager.GetComponentData<Curve>(step.Road).m_Bezier;
                var point = MathUtils.Position(line, .5f);
                point.xz += MathUtils.Left(math.normalizesafe(MathUtils.Tangent(line, .5f).xz)) * (sameOrientation ? lateral : -lateral);
                SelectLine(step.Road, role, point, exact: false, reversedRoad: !sameOrientation,
                    startingDivider: snapped?.DividerPosition ?? default);
            }

            HoverLineChoice SelectLine(Entity owner, byte role, float3 point, bool exact, bool reversedRoad = false,
                VanillaDividerPolicy.LanePosition startingDivider = default)
            {
                _routeLineChoices.Clear();
                CollectHoverLines(owner, node, style, visibility, _routeLineChoices);
                HoverLineChoice best = null;
                bool followDivider = alignDivider && !exact;
                bool matchedLane = false;
                float distance = followDivider ? 1.65f : float.PositiveInfinity;
                foreach (var candidate in _routeLineChoices)
                {
                    var target = LineTarget(candidate);
                    bool matches = alignDivider
                        ? VanillaDividerPolicy.Matches(anchor, node, target, candidate.Node, exact)
                        : UpgradeLineRoute.Matches(anchor, node, target, candidate.Node, role, exact);
                    if (!matches) continue;
                    bool matchesLane = followDivider && startingDivider.Matches(candidate.DividerPosition, reversedRoad);
                    if (followDivider && !matchesLane && (matchedLane || (startingDivider.Known && candidate.DividerPosition.Known))) continue;
                    foreach (var guide in candidate.Guides ?? candidate.Paths)
                    {
                        float candidateDistance = HoverLinePolicy.Distance(guide, point);
                        if (!math.isfinite(candidateDistance) || ((!matchesLane || matchedLane) && candidateDistance >= distance)) continue;
                        best = candidate;
                        distance = candidateDistance;
                        matchedLane = matchesLane;
                    }
                }
                if (best == null) return null;
                AddLine(best);
                // A composed boundary can be split into independently authored or generated halves.
                if (alignDivider && best.DividerPosition.Known)
                    foreach (var candidate in _routeLineChoices)
                        if (candidate != best && VanillaDividerPolicy.Eligible(LineTarget(candidate))
                            && best.DividerPosition.Matches(candidate.DividerPosition, false))
                            AddLine(candidate);
                return best;
            }

            void AddLine(HoverLineChoice choice)
            {
                bool generated = choice.Slot != 0;
                if (generated && (visibility ? _drag.Removing || !choice.Placed : _drag.Removing && !choice.Placed))
                    return;
                if (!visibility && !_drag.Removing && choice.Paths.Count == 0) return;
                var selection = LineTarget(choice);
                if (!_drag.Add(selection, default, 0f)) return;
                if (generated) _placedDividerDragPaths[selection] = (choice.FirstBoundary, choice.LastBoundary);
                var highlight = generated && !visibility && !_drag.Removing ? choice.Paths : choice.Guides ?? choice.Paths;
                foreach (var path in highlight) _drag.AddHighlight(path, .55f);
            }
        }

        private float RouteLineLateral(Entity road, float3 point)
        {
            var curve = EntityManager.GetComponentData<Curve>(road).m_Bezier;
            MathUtils.Distance(curve, point, out float at);
            return math.dot(MathUtils.Left(math.normalizesafe(MathUtils.Tangent(curve, at).xz)), point.xz - MathUtils.Position(curve, at).xz);
        }

        private bool HoverPaintReady(byte style)
        {
            var palette = World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            return (style == VanillaDividerPolicy.Style ? palette.VanillaDivider
                : palette.Explicit(EdgeUpgradePolicy.Paired(LineShapePolicy.Edge(style)) ? (byte)2 : (byte)1,
                    LineShapePolicy.Yellow(style))) != Entity.Null;
        }

        private void ApplyHoverLine(UpgradeDragTarget selection, bool remove)
        {
            var target = selection.Owner;
            bool node = EntityManager.HasComponent<Node>(target);
            if (!node && !EntityManager.HasComponent<Road>(target)) return;
            bool individual = selection.Individual;
            if (selection.Part != Entity.Null && !UpgradeDragTarget.Live(EntityManager, selection.Part)) return;
            bool visibility = _selected?.name == LineShapePolicy.Visibility;
            byte style = visibility ? (byte)1 : LineShapePolicy.Style(_selected?.name);
            byte centerStyle = LineShapePolicy.Center(style);
            byte edgeStyle = LineShapePolicy.Edge(style);
            byte dividerStyle = LineShapePolicy.Divider(style);
            var emitter = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            if(!visibility&&!remove&&!HoverPaintReady(style)){UpgradeFeedback.Report(_selected.name,"Paint is loading; no upgrade applied.");return;}
            if (selection.Slot != 0)
            {
                if (_placedDividerDragPaths.TryGetValue(selection, out var geometry))
                    emitter.ApplyPlacedDivider(target, geometry.First, geometry.Last, style, remove || visibility);
                _lineHoverUntil = 0;
                return;
            }
            if(selection.Part!=Entity.Null)
            {
                byte intent=visibility?(remove?GranularHighwayLinePolicy.Native:GranularHighwayLinePolicy.Removed):remove?GranularHighwayLinePolicy.Removed:style;
                if(individual)emitter.ApplyIndividualDivider(target,selection.Part,intent);else emitter.ApplyHighwayLine(target,selection.Part,intent);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);_lineHoverUntil=0;return;
            }
            if(visibility){ApplyHoverVisibility(target,node,selection.Role,remove);_lineHoverUntil=0;return;}
            if(selection.Role==4){if(node){emitter.ApplyNodeDividers(target,remove,dividerStyle);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);}else {World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(target);if(!EntityManager.HasComponent<MGRLaneDivider>(target))EntityManager.AddComponent<MGRLaneDivider>(target);EntityManager.SetComponentData(target,new MGRLaneDivider{Value=remove?(byte)2:dividerStyle});CurbLineEmissionSystem.QueueRoad(target);SegmentUpgradePolicy.QueuePaint(EntityManager,target);UpgradeFeedback.Report(_selected.name,remove?"Divider lines removed.":"Divider lines applied.");}_lineHoverUntil=0;return;}
            if(selection.Role!=0){emitter.ApplyEdge(target,node,selection.Role==1,edgeStyle,remove);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);_lineHoverUntil=0;return;}
            else if(node){emitter.ApplyNodeCenter(target,centerStyle,remove);UpgradeFeedback.Report(_selected.name,UpgradeFeedback.Text);_lineHoverUntil=0;return;}
            else
            {
                if(!remove&&(!emitter.TryCenterUpgradePlan(target,centerStyle,out _,out var reason))){UpgradeFeedback.Report(_selected.name,reason);return;}
                if(!remove&&!HoverPaintReady(style)){UpgradeFeedback.Report(_selected.name,"Paint is loading; no upgrade applied.");return;}
                World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(target);
                byte next=remove?CenterlineUpgradePolicy.Removed:centerStyle;
                if(!EntityManager.HasComponent<MGRCenterlineStyle>(target))EntityManager.AddComponent<MGRCenterlineStyle>(target);
                EntityManager.SetComponentData(target,new MGRCenterlineStyle{Value=next});
                if(EntityManager.HasComponent<MGRHideCenterline>(target))EntityManager.RemoveComponent<MGRHideCenterline>(target);
                CurbLineEmissionSystem.QueueRoad(target);SegmentUpgradePolicy.QueuePaint(EntityManager,target);
            }
            _lineHoverUntil=0;
            UpgradeFeedback.Report(_selected.name,(node?"Node: ":"Section: ")+(selection.Role==0?"center marking":selection.Role==3?"junction edges":selection.Role==1?"left edge":"right edge")+(remove?" removed, including native paint.":" applied."));
        }
    }
}
