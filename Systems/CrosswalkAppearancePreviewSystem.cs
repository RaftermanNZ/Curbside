using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Serialization;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    public struct MGRCrosswalkPreviewRequest : IComponentData { }

    // A crossing keeps its committed appearance and paint. Only the native
    // temporary zebra's render definition changes; pedestrian routing is intact.
    public partial class CrosswalkAppearancePreviewSystem : GameSystemBase, IPreSerialize
    {
        private sealed class Binding
        {
            internal Entity Road, Node, Source;
            internal bool Dirty = true;
            internal readonly List<Entity> PaintSources = new List<Entity>();
        }

        private static CrosswalkAppearancePreviewSystem active;
        private EntityQuery changes, restoreRequests;
        private readonly Dictionary<Entity, Binding> bindings = new Dictionary<Entity, Binding>();
        private readonly Dictionary<Entity, List<PreviewCrosswalkApproach>> approaches = new Dictionary<Entity, List<PreviewCrosswalkApproach>>();
        private readonly Dictionary<Entity, Entity> aliases = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, Entity> definitions = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, Entity> reverse = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, TemporaryPreviewWrite> writes = new Dictionary<Entity, TemporaryPreviewWrite>();
        private readonly HashSet<Entity> retained = new HashSet<Entity>();
        private readonly List<Entity> stale = new List<Entity>();
        private readonly List<Entity> scratch = new List<Entity>();
        private readonly List<Entity> legacySources = new List<Entity>();
        private readonly ExternalPaintOwnership external = new ExternalPaintOwnership();

        protected override void OnCreate()
        {
            base.OnCreate();
            active = this;
            changes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PedestrianLane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<MGRCrosswalkPreviewRequest>() },
                None = new[] { ComponentType.ReadOnly<MGRTemporaryPreviewPaint>() }
            }, new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<MGRCrosswalkRestoreRequest>() } });
            RequireForUpdate(changes);
            restoreRequests = GetEntityQuery(ComponentType.ReadOnly<MGRCrosswalkRestoreRequest>());
        }

        private bool Alive(Entity entity) => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);

        internal static bool KeepsOriginal(World world, Entity source)
            => active != null && active.World == world && active.retained.Contains(source);

        internal static void SourceChanged(World world, Entity source)
        {
            var system = active;
            if (system == null || system.World != world || Mod.Saving) return;
            var choices = world.GetExistingSystemManaged<CrosswalkUpgradeSystem>();
            Entity road = Entity.Null, node = Entity.Null;
            bool resolved = choices != null && choices.TryResolveApproach(source, out road, out node);
            foreach (var pair in system.bindings)
                if ((pair.Value.Source == source || pair.Value.PaintSources.Contains(source)
                    || resolved && pair.Value.Road == road && pair.Value.Node == node)
                    && system.Alive(pair.Key) && system.EntityManager.HasComponent<Temp>(pair.Key)
                    && !system.EntityManager.HasComponent<MGRCrosswalkPreviewRequest>(pair.Key))
                {
                    pair.Value.Dirty = true;
                    system.EntityManager.AddComponent<MGRCrosswalkPreviewRequest>(pair.Key);
                }
        }

        private Entity Original(Entity entity)
        {
            for (int depth = 0; depth < 8 && Alive(entity); depth++)
            {
                if (!EntityManager.HasComponent<Temp>(entity)) return entity;
                var temp = EntityManager.GetComponentData<Temp>(entity);
                if (!PreviewVisualPolicy.Active(temp.m_Flags) || temp.m_Original == entity) return Entity.Null;
                entity = temp.m_Original;
            }
            return Entity.Null;
        }

        private void AddApproach(List<PreviewCrosswalkApproach> result, Entity edge, Entity previewNode)
        {
            if (!Alive(edge) || EntityManager.HasComponent<Hidden>(edge)
                || !EntityManager.HasComponent<Edge>(edge) || !EntityManager.HasComponent<EdgeGeometry>(edge)) return;
            var preview = EntityManager.GetComponentData<Edge>(edge);
            var road = Original(edge);
            var node = Original(previewNode);
            if (!Alive(road) || !Alive(node) || !EntityManager.HasComponent<Edge>(road)) return;
            bool start = preview.m_Start == previewNode || Original(preview.m_Start) == node;
            bool end = preview.m_End == previewNode || Original(preview.m_End) == node;
            if (start == end) return; // Missing/ambiguous topology, including a loop.
            var committed = EntityManager.GetComponentData<Edge>(road);
            if (committed.m_Start != node && committed.m_End != node) return;
            foreach (var item in result)
                if (item.Road == road && item.Node == node) return;
            result.Add(new PreviewCrosswalkApproach
            {
                Road = road, Node = node, Start = start,
                Geometry = EntityManager.GetComponentData<EdgeGeometry>(edge)
            });
        }

        private bool Resolve(Entity lane, CrosswalkUpgradeSystem choices, out Entity road, out Entity node, out Entity source)
        {
            road = node = Entity.Null;
            source = Original(lane);
            if (Alive(source) && EntityManager.HasComponent<PedestrianLane>(source)
                && (EntityManager.GetComponentData<PedestrianLane>(source).m_Flags & PedestrianLaneFlags.Crosswalk) != 0
                && !external.Contains(EntityManager, source)
                && choices.TryResolveApproach(source, out road, out node)) return true;
            source = Entity.Null;
            var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            if (!Alive(owner) || !EntityManager.HasComponent<Temp>(owner) || Original(owner) == Entity.Null || external.Contains(EntityManager, owner)) return false;
            if (!approaches.TryGetValue(owner, out var candidates))
            {
                candidates = new List<PreviewCrosswalkApproach>();
                if (EntityManager.HasComponent<Edge>(owner))
                {
                    var edge = EntityManager.GetComponentData<Edge>(owner);
                    AddApproach(candidates, owner, edge.m_Start);
                    AddApproach(candidates, owner, edge.m_End);
                }
                else if (EntityManager.HasComponent<Node>(owner))
                {
                    // Read only the preview node's connected roads. No city or
                    // neighbouring-intersection scan, and no original-curve equality.
                    if (EntityManager.HasBuffer<ConnectedEdge>(owner))
                        foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(owner, true))
                            AddApproach(candidates, edge.m_Edge, owner);
                    // Native EdgeIterator also visits unchanged connected roads
                    // on the original node. Hidden originals are excluded above,
                    // so proposed geometry takes precedence over replaced roads.
                    var originalNode = Original(owner);
                    if (EntityManager.HasBuffer<ConnectedEdge>(originalNode))
                        foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(originalNode, true))
                            AddApproach(candidates, edge.m_Edge, owner);
                }
                approaches[owner] = candidates;
            }
            var path = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
            float best = float.MaxValue;
            bool tied = false;
            foreach (var candidate in candidates)
            {
                if (!CrosswalkApproachPlacement.TryScore(candidate.Geometry, candidate.Start, path, out var score)) continue;
                if (CrosswalkPreviewChoicePolicy.Better(score, best))
                { best = score; road = candidate.Road; node = candidate.Node; tied = false; }
                else if (CrosswalkPreviewChoicePolicy.Tied(score, best)) tied = true;
            }
            return road != Entity.Null && !tied;
        }

        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            approaches.Clear();
            stale.Clear();
            foreach (var pair in bindings)
                if (!Alive(pair.Key) || !EntityManager.HasComponent<Temp>(pair.Key)
                    || !PreviewVisualPolicy.Active(EntityManager.GetComponentData<Temp>(pair.Key).m_Flags)) stale.Add(pair.Key);
            foreach (var lane in stale) Remove(lane);
            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            var paint = World.GetOrCreateSystemManaged<IntersectionPaintSystem>();
            using (var lanes = changes.ToEntityArray(Allocator.Temp))
                foreach (var lane in lanes)
                {
                    if (EntityManager.HasComponent<MGRCrosswalkRestoreRequest>(lane))
                    {
                        EntityManager.RemoveComponent<MGRCrosswalkRestoreRequest>(lane);
                        // This entity can also be a crossing; process it below
                        // only if it satisfies the temporary crossing contract.
                        if (!EntityManager.HasComponent<Temp>(lane) || !EntityManager.HasComponent<PedestrianLane>(lane)) continue;
                    }
                    if (EntityManager.HasComponent<MGRCrosswalkPreviewRequest>(lane)) EntityManager.RemoveComponent<MGRCrosswalkPreviewRequest>(lane);
                    if (!Alive(lane) || !PreviewVisualPolicy.Active(EntityManager.GetComponentData<Temp>(lane).m_Flags)
                        || (EntityManager.GetComponentData<PedestrianLane>(lane).m_Flags & PedestrianLaneFlags.Crosswalk) == 0
                        || external.Contains(EntityManager, lane)
                        || !Resolve(lane, choices, out var road, out var node, out var source))
                    { Remove(lane); continue; }
                    byte appearance = choices.CommittedAppearance(road, node);
                    // Legacy individually selected crossings retain their own
                    // appearance; IsSourceSelected honours explicit native intent.
                    if (appearance == 0 && Alive(source))
                        appearance = choices.IsSourceInvisible(source) ? (byte)2 : choices.IsSourceSelected(source) ? (byte)1 : (byte)0;
                    if (appearance == 0 && source == Entity.Null)
                    {
                        // Older saves can contain an individual curve choice
                        // without a road-end record. A single owned committed
                        // crossing on this exact approach can supply that choice.
                        // Multiple candidates remain unresolved, never guessed.
                        paint.ReadCommittedCrosswalkSources(road, node, legacySources, scratch);
                        if (legacySources.Count == 1)
                        {
                            source = legacySources[0];
                            appearance = choices.IsSourceInvisible(source) ? (byte)2 : choices.IsSourceSelected(source) ? (byte)1 : (byte)0;
                        }
                    }
                    if (appearance == 0) { Remove(lane); continue; }
                    var current = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                    var native = reverse.TryGetValue(current, out var original) ? original : current;
                    if (!EntityManager.HasComponent<NetLaneGeometryData>(native)) { Remove(lane); continue; }
                    if (!bindings.TryGetValue(lane, out var binding) || binding.Road != road || binding.Node != node)
                        binding = new Binding { Road = road, Node = node };
                    binding.Source = source;
                    if (appearance == 1)
                    {
                        bool dirty = binding.Dirty || binding.PaintSources.Count == 0;
                        foreach (var item in binding.PaintSources) dirty |= !Alive(item);
                        if (dirty) paint.ReadCommittedCrosswalkSources(road, node, binding.PaintSources, scratch);
                    }
                    else binding.PaintSources.Clear();
                    binding.Dirty = false;
                    bindings[lane] = binding;
                    aliases[lane] = native;
                    writes[lane] = new TemporaryPreviewWrite { Prefab = HiddenDefinition(native) };
                }
            retained.Clear();
            foreach (var binding in bindings.Values)
                foreach (var source in binding.PaintSources) if (Alive(source)) retained.Add(source);
            TemporaryPreviewWrites.Apply(World, writes);
            paint.RevealCommittedCrosswalkPaint(retained);
            World.GetExistingSystemManaged<CrosswalkVisualSystem>()?.FinishNetworkRefresh();
        }

        private Entity HiddenDefinition(Entity native)
        {
            if (definitions.TryGetValue(native, out var copy) && EntityManager.Exists(copy)) return copy;
            copy = EntityManager.Instantiate(native); // Definition only, never a routed lane/paint actor.
            EntityManager.AddComponent<MGRPrivateVisualPrefab>(copy);
            if (EntityManager.HasComponent<Created>(copy)) EntityManager.RemoveComponent<Created>(copy);
            if (EntityManager.HasComponent<Updated>(copy)) EntityManager.RemoveComponent<Updated>(copy);
            if (EntityManager.HasComponent<PrefabData>(copy)) EntityManager.SetComponentEnabled<PrefabData>(copy, false);
            EntityManager.SetComponentData(copy, CrosswalkVisualPolicy.WithoutPaint(EntityManager.GetComponentData<NetLaneGeometryData>(native)));
            definitions[native] = copy;
            reverse[copy] = native;
            return copy;
        }

        private void Remove(Entity lane)
        {
            bindings.Remove(lane);
            RestoreAlias(lane);
        }

        private void RestoreAlias(Entity lane)
        {
            if (!aliases.TryGetValue(lane, out var native)) return;
            if (Alive(lane) && EntityManager.HasComponent<Temp>(lane) && EntityManager.HasComponent<PrefabRef>(lane)
                && reverse.TryGetValue(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab, out var original) && original == native)
                writes[lane] = new TemporaryPreviewWrite { Prefab = native };
            aliases.Remove(lane);
        }

        internal void RestoreForRefresh()
        {
            // Restore native matching keys. Retention survives until the new
            // changed-lane batch replaces it, so no intermediate paint rebuild.
            stale.Clear(); stale.AddRange(aliases.Keys);
            foreach (var lane in stale) RestoreAlias(lane);
            TemporaryPreviewWrites.Apply(World, writes);
        }

        internal void Restore()
        {
            RestoreForRefresh();
            foreach (var lane in bindings.Keys)
                if (EntityManager.Exists(lane) && EntityManager.HasComponent<MGRCrosswalkPreviewRequest>(lane)) EntityManager.RemoveComponent<MGRCrosswalkPreviewRequest>(lane);
            bindings.Clear(); retained.Clear(); approaches.Clear(); external.Clear();
            using (var requests = restoreRequests.ToEntityArray(Allocator.Temp))
                foreach (var entity in requests) EntityManager.RemoveComponent<MGRCrosswalkRestoreRequest>(entity);
            foreach (var copy in definitions.Values)
                if (EntityManager.Exists(copy)) GeneratedVisualLifecycle.RetireDefinition(World, copy, reverse[copy]);
            definitions.Clear(); reverse.Clear();
        }

        public void PreSerialize(Context context) { Restore(); }
        protected override void OnGamePreload(Purpose purpose, GameMode mode) { Restore(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { Restore(); if (active == this) active = null; base.OnDestroy(); }
    }
}
