using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    public partial class NativePaintProjectionSystem
    {
        // The saved approach is the appearance identity when native tools have
        // regenerated a crossing without its old lane ID. Cache only this batch's
        // affected owners; never search the city or neighbouring intersections.
        private readonly Dictionary<Entity, List<PreviewCrosswalkApproach>> crossingApproaches = new Dictionary<Entity, List<PreviewCrosswalkApproach>>();
        private readonly Dictionary<NativeCrosswalkApproach, List<Entity>> crossingSources = new Dictionary<NativeCrosswalkApproach, List<Entity>>();
        private readonly List<Entity> crossingScratch = new List<Entity>();
        private readonly ExternalPaintOwnership externalPaint = new ExternalPaintOwnership();
        private readonly NativeProjectionLeaseIndex<Entity, Entity> paintOwnerWatch = new NativeProjectionLeaseIndex<Entity, Entity>();

        private void ClearCrossingWork()
        { crossingApproaches.Clear(); crossingSources.Clear(); crossingScratch.Clear(); externalPaint.Clear(); }

        private Entity CrossingOriginal(Entity entity)
        {
            for (int depth = 0; depth < 8 && Live(entity); depth++)
            {
                if (!EntityManager.HasComponent<Temp>(entity)) return entity;
                var temp = EntityManager.GetComponentData<Temp>(entity);
                if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel | TempFlags.Hidden)) != 0 || temp.m_Original == entity) return Entity.Null;
                entity = temp.m_Original;
            }
            return Entity.Null;
        }

        private void AddCrossingApproach(List<PreviewCrosswalkApproach> result, Entity edge, Entity previewNode)
        {
            // Hidden on a committed road is the native preview handoff, not a
            // deletion of its saved appearance. Prefer preview geometry (added
            // first), but retain this original-road fallback when it is absent.
            if (!Live(edge) || EntityManager.HasComponent<Hidden>(edge) && EntityManager.HasComponent<Temp>(edge) || externalPaint.Contains(EntityManager, edge)
                || !EntityManager.HasComponent<Edge>(edge) || !EntityManager.HasComponent<EdgeGeometry>(edge)) return;
            var preview = EntityManager.GetComponentData<Edge>(edge);
            var road = CrossingOriginal(edge); var node = CrossingOriginal(previewNode);
            if (!Live(road) || !Live(node) || !EntityManager.HasComponent<Edge>(road)) return;
            bool start = preview.m_Start == previewNode || CrossingOriginal(preview.m_Start) == node;
            bool end = preview.m_End == previewNode || CrossingOriginal(preview.m_End) == node;
            if (start == end) return;
            var committed = EntityManager.GetComponentData<Edge>(road);
            if (committed.m_Start != node && committed.m_End != node) return;
            foreach (var item in result) if (item.Road == road && item.Node == node) return;
            result.Add(new PreviewCrosswalkApproach { Road = road, Node = node, Start = start,
                Geometry = EntityManager.GetComponentData<EdgeGeometry>(edge) });
        }

        private bool CrossingApproach(Entity actor, Entity source, Entity parent, CrosswalkUpgradeSystem choices, out Entity road, out Entity node)
        {
            road = node = Entity.Null;
            if (Live(source) && !externalPaint.Contains(EntityManager, source)
                && choices.TryResolveApproach(source, out road, out node)) return true;
            if (!EntityManager.HasComponent<Temp>(parent) || externalPaint.Contains(EntityManager, parent)) return false;
            if (!crossingApproaches.TryGetValue(parent, out var candidates))
            {
                candidates = new List<PreviewCrosswalkApproach>();
                if (EntityManager.HasComponent<Edge>(parent))
                {
                    var edge = EntityManager.GetComponentData<Edge>(parent);
                    AddCrossingApproach(candidates, parent, edge.m_Start);
                    AddCrossingApproach(candidates, parent, edge.m_End);
                }
                else if (EntityManager.HasComponent<Node>(parent))
                {
                    if (EntityManager.HasBuffer<ConnectedEdge>(parent))
                        foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(parent, true)) AddCrossingApproach(candidates, edge.m_Edge, parent);
                    var original = CrossingOriginal(parent);
                    if (EntityManager.HasBuffer<ConnectedEdge>(original))
                        foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(original, true)) AddCrossingApproach(candidates, edge.m_Edge, parent);
                }
                crossingApproaches[parent] = candidates;
            }
            float best = float.MaxValue; bool tied = false;
            var path = EntityManager.GetComponentData<Curve>(actor).m_Bezier;
            foreach (var candidate in candidates)
            {
                if (!CrosswalkApproachPlacement.TryScore(candidate.Geometry, candidate.Start, path, out var score)) continue;
                if (CrosswalkPreviewChoicePolicy.Better(score, best))
                { best = score; road = candidate.Road; node = candidate.Node; tied = false; }
                else if (CrosswalkPreviewChoicePolicy.Tied(score, best)) tied = true;
            }
            return road != Entity.Null && !tied;
        }

        private bool TryCommittedCrossing(Entity actor, Entity parent, ref Entity source, out Entity alias, out bool invisible)
        {
            alias = Entity.Null; invisible = false;
            if (!EntityManager.HasComponent<PedestrianLane>(actor) || !EntityManager.HasComponent<Curve>(actor)
                || (EntityManager.GetComponentData<PedestrianLane>(actor).m_Flags & PedestrianLaneFlags.Crosswalk) == 0
                || externalPaint.Contains(EntityManager, actor)) return false;
            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            if (!CrossingApproach(actor, source, parent, choices, out var road, out var node)) return false;
            byte appearance = choices.CommittedAppearance(road, node);
            if (appearance == 0) return false;
            invisible = appearance == 2;
            if (appearance == 1)
            {
                var key = new NativeCrosswalkApproach(road, node);
                if (!crossingSources.TryGetValue(key, out var candidates))
                {
                    candidates = new List<Entity>();
                    World.GetExistingSystemManaged<IntersectionPaintSystem>()?.ReadCommittedCrosswalkSources(road, node, candidates, crossingScratch);
                    crossingSources[key] = candidates;
                }
                // Keep a proved source. Multiple composition pieces need span
                // matching when regeneration changes their IDs and curves.
                if (!candidates.Contains(source))
                {
                    if (candidates.Count > 64) return false;
                    Entity match = Entity.Null; bool ambiguous = false;
                    float best = float.MaxValue;
                    var curve = EntityManager.GetComponentData<Curve>(actor).m_Bezier;
                    foreach (var candidate in candidates)
                    {
                        if (!Live(candidate) || externalPaint.Contains(EntityManager, candidate)) continue;
                        float score = 0;
                        if (candidates.Count != 1 && (!EntityManager.HasComponent<Curve>(candidate)
                            || !PreviewCrosswalkPieceMatching.TryScore(curve, EntityManager.GetComponentData<Curve>(candidate).m_Bezier, out score))) continue;
                        if (CrosswalkPreviewChoicePolicy.Better(score, best))
                        { match = candidate; best = score; ambiguous = false; }
                        else if (CrosswalkPreviewChoicePolicy.Tied(score, best) && match != candidate) ambiguous = true;
                    }
                    if (ambiguous || match == Entity.Null) return false;
                    source = match;
                }
                if (!EntityManager.HasComponent<MGRReplacedPaint>(source)) return false;
            }
            // Change only the render definition, including invisible crossings.
            // Pedestrian routing, native Temp identity and operation flags stay native.
            alias = crossings.PreviewDefinition(Canonical(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab));
            return alias != Entity.Null;
        }
    }
}
