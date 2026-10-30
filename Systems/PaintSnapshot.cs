using Colossal.Mathematics;using Game.Common;using Game.Net;using Game.Prefabs;using Game.Tools;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        private Entity _diagnosticPaintTarget;
        internal void RetainPaintTarget(Entity target){if(ModDiagnostics.TraceBuffer.Enabled)_diagnosticPaintTarget=target;}
        internal void RecordPaintSnapshot()
        {
            if(!ModDiagnostics.TraceBuffer.Enabled)return;
            var ray=World.GetOrCreateSystemManaged<ToolRaycastSystem>();
            var road=_diagnosticPaintTarget;
            if(!EntityManager.Exists(road)&&ray.GetRaycastResult(out var hit)&&SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var current))road=current;
            if(!EntityManager.Exists(road)||EntityManager.HasComponent<Deleted>(road)||EntityManager.HasComponent<Temp>(road)||!EntityManager.HasComponent<Road>(road)&&!EntityManager.HasComponent<Node>(road)){ModDiagnostics.TraceBuffer.Record("snapshot.paintSources","No valid Curbside hover target retained. Hover a road or node before writing a snapshot.");return;}
            ModDiagnostics.TraceBuffer.Record("snapshot.paintTarget",road+" (current ray or last valid Curbside hover)");
            Dependency.Complete();int remaining=24;
            void Read(Entity owner)
            {
                if(EntityManager.HasComponent<Road>(owner))ModDiagnostics.TraceBuffer.Record("snapshot.sectionStyles",owner+" center="+CenterlineUpgradePolicy.Mode(EntityManager,owner)+" left="+EdgeUpgradePolicy.Mode(EntityManager,owner,true)+" right="+EdgeUpgradePolicy.Mode(EntityManager,owner,false)+" divider="+LaneDividerPolicy.Mode(EntityManager,owner)+" nativeOneWayEdges="+UsesNativeOneWayHighwayEdges(owner));
                if(_marks.TryGetValue(owner,out var emitted)||_joins.TryGetValue(owner,out emitted))
                {
                    int printed=0;foreach(var mark in emitted.Curves){if(++printed>12)break;ModDiagnostics.TraceBuffer.Record("snapshot.emittedPaint",owner+" source="+mark.NativeSource+" median="+mark.Median+" curb="+mark.Curb+" start="+mark.Curve.a+" end="+mark.Curve.d);}
                }
                if(EntityManager.HasComponent<Node>(owner)&&HighwayNode(owner))
                {
                    int printed=0;foreach(var anchor in HighwayPaintAnchors(owner)){if(++printed>12)break;ModDiagnostics.TraceBuffer.Record("snapshot.highwayAnchor",owner+" yellow="+anchor.Yellow+" native="+anchor.Native+" paint="+anchor.Paint);}
                }
                if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return;
                if(EntityManager.HasComponent<Node>(owner))ModDiagnostics.TraceBuffer.Record("snapshot.nodePaint",owner+" roundabout="+EntityManager.HasComponent<Roundabout>(owner)+" nativeCenter="+NativeNodePaintPaths(owner,1).Count+" nativeEdge="+NativeNodePaintPaths(owner,2).Count+" nativeDivider="+NativeNodePaintPaths(owner,3).Count+" style="+CenterlineUpgradePolicy.Mode(EntityManager,owner));
                foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
                {
                    var lane=entry.m_SubLane;
                    if(remaining<=0)return;
                    if(EntityManager.HasComponent<MGRCurbMarking>(lane)||EntityManager.HasComponent<MGRIntersectionPaint>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                    if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab,out var prefab)||!NodeLinePolicy.IsLineGeometry(prefab))continue;
                    remaining--;
                    var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    ModDiagnostics.TraceBuffer.Record("snapshot.lineAnchors",lane+" a="+curve.a+" d="+curve.d);
                    bool edge=EntityManager.HasComponent<Road>(owner);
                    ModDiagnostics.TraceBuffer.Record("snapshot.paintSource",lane+" "+prefab.name+" owner="+owner+" road="+edge+" hidden="+EntityManager.HasComponent<Hidden>(lane)+" replaced="+EntityManager.HasComponent<MGRReplacedPaint>(lane)+" junctionHidden="+EntityManager.HasComponent<MGRHiddenJunctionLine>(lane)+" external="+_externalPaint.Contains(EntityManager,lane)+" medianCovered="+(edge&&HasMedianReplacement(owner,curve))+" curbCovered="+(edge&&HasCurbReplacement(owner,curve))+" centerRole="+(edge?IsOpposingBoundary(owner,curve):IsYellowNodeBoundary(owner,curve)));
                }
            }
            if(EntityManager.HasBuffer<MGRHighwayLineIntent>(road))foreach(var intent in EntityManager.GetBuffer<MGRHighwayLineIntent>(road,true))ModDiagnostics.TraceBuffer.Record("snapshot.highwayLineIntent","node="+road+" role="+intent.Role+" style="+intent.Style+" start="+intent.A+" end="+intent.D);
            Read(road);if(EntityManager.HasBuffer<ConnectedEdge>(road))foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(road,true))Read(entry.m_Edge);if(EntityManager.HasComponent<Edge>(road)){var e=EntityManager.GetComponentData<Edge>(road);Read(e.m_Start);Read(e.m_End);}
        }
    }
}
