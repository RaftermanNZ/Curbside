using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Pathfind;
using Game.Serialization;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public struct MGRTemporaryPreviewPaint : IComponentData { }
    public struct MGRPreviewPaintRequest : IComponentData { }

    internal struct PreviewPaintStroke
    {
        internal Bezier4x3 Curve;
        internal Entity Prefab, Actor;
    }

    public partial class IntersectionPaintSystem
    {
        internal bool ReadPreviewPaint(Entity source, List<PreviewPaintStroke> output)
        {
            output.Clear();
            if (!Alive(source) || EntityManager.HasComponent<Temp>(source) || _externalPaint.Contains(EntityManager, source)) return false;
            if (EntityManager.HasComponent<MGRHiddenJunctionLine>(source)) return true;
            if (!_replacements.TryGetValue(source, out var replacement)) return false;
            foreach (var actor in replacement.Paint)
            {
                if (!Alive(actor) || !EntityManager.HasComponent<Curve>(actor) || !EntityManager.HasComponent<PrefabRef>(actor))
                { output.Clear(); return false; } // Keep native output if a replacement is not ready.
                output.Add(new PreviewPaintStroke
                {
                    Curve = EntityManager.GetComponentData<Curve>(actor).m_Bezier,
                    Prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab,
                    Actor = actor
                });
            }
            return true; // Zero strokes deliberately means a saved hidden source.
        }
    }

    // Work is limited to native temporary lanes changed by a modification batch.
    // Saved choices come from the exact original source; no nearby-road guessing,
    // permanent curve edits, or rerun of the road line planner is involved.
    public partial class PreviewPaintInheritanceSystem : GameSystemBase, IPreSerialize
    {
        private sealed class Entry
        {
            internal Entity Source, Prefab;
            internal Bezier4x3 Curve;
            internal bool UsingOriginal;
            internal readonly List<MarkingKey> Keys = new List<MarkingKey>();
            internal readonly List<Entity> Actors = new List<Entity>();
        }

        private EntityQuery changes;
        private struct SourceCandidate
        {
            internal Entity Actor, Prefab;
            internal Bezier4x3 Curve;
        }
        private readonly Dictionary<Entity, List<SourceCandidate>> ownerSources = new Dictionary<Entity, List<SourceCandidate>>();
        private static PreviewPaintInheritanceSystem active;
        private readonly Dictionary<Entity, Entity> sources = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, bool> identicalSources = new Dictionary<Entity, bool>();
        private readonly HashSet<Entity> retainedOriginals = new HashSet<Entity>();
        private readonly HashSet<Entity> work = new HashSet<Entity>();
        private readonly Dictionary<Entity, Entry> entries = new Dictionary<Entity, Entry>();
        private readonly Dictionary<Entity, Entity> aliases = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, Entity> copies = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, Entity> reverse = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, TemporaryPreviewWrite> writes = new Dictionary<Entity, TemporaryPreviewWrite>();
        private readonly List<PreviewPaintStroke> strokes = new List<PreviewPaintStroke>();
        private readonly List<PreviewPaintStroke> mapped = new List<PreviewPaintStroke>();
        private readonly List<Entity> stale = new List<Entity>();
        private readonly List<Entity> retired = new List<Entity>();
        private readonly HashSet<Entity> retiredActors = new HashSet<Entity>();
        private readonly HashSet<Entity> retiredOwners = new HashSet<Entity>();
        private readonly ExternalPaintOwnership external = new ExternalPaintOwnership();

        protected override void OnCreate()
        {
            base.OnCreate();
            active = this;
            changes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Owner>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<MGRPreviewPaintRequest>() },
                None = new[] { ComponentType.ReadOnly<MGRTemporaryPreviewPaint>(), ComponentType.ReadOnly<Game.Net.CarLane>(), ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.ReadOnly<Game.Net.TrackLane>(), ComponentType.ReadOnly<Game.Net.PedestrianLane>() }
            });
            RequireForUpdate(changes);
        }

        internal static void SourceChanged(World world, Entity source)
        {
            CrosswalkAppearancePreviewSystem.SourceChanged(world,source);
            if (active == null || active.World != world || Mod.Saving) return;
            foreach (var pair in active.sources)
                if (pair.Value == source && active.Alive(pair.Key) && active.EntityManager.HasComponent<Temp>(pair.Key)
                    && !active.EntityManager.HasComponent<MGRPreviewPaintRequest>(pair.Key))
                    active.EntityManager.AddComponent<MGRPreviewPaintRequest>(pair.Key);
        }

        internal static bool KeepsOriginal(World world, Entity source)
            => active != null && active.World == world && active.retainedOriginals.Contains(source)
                || CrosswalkAppearancePreviewSystem.KeepsOriginal(world,source);

        private bool Alive(Entity actor) => EntityManager.Exists(actor) && !EntityManager.HasComponent<Deleted>(actor);

        private Entity ResolveSource(Entity lane, Temp temp)
        {
            // A valid native identity is authoritative even when it is unowned.
            if (Alive(temp.m_Original)) return temp.m_Original;
            var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            var original = owner;
            for (int depth = 0; depth < 8 && Alive(original) && EntityManager.HasComponent<Temp>(original); depth++)
            {
                var link = EntityManager.GetComponentData<Temp>(original);
                if (!PreviewVisualPolicy.Active(link.m_Flags) || link.m_Original == original) return Entity.Null;
                original = link.m_Original;
            }
            if (!Alive(original) || original == owner || EntityManager.HasComponent<Temp>(original)
                || !EntityManager.HasBuffer<Game.Net.SubLane>(original) || external.Contains(EntityManager, original)) return Entity.Null;
            var visual = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>();
            var prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
            if (reverse.TryGetValue(prefab, out var native)) prefab = native;
            prefab = visual.Original(prefab);
            var path = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
            if (EntityManager.HasComponent<Curve>(owner) && EntityManager.HasComponent<Curve>(original))
            {
                var from = EntityManager.GetComponentData<Curve>(owner).m_Bezier;
                var to = EntityManager.GetComponentData<Curve>(original).m_Bezier;
                if (!PreviewPaintGeometry.Map(path, from, to, out path)) return Entity.Null;
            }
            if (!ownerSources.TryGetValue(original, out var candidates))
            {
                candidates = new List<SourceCandidate>();
                foreach (var item in EntityManager.GetBuffer<Game.Net.SubLane>(original, true))
                {
                    var source = item.m_SubLane;
                    if (!Alive(source) || EntityManager.HasComponent<Temp>(source) || !EntityManager.HasComponent<Owner>(source)
                        || EntityManager.GetComponentData<Owner>(source).m_Owner != original
                        || !EntityManager.HasComponent<Curve>(source) || !EntityManager.HasComponent<PrefabRef>(source)
                        || EntityManager.HasComponent<MGRCurbMarking>(source) || EntityManager.HasComponent<MGRIntersectionPaint>(source)
                        || EntityManager.HasComponent<Game.Net.CarLane>(source) || EntityManager.HasComponent<Game.Net.ParkingLane>(source)
                        || EntityManager.HasComponent<Game.Net.TrackLane>(source) || external.Contains(EntityManager, source)) continue;
                    candidates.Add(new SourceCandidate
                    {
                        Actor = source, Prefab = visual.Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab),
                        Curve = EntityManager.GetComponentData<Curve>(source).m_Bezier
                    });
                }
                ownerSources[original] = candidates;
            }
            var key = new MarkingKey(path, prefab);
            Entity result = Entity.Null;
            foreach (var candidate in candidates)
            {
                if (!key.Equals(new MarkingKey(candidate.Curve, candidate.Prefab))) continue;
                if (result != Entity.Null) return Entity.Null; // Ambiguous fork: never borrow another line's choice.
                result = candidate.Actor;
            }
            return result;
        }

        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            ownerSources.Clear();
            var replacements = World.GetOrCreateSystemManaged<IntersectionPaintSystem>();
            stale.Clear();
            foreach (var entry in sources)
                if (!Alive(entry.Key) || !EntityManager.HasComponent<Temp>(entry.Key)
                    || !PreviewVisualPolicy.Active(EntityManager.GetComponentData<Temp>(entry.Key).m_Flags)
                    || !EntityManager.HasComponent<Owner>(entry.Key)
                    || !Alive(EntityManager.GetComponentData<Owner>(entry.Key).m_Owner)) stale.Add(entry.Key);
            foreach (var lane in stale) { Remove(lane); sources.Remove(lane); }
            using (var lanes = changes.ToEntityArray(Allocator.Temp))
            {
                work.Clear();
                // Resolve the changed batch first, so duplicate native copies
                // cannot each retain the one original actor as their own view.
                foreach (var lane in lanes)
                {
                    work.Add(lane);
                    if (!Alive(lane) || !PreviewVisualPolicy.Active(EntityManager.GetComponentData<Temp>(lane).m_Flags)
                        || external.Contains(EntityManager, lane)) { sources.Remove(lane); continue; }
                    var source = ResolveSource(lane, EntityManager.GetComponentData<Temp>(lane));
                    if (source == Entity.Null) sources.Remove(lane); else sources[lane] = source;
                }
                identicalSources.Clear();
                foreach (var pair in sources)
                {
                    var source = pair.Value;
                    bool same = Alive(source) && EntityManager.HasComponent<Curve>(source)
                        && PreviewPaintGeometry.SamePath(EntityManager.GetComponentData<Curve>(source).m_Bezier,
                            EntityManager.GetComponentData<Curve>(pair.Key).m_Bezier);
                    identicalSources[source] = (!identicalSources.TryGetValue(source, out var priorSame) || priorSame) && same;
                }
                foreach (var pair in entries)
                    if (identicalSources.TryGetValue(pair.Value.Source, out var unchanged) && unchanged != pair.Value.UsingOriginal)
                        work.Add(pair.Key);
                foreach (var lane in work)
                {
                    if (!Alive(lane)) continue;
                    if (EntityManager.HasComponent<MGRPreviewPaintRequest>(lane)) EntityManager.RemoveComponent<MGRPreviewPaintRequest>(lane);
                    var temp = EntityManager.GetComponentData<Temp>(lane);
                    if (!PreviewVisualPolicy.Active(temp.m_Flags) || external.Contains(EntityManager, lane)) { Remove(lane); continue; }
                    var source = sources.TryGetValue(lane, out var resolved) ? resolved : Entity.Null;
                    if (!EntityManager.Exists(source) || !EntityManager.HasComponent<Curve>(source)) { Remove(lane); sources.Remove(lane); continue; }
                    sources[lane] = source;
                    if (!replacements.ReadPreviewPaint(source, strokes)) { Remove(lane); continue; }
                    var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                    if (!EntityManager.Exists(owner) || !EntityManager.HasComponent<Temp>(owner)) continue;
                    var original = EntityManager.GetComponentData<Curve>(source).m_Bezier;
                    var preview = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    bool useOriginal = identicalSources.TryGetValue(source, out var allUnchanged) && allUnchanged;
                    mapped.Clear();
                    bool valid = true;
                    foreach (var stroke in strokes)
                    {
                        if (!PreviewPaintGeometry.Map(stroke.Curve, original, preview, out var curve)
                            || !EntityManager.HasComponent<NetLaneArchetypeData>(stroke.Prefab)
                            || !EntityManager.GetComponentData<NetLaneArchetypeData>(stroke.Prefab).m_LaneArchetype.Valid)
                        { valid = false; break; }
                        useOriginal &= Alive(stroke.Actor);
                        mapped.Add(new PreviewPaintStroke { Curve = curve, Prefab = stroke.Prefab, Actor = stroke.Actor });
                    }
                    if (!valid) { Remove(lane); continue; }
                    var current = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                    var native = reverse.TryGetValue(current, out var restored) ? restored : current;
                    if (!EntityManager.HasComponent<NetLaneGeometryData>(native)) continue;
                    bool same = entries.TryGetValue(lane, out var prior) && prior.Source == source && prior.Prefab == native
                        && MarkingPreviewPolicy.SameCurve(prior.Curve, preview) && prior.Keys.Count == mapped.Count && prior.UsingOriginal == useOriginal;
                    for (int i = 0; same && i < mapped.Count; i++)
                        same = prior.Keys[i].Equals(new MarkingKey(mapped[i].Curve, mapped[i].Prefab)) && (useOriginal || Alive(prior.Actors[i]));
                    if (!same)
                    {
                        Remove(lane);
                        var next = new Entry { Source = source, Prefab = native, Curve = preview, UsingOriginal = useOriginal };
                        try
                        {
                            foreach (var stroke in mapped)
                            {
                                if (!useOriginal) next.Actors.Add(CreateActor(owner, stroke));
                                next.Keys.Add(new MarkingKey(stroke.Curve, stroke.Prefab));
                            }
                        }
                        catch
                        {
                            retired.AddRange(next.Actors);
                            FlushRetired();
                            throw;
                        }
                        entries[lane] = next;
                    }
                    var hidden = HiddenDefinition(native);
                    aliases[lane] = native;
                    writes[lane] = new TemporaryPreviewWrite { Prefab = hidden };
                }
            }
            retainedOriginals.Clear();
            foreach (var entry in entries.Values)
                if (entry.UsingOriginal && identicalSources.TryGetValue(entry.Source, out var unchanged) && unchanged) retainedOriginals.Add(entry.Source);
            FlushRetired();
            TemporaryPreviewWrites.Apply(World, writes);
        }

        private Entity HiddenDefinition(Entity native)
        {
            if (copies.TryGetValue(native, out var copy) && EntityManager.Exists(copy)) return copy;
            copy = EntityManager.Instantiate(native);
            EntityManager.AddComponent<MGRPrivateVisualPrefab>(copy);
            if (EntityManager.HasComponent<Created>(copy)) EntityManager.RemoveComponent<Created>(copy);
            if (EntityManager.HasComponent<Updated>(copy)) EntityManager.RemoveComponent<Updated>(copy);
            if (EntityManager.HasComponent<PrefabData>(copy)) EntityManager.SetComponentEnabled<PrefabData>(copy, false);
            EntityManager.SetComponentData(copy, CrosswalkVisualPolicy.WithoutPaint(EntityManager.GetComponentData<NetLaneGeometryData>(native)));
            copies[native] = copy;
            reverse[copy] = native;
            return copy;
        }

        private Entity CreateActor(Entity owner, PreviewPaintStroke stroke)
        {
            Entity actor;
            using (var created = new NativeArray<Entity>(1, Allocator.Temp))
            {
                EntityManager.CreateEntity(EntityManager.GetComponentData<NetLaneArchetypeData>(stroke.Prefab).m_LaneArchetype, created);
                actor = created[0];
            }
            try
            {
            EntityManager.SetComponentData(actor, new PrefabRef(stroke.Prefab));
            EntityManager.SetComponentData(actor, new Lane
            {
                m_StartNode = new PathNode(new PathNode(actor, (ushort)0), true),
                m_MiddleNode = new PathNode(new PathNode(actor, (ushort)1), true),
                m_EndNode = new PathNode(new PathNode(actor, (ushort)2), true)
            });
            EntityManager.SetComponentData(actor, new Curve { m_Bezier = stroke.Curve, m_Length = MathUtils.Length(stroke.Curve) });
            EntityManager.AddComponentData(actor, new Owner(owner));
            EntityManager.AddComponentData(actor, new Temp { m_Original = Entity.Null, m_Flags = TempFlags.Select });
            EntityManager.AddComponent<Elevation>(actor);
            EntityManager.AddComponent<MGRTemporaryPreviewPaint>(actor);
            if (EntityManager.HasComponent<Game.Net.SecondaryLane>(actor)) EntityManager.RemoveComponent<Game.Net.SecondaryLane>(actor);
            if (!EntityManager.HasComponent<Created>(actor)) EntityManager.AddComponent<Created>(actor);
            if (!EntityManager.HasComponent<Updated>(actor)) EntityManager.AddComponent<Updated>(actor);
            // Native search runs earlier in Modification5. Retain insertion
            // intent, without adding these temporary actors to the road index.
            EntityManager.AddComponent<MGRPendingVisualRegistration>(actor);
            return actor;
            }
            catch { retired.Add(actor); FlushRetired(); throw; }
        }

        private void FlushRetired()
        {
            if (retired.Count == 0) return;
            retiredActors.Clear(); retiredOwners.Clear();
            using (var actors = new NativeList<Entity>(retired.Count, Allocator.Temp))
            {
                foreach (var actor in retired)
                    if (EntityManager.Exists(actor) && retiredActors.Add(actor))
                    {
                        actors.Add(actor);
                        if (EntityManager.HasComponent<Owner>(actor)) retiredOwners.Add(EntityManager.GetComponentData<Owner>(actor).m_Owner);
                    }
                if (actors.Length != 0)
                {
                    // A second native modification pass or another mod can
                    // discover a fresh lane. Remove only our copied actors
                    // from those affected owners before deleting the actors.
                    foreach (var owner in retiredOwners)
                    {
                        if (!EntityManager.Exists(owner) || !EntityManager.HasBuffer<Game.Net.SubLane>(owner)) continue;
                        var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(owner);
                        for (int i = lanes.Length - 1; i >= 0; i--)
                            if (retiredActors.Contains(lanes[i].m_SubLane)) lanes.RemoveAt(i);
                    }
                    // ApplyNetSystem does not exclude Deleted entities. Remove
                    // Temp as well, so preview-only paint can never be committed.
                    EntityManager.RemoveComponent(actors.AsArray(), ComponentType.ReadWrite<Temp>());
                    GeneratedVisualLifecycle.RetireSaveBatch(World, actors.AsArray());
                }
            }
            retired.Clear();
            retiredActors.Clear(); retiredOwners.Clear();
        }

        private void Remove(Entity lane)
        {
            if (entries.TryGetValue(lane, out var entry))
            {
                retired.AddRange(entry.Actors);
                entries.Remove(lane);
            }
            if (aliases.TryGetValue(lane, out var native))
            {
                if (Alive(lane) && EntityManager.HasComponent<PrefabRef>(lane)
                    && reverse.TryGetValue(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab, out var original) && original == native)
                    writes[lane] = new TemporaryPreviewWrite { Prefab = native };
                aliases.Remove(lane);
            }
        }

        internal void RestoreForRefresh()
        {
            retainedOriginals.Clear();
            stale.Clear(); stale.AddRange(entries.Keys);
            foreach (var lane in stale) Remove(lane);
            FlushRetired();
            TemporaryPreviewWrites.Apply(World, writes);
        }

        internal void Restore()
        {
            RestoreForRefresh();
            foreach (var copy in copies.Values)
                if (EntityManager.Exists(copy)) GeneratedVisualLifecycle.RetireDefinition(World, copy, reverse[copy]);
            copies.Clear(); reverse.Clear(); aliases.Clear(); external.Clear();
            foreach (var lane in sources.Keys)
                if (EntityManager.Exists(lane) && EntityManager.HasComponent<MGRPreviewPaintRequest>(lane)) EntityManager.RemoveComponent<MGRPreviewPaintRequest>(lane);
            sources.Clear();
        }

        public void PreSerialize(Context context) { Restore(); }
        protected override void OnGamePreload(Purpose purpose, GameMode mode) { Restore(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { Restore(); if (active == this) active = null; base.OnDestroy(); }
    }
}
