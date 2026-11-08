using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Serialization;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    public struct MGRSignalPreviewRequest : IComponentData { }

    public partial class SignalPreviewSystem : GameSystemBase, IPreSerialize
    {
        private struct Saved
        {
            internal Entity Original, Applied;
            internal Entity NativeOriginal, LinkedOriginal, Owner;
            internal bool Linked;
            internal Transform GeneratedPose, InheritedPose;
        }

        private struct Candidate
        {
            internal Entity Source, Role;
            internal ushort Mask;
            internal int Slot;
            internal bool Secondary;
            internal Transform Native;
        }

        private struct Binding
        {
            internal Entity Source, Node, Role;
            internal ushort Mask;
            internal int Slot;
            internal bool Secondary;
            internal int Revision;
            internal Transform Generated;
        }

        private sealed class NodeCache
        {
            internal int Revision;
            internal readonly List<Candidate> Items = new List<Candidate>();
        }

        private struct Appearance
        {
            internal bool Valid;
            internal Entity Prefab;
            internal Transform Native, Applied;
        }

        private EntityQuery changes;
        private static SignalPreviewSystem activeSystem;
        private readonly Dictionary<Entity, Saved> saved = new Dictionary<Entity, Saved>();
        private readonly Dictionary<Entity, Binding> bindings = new Dictionary<Entity, Binding>();
        private readonly Dictionary<Entity, NodeCache> nodes = new Dictionary<Entity, NodeCache>();
        private readonly Dictionary<Entity, Appearance> appearances = new Dictionary<Entity, Appearance>();
        private readonly Dictionary<Entity, TemporaryPreviewWrite> writes = new Dictionary<Entity, TemporaryPreviewWrite>();
        private readonly List<Entity> stale = new List<Entity>();
        private readonly HashSet<Entity> checkedNodes = new HashSet<Entity>();
        private readonly HashSet<Entity> changing = new HashSet<Entity>();
        private readonly Dictionary<Entity, HashSet<Entity>> claims = new Dictionary<Entity, HashSet<Entity>>();
        private readonly Stack<HashSet<Entity>> claimPool = new Stack<HashSet<Entity>>();
        private long passes, cacheBuilds, cacheHits, visualWrites;

        protected override void OnCreate()
        {
            base.OnCreate();
            activeSystem = this;
            changes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<MGRSignalPreviewRequest>() }
            });
            RequireForUpdate(changes);
        }

        internal static void SourceChanged(World world, Entity source)
        {
            var system = activeSystem;
            if (system == null || system.World != world || Mod.Saving) return;
            var manager = system.EntityManager;
            var node = manager.Exists(source) && manager.HasComponent<Owner>(source)
                ? manager.GetComponentData<Owner>(source).m_Owner : Entity.Null;
            var role = manager.Exists(source) && manager.HasComponent<PrefabRef>(source)
                ? GardenCityLightPolicy.NativeSource(manager.GetComponentData<PrefabRef>(source).m_Prefab) : Entity.Null;
            ushort mask = manager.Exists(source) && manager.HasComponent<Game.Objects.TrafficLight>(source)
                ? manager.GetComponentData<Game.Objects.TrafficLight>(source).m_GroupMask0 : (ushort)0;
            foreach (var pair in system.bindings)
                if ((pair.Value.Source == source || pair.Value.Source == Entity.Null && pair.Value.Node == node
                        && pair.Value.Role == role && SignalPreviewIdentityPolicy.CompatibleMask(pair.Value.Mask, mask))
                    && system.EntityManager.Exists(pair.Key)
                    && system.EntityManager.HasComponent<Temp>(pair.Key) && !system.EntityManager.HasComponent<Deleted>(pair.Key)
                    && !system.EntityManager.HasComponent<MGRSignalPreviewRequest>(pair.Key))
                    system.EntityManager.AddComponent<MGRSignalPreviewRequest>(pair.Key);
        }

        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            long started = ModDiagnostics.RuntimeCosts.Begin();
            try
            {
                passes++;
                appearances.Clear();
                checkedNodes.Clear();
                stale.Clear();
                foreach (var entry in bindings)
                    if (!EntityManager.Exists(entry.Key) || !EntityManager.HasComponent<Temp>(entry.Key)
                        || EntityManager.HasComponent<Deleted>(entry.Key)) stale.Add(entry.Key);
                RestoreRetiredIdentities(stale);
                foreach (var actor in stale) { bindings.Remove(actor); saved.Remove(actor); }
                var signals = World.GetOrCreateSystemManaged<FarSideLightSystem>();
                using (var actors = changes.ToEntityArray(Allocator.Temp))
                {
                    ClearClaims();
                    foreach (var actor in actors) changing.Add(actor);
                    // Native secondary matching consumes each original once per
                    // preview owner. Retain unchanged bindings and rebuild claims
                    // for the entire changed batch before choosing any matches.
                    foreach (var pair in bindings)
                        if (!changing.Contains(pair.Key) && EntityManager.Exists(pair.Key)
                            && !EntityManager.HasComponent<Deleted>(pair.Key) && EntityManager.HasComponent<Temp>(pair.Key)
                            && PreviewVisualPolicy.Active(EntityManager.GetComponentData<Temp>(pair.Key).m_Flags)) Claim(pair.Key, pair.Value.Source);
                    foreach (var actor in actors)
                    {
                        if (EntityManager.HasComponent<MGRSignalPreviewRequest>(actor)) EntityManager.RemoveComponent<MGRSignalPreviewRequest>(actor);
                        if (EntityManager.HasComponent<Deleted>(actor)) continue;
                        var temp = EntityManager.GetComponentData<Temp>(actor);
                        if (!PreviewVisualPolicy.Active(temp.m_Flags)) { RestoreActor(actor); bindings.Remove(actor); continue; }
                        var current = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
                        if (!EntityManager.HasComponent<TrafficLightData>(current) && !EntityManager.HasComponent<StreetLightData>(current)) continue;
                        ushort mask = EntityManager.HasComponent<Game.Objects.TrafficLight>(actor)
                            ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(actor).m_GroupMask0 : (ushort)0;
                        bool secondary = EntityManager.HasComponent<Game.Objects.Secondary>(actor);
                        int slot = secondary ? -1 : Slot(actor);
                        var node = FindNode(actor);
                        var role = GardenCityLightPolicy.NativeSource(current);
                        bool tracked = saved.TryGetValue(actor, out var prior);
                        var previewOwner = EntityManager.HasComponent<Owner>(actor) ? EntityManager.GetComponentData<Owner>(actor).m_Owner : Entity.Null;
                        if (tracked && prior.Owner != previewOwner)
                        {
                            // Return our temporary fields before handing an
                            // actor to a different owner. Its new owner/pose
                            // remains authoritative; do not carry an old match.
                            RestoreActor(actor); bindings.Remove(actor); continue;
                        }
                        var generated = EntityManager.GetComponentData<Transform>(actor);
                        if (tracked && prior.Linked && temp.m_Original == prior.LinkedOriginal
                            && generated.Equals(prior.InheritedPose)) generated = prior.GeneratedPose;
                        var nativeOriginal = tracked && prior.Linked && temp.m_Original == prior.LinkedOriginal
                            ? prior.NativeOriginal : temp.m_Original;
                        Entity source = temp.m_Original;
                        if (!ReadVisual(signals, source, out _, out _, out _))
                        {
                            // Native identity and custom appearance can differ
                            // after a placed secondary's prefab was replaced.
                            // Only accept a fallback inside the same verified
                            // original owner/type/role. Its link is preview-only
                            // and the exact native identity is restored on apply.
                            if (node == Entity.Null || !AllowsAppearanceFallback(source, node, role, secondary))
                            { RestoreActor(actor); bindings.Remove(actor); continue; }
                            if (bindings.TryGetValue(actor, out var bound) && bound.Mask == mask && bound.Role == role
                                && bound.Node == node && bound.Secondary == secondary && bound.Revision == signals.PreviewRevision
                                && bound.Generated.Equals(generated) && (secondary || bound.Slot == slot && SignalPreviewIdentityPolicy.SameSlot(slot, Slot(bound.Source)))
                                && EntityManager.HasComponent<Owner>(bound.Source)
                                && EntityManager.GetComponentData<Owner>(bound.Source).m_Owner == bound.Node
                                && EntityManager.HasComponent<Game.Objects.Secondary>(bound.Source) == secondary
                                && Available(actor, bound.Source)
                                && SignalPreviewIdentityPolicy.CompatibleMask(mask, Mask(bound.Source))
                                && ReadVisual(signals, bound.Source, out _, out _, out _))
                            { source = bound.Source; cacheHits++; }
                            else source = FindSource(actor, current, generated, signals);
                        }
                        if (!ReadVisual(signals, source, out var visual, out var native, out var inherited)
                            || secondary && !PrefersCustomSource(nativeOriginal, source, generated, native))
                        {
                            RestoreActor(actor);
                            // A source may finish registration after this batch.
                            // A real source-change callback can retry this owner;
                            // no periodic check or city-wide search is needed.
                            bindings[actor] = new Binding { Source = Entity.Null, Node = node, Role = role, Mask = mask, Slot = slot,
                                Secondary = secondary, Revision = signals.PreviewRevision, Generated = generated };
                            continue;
                        }
                        bindings[actor] = new Binding { Source = source, Node = node, Role = role, Mask = mask, Slot = slot,
                            Secondary = secondary, Revision = signals.PreviewRevision, Generated = generated };
                        Claim(actor, source);
                        bool link = temp.m_Original != source;
                        if (PreviewVisualPolicy.Delta(current, default, visual, default, false) == 0 && !link) continue;
                        if (!TemporaryPreviewWrites.Ready(EntityManager, actor, visual)) continue;
                        saved[actor] = new Saved
                        {
                            Original = tracked && current == prior.Applied ? prior.Original : current,
                            Applied = visual,
                            NativeOriginal = nativeOriginal,
                            LinkedOriginal = source, Linked = link || tracked && prior.Linked,
                            GeneratedPose = generated,
                            InheritedPose = inherited,
                            Owner = previewOwner
                        };
                        // Native ObjectInterpolateSystem handles secondary road
                        // objects; RelativeObjectSystem handles attached objects.
                        // Both consume original identity. No preview pose repair.
                        writes[actor] = new TemporaryPreviewWrite { Prefab = visual, HasOriginal = link, Original = source };
                    }
                }
                visualWrites += TemporaryPreviewWrites.Apply(World, writes);
            }
            finally { ClearClaims(); ModDiagnostics.RuntimeCosts.End("SignalPreviewSystem", started); }
        }

        private int Slot(Entity actor) => EntityManager.Exists(actor) && EntityManager.HasComponent<Game.Objects.Aligned>(actor)
            ? EntityManager.GetComponentData<Game.Objects.Aligned>(actor).m_SubObjectIndex : -1;

        private ushort Mask(Entity actor) => EntityManager.Exists(actor) && EntityManager.HasComponent<Game.Objects.TrafficLight>(actor)
            ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(actor).m_GroupMask0 : (ushort)0;

        private bool Available(Entity actor, Entity source)
        {
            if (!EntityManager.HasComponent<Owner>(actor)) return false;
            var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            return !claims.TryGetValue(owner, out var sources) || !sources.Contains(source);
        }

        private void Claim(Entity actor, Entity source)
        {
            if (source == Entity.Null || !EntityManager.HasComponent<Owner>(actor)) return;
            var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            if (!claims.TryGetValue(owner, out var sources)) claims[owner] = sources = claimPool.Count != 0 ? claimPool.Pop() : new HashSet<Entity>();
            sources.Add(source);
        }

        private void ClearClaims()
        {
            foreach (var sources in claims.Values)
            {
                sources.Clear();
                if (claimPool.Count < 64) claimPool.Push(sources);
            }
            claims.Clear(); changing.Clear();
        }

        private bool AllowsAppearanceFallback(Entity original, Entity node, Entity role, bool secondary)
        {
            if (EntityManager.HasComponent<Temp>(node) || EntityManager.HasComponent<Deleted>(node)) return false;
            if (!EntityManager.Exists(original) || EntityManager.HasComponent<Deleted>(original)) return true;
            if (!secondary) return false; // This live-original recovery belongs to the secondary path.
            return EntityManager.HasComponent<Owner>(original)
                && EntityManager.GetComponentData<Owner>(original).m_Owner == node
                && !EntityManager.HasComponent<Temp>(original)
                && EntityManager.HasComponent<Game.Objects.Secondary>(original) == secondary
                && EntityManager.HasComponent<PrefabRef>(original)
                && EntityManager.GetComponentData<PrefabRef>(original).m_Prefab == role;
        }

        private bool PrefersCustomSource(Entity original, Entity source, Transform generated, Transform native)
        {
            if (original == source || !EntityManager.Exists(original) || EntityManager.HasComponent<Deleted>(original)) return true;
            if (!EntityManager.HasComponent<Transform>(original)) return false;
            var originalPose = EntityManager.GetComponentData<Transform>(original);
            if (!SignalPreviewIdentityPolicy.TrySecondaryScore(generated, native, out var score)) return false;
            float originalScore = math.distancesq(generated.m_Position, originalPose.m_Position);
            // An uncustomized native original is evidence too. Keep its identity
            // and appearance when it is already the better/equally good match.
            return math.isfinite(originalScore) && SignalPreviewIdentityPolicy.Better(score, originalScore);
        }

        private bool ReadVisual(FarSideLightSystem signals, Entity source, out Entity prefab, out Transform native, out Transform applied)
        {
            // One source read per changed batch; never cache across a committed
            // style/position change or borrow the proposed cursor transform.
            if (!appearances.TryGetValue(source, out var value))
            {
                value.Valid = signals.TryPreviewVisual(source, out value.Prefab, out value.Native, out value.Applied);
                appearances[source] = value;
            }
            prefab = value.Prefab; native = value.Native; applied = value.Applied;
            return value.Valid;
        }

        private Entity FindNode(Entity actor)
        {
            var at = actor;
            for (int i = 0; i < 8 && EntityManager.Exists(at); i++)
            {
                if (!EntityManager.HasComponent<Temp>(at) && !EntityManager.HasComponent<Deleted>(at)
                    && EntityManager.HasComponent<MGRFarSideLights>(at)) return at;
                if (EntityManager.HasComponent<Temp>(at))
                {
                    var original = EntityManager.GetComponentData<Temp>(at).m_Original;
                    if (EntityManager.Exists(original) && !EntityManager.HasComponent<Temp>(original)
                        && !EntityManager.HasComponent<Deleted>(original) && EntityManager.HasComponent<MGRFarSideLights>(original)) return original;
                }
                if (!EntityManager.HasComponent<Owner>(at)) break;
                var next = EntityManager.GetComponentData<Owner>(at).m_Owner;
                if (next == at) break;
                at = next;
            }
            return Entity.Null;
        }

        private Entity FindSource(Entity actor, Entity prefab, Transform generated, FarSideLightSystem signals)
        {
            var node = FindNode(actor);
            if (node == Entity.Null || !EntityManager.HasBuffer<Game.Objects.SubObject>(node)) return Entity.Null;
            bool firstVisit = checkedNodes.Add(node);
            if (!nodes.TryGetValue(node, out var cache) || cache.Revision != signals.PreviewRevision
                || firstVisit && EntityManager.HasComponent<Updated>(node))
            {
                if (nodes.Count >= 32 && !nodes.ContainsKey(node)) nodes.Clear();
                cache = new NodeCache { Revision = signals.PreviewRevision };
                foreach (var item in EntityManager.GetBuffer<Game.Objects.SubObject>(node, true))
                {
                    if (!ReadVisual(signals, item.m_SubObject, out var visual, out var native, out _)) continue;
                    ushort mask = EntityManager.HasComponent<Game.Objects.TrafficLight>(item.m_SubObject)
                        ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(item.m_SubObject).m_GroupMask0 : (ushort)0;
                    if (!EntityManager.HasComponent<Owner>(item.m_SubObject)
                        || EntityManager.GetComponentData<Owner>(item.m_SubObject).m_Owner != node) continue;
                    bool secondarySource = EntityManager.HasComponent<Game.Objects.Secondary>(item.m_SubObject);
                    cache.Items.Add(new Candidate { Source = item.m_SubObject, Role = GardenCityLightPolicy.NativeSource(visual), Mask = mask, Slot = secondarySource ? -1 : Slot(item.m_SubObject),
                        Secondary = secondarySource, Native = native });
                }
                nodes[node] = cache;
                cacheBuilds++;
            }
            Entity result = Entity.Null;
            var role = GardenCityLightPolicy.NativeSource(prefab);
            bool secondary = EntityManager.HasComponent<Game.Objects.Secondary>(actor);
            int slot = secondary ? -1 : Slot(actor);
            float best = float.MaxValue;
            bool tied = false;
            ushort requested = EntityManager.HasComponent<Game.Objects.TrafficLight>(actor)
                ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(actor).m_GroupMask0 : (ushort)0;
            foreach (var candidate in cache.Items)
            {
                if (candidate.Role != role || !EntityManager.Exists(candidate.Source) || EntityManager.HasComponent<Deleted>(candidate.Source)) continue;
                if (!Available(actor, candidate.Source)) continue;
                if (candidate.Secondary != secondary || EntityManager.HasComponent<Game.Objects.Secondary>(candidate.Source) != secondary
                    || !EntityManager.HasComponent<Owner>(candidate.Source)
                    || EntityManager.GetComponentData<Owner>(candidate.Source).m_Owner != node) continue;
                ushort currentMask = EntityManager.HasComponent<Game.Objects.TrafficLight>(candidate.Source)
                    ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(candidate.Source).m_GroupMask0 : (ushort)0;
                if (!SignalPreviewIdentityPolicy.CompatibleMask(requested, currentMask)) continue;
                if (secondary)
                {
                    if (!SignalPreviewIdentityPolicy.TrySecondaryScore(generated, candidate.Native, out var score)) continue;
                    if (SignalPreviewIdentityPolicy.Better(score, best)) { best = score; result = candidate.Source; tied = false; }
                    else if (SignalPreviewIdentityPolicy.Tied(score, best)) tied = true;
                }
                else
                {
                    if (!SignalPreviewIdentityPolicy.SameSlot(slot, candidate.Slot) || Slot(candidate.Source) != slot) continue;
                    if (result != Entity.Null) return Entity.Null;
                    result = candidate.Source;
                }
            }
            return tied ? Entity.Null : result;
        }

        internal void RestoreForRefresh()
        {
            stale.Clear();
            foreach (var actor in saved.Keys)
                if (EntityManager.Exists(actor) && EntityManager.HasComponent<Deleted>(actor)) stale.Add(actor);
            RestoreRetiredIdentities(stale);
            foreach (var pair in saved)
                QueueRestore(pair.Key, pair.Value);
            visualWrites += TemporaryPreviewWrites.Apply(World, writes);
            saved.Clear();
        }

        private void RestoreRetiredIdentities(List<Entity> actors)
        {
            if (actors.Count == 0) return;
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            {
                bool changed = false;
                foreach (var actor in actors)
                {
                    if (!saved.TryGetValue(actor, out var value) || !value.Linked || !EntityManager.Exists(actor)
                        || !EntityManager.HasComponent<Deleted>(actor) || !EntityManager.HasComponent<Temp>(actor)) continue;
                    var temp = EntityManager.GetComponentData<Temp>(actor);
                    if (temp.m_Original != value.LinkedOriginal) continue;
                    // Deleted actors must never reach native apply carrying our
                    // render-only link. No prefab, pose or validity flag changes.
                    commands.SetComponent(actor, SignalPreviewIdentityPolicy.WithOriginal(temp, value.NativeOriginal));
                    changed = true;
                }
                if (!changed) return;
                NativeVisualInvalidation.CompleteReaders(World);
                commands.Playback(EntityManager);
            }
        }

        private void QueueRestore(Entity actor, Saved value)
        {
            if (!EntityManager.Exists(actor) || EntityManager.HasComponent<Deleted>(actor)
                || !EntityManager.HasComponent<Temp>(actor) || !EntityManager.HasComponent<PrefabRef>(actor)) return;
            var current = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
            var temp = EntityManager.GetComponentData<Temp>(actor);
            var owner = EntityManager.HasComponent<Owner>(actor) ? EntityManager.GetComponentData<Owner>(actor).m_Owner : Entity.Null;
            bool identityOwned = value.Linked && temp.m_Original == value.LinkedOriginal;
            bool prefabOwned = current == value.Applied;
            if (!prefabOwned && !identityOwned) return;
            // Native rendering can copy the committed pose into Transform too.
            // Undo only that derived copy when returning the tool's original
            // identity/geometry inputs, so a custom far-side pose is never baked
            // into a newly committed native light or used as the next native key.
            bool inheritedPoseOwned = identityOwned && prefabOwned && owner == value.Owner
                && SignalPreviewIdentityPolicy.CopiesOriginalPose(EntityManager.HasComponent<Game.Objects.Static>(actor), true, temp.m_Flags)
                && EntityManager.HasComponent<Transform>(actor)
                && EntityManager.GetComponentData<Transform>(actor).Equals(value.InheritedPose);
            writes[actor] = new TemporaryPreviewWrite
            {
                Prefab = prefabOwned ? value.Original : current,
                HasOriginal = identityOwned, Original = value.NativeOriginal,
                HasPose = inheritedPoseOwned, Pose = value.GeneratedPose
            };
        }

        private void RestoreActor(Entity actor)
        {
            if (saved.TryGetValue(actor, out var value)) { QueueRestore(actor, value); saved.Remove(actor); }
        }

        internal void Restore()
        {
            RestoreForRefresh();
            foreach (var actor in bindings.Keys)
                if (EntityManager.Exists(actor) && EntityManager.HasComponent<MGRSignalPreviewRequest>(actor)) EntityManager.RemoveComponent<MGRSignalPreviewRequest>(actor);
            bindings.Clear(); nodes.Clear(); checkedNodes.Clear(); appearances.Clear(); ClearClaims(); claimPool.Clear();
        }

        internal void RecordSnapshot()
        {
            Mod.Log.Info("Signal preview changes: passes=" + passes + " sourceCacheBuilds=" + cacheBuilds
                + " bindingCacheHits=" + cacheHits + " changedVisualWrites=" + visualWrites
                + "; all tools, only Created/Updated/Deleted temporary actors; no timer refresh.");
        }

        public void PreSerialize(Context context) { Restore(); }
        protected override void OnGamePreload(Purpose purpose, GameMode mode) { Restore(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { Restore(); if (activeSystem == this) activeSystem = null; base.OnDestroy(); }
    }

    public partial class SignalPreviewRestoreSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystemManaged<SignalPreviewSystem>()?.Restore();
            World.GetExistingSystemManaged<PreviewPaintInheritanceSystem>()?.Restore();
            World.GetExistingSystemManaged<CrosswalkAppearancePreviewSystem>()?.Restore();
            World.GetExistingSystemManaged<CrosswalkVisualSystem>()?.RestorePreviewReferences();
            World.GetExistingSystemManaged<VisualLifecycleSystem>()?.FlushPending();
        }
    }

    // Runs only for a native network preview generation/cancellation batch.
    // Restore definition keys before native subobject/lane matching, not every
    // presentation frame. No permanent road/light reference is changed here.
    public partial class PreviewRefreshRestoreSystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            var networkChanges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Updated>() },
                Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>() }
            }, new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Created>() },
                Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>() }
            }, new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
                Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>() }
            });
            RequireForUpdate(networkChanges);
        }

        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            World.GetExistingSystemManaged<SignalPreviewSystem>()?.RestoreForRefresh();
            World.GetExistingSystemManaged<PreviewPaintInheritanceSystem>()?.RestoreForRefresh();
            World.GetExistingSystemManaged<CrosswalkAppearancePreviewSystem>()?.RestoreForRefresh();
        }
    }
}
