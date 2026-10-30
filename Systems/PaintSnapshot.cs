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
            if(ray.GetRaycastResult(out var hit)&&SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var current))road=current;
            if(!EntityManager.Exists(road)||EntityManager.HasComponent<Deleted>(road)||EntityManager.HasComponent<Temp>(road)||!EntityManager.HasComponent<Road>(road)&&!EntityManager.HasComponent<Node>(road)){ModDiagnostics.TraceBuffer.Record("snapshot.paintSources","No valid Curbside hover target retained. Hover a road or node before writing a snapshot.");return;}
            ModDiagnostics.TraceBuffer.Record("snapshot.paintTarget",road+" (current ray or last valid Curbside hover)");
            Dependency.Complete();int remaining=24;
            void Read(Entity owner)
            {
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
            Read(road);if(EntityManager.HasBuffer<ConnectedEdge>(road))foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(road,true))Read(entry.m_Edge);if(EntityManager.HasComponent<Edge>(road)){var e=EntityManager.GetComponentData<Edge>(road);Read(e.m_Start);Read(e.m_End);}
        }
    }
}
