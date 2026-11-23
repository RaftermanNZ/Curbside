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
            internal ushort Mask, PedestrianMask;
            internal int Slot;
            internal bool Secondary;
            internal int Revision;
            internal Transform Generated;
        }

        private struct CandidateKey : System.IEquatable<CandidateKey>
        {
            internal Entity Role; internal int Slot;
            public bool Equals(CandidateKey other) => Role == other.Role && Slot == other.Slot;
            public override bool Equals(object other) => other is CandidateKey key && Equals(key);
            public override int GetHashCode() => unchecked(Role.GetHashCode() * 397 ^ Slot);
        }
        private struct CandidateRange { internal int Head, Tail; }
        private sealed class NodeCache
        {
            internal int Revision;
            internal readonly List<Candidate> Items = new List<Candidate>();
            private int primaryCount;
            private Dictionary<CandidateKey, CandidateRange> ranges;
            private int[] next;
            internal bool HasIndex => ranges != null;
            internal void Add(Candidate value) { Items.Add(value); if (!value.Secondary) primaryCount++; }
            internal void BuildIndex()
            {
                if (primaryCount <= 32) return;
                ranges = new Dictionary<CandidateKey, CandidateRange>(primaryCount);
                next = new int[Items.Count];
                for (int i = 0; i < Items.Count; i++)
                {
                    next[i] = -1;
                    var value = Items[i]; if (value.Secondary) continue;
                    var key = new CandidateKey { Role = value.Role, Slot = value.Slot };
                    if (ranges.TryGetValue(key, out var range)) { next[range.Tail] = i; range.Tail = i; }
                    else range = new CandidateRange { Head = i, Tail = i };
                    ranges[key] = range;
                }
            }
            internal int First(Entity role, int slot) => slot >= 0
                && ranges.TryGetValue(new CandidateKey { Role = role, Slot = slot }, out var range) ? range.Head : -1;
            internal int Next(int index) => next[index];
        }

        private struct Appearance
        {
            internal bool Valid;
            internal Entity Prefab;
            internal Transform Native, Applied;
            internal bool MasksRead, MasksValid;
            internal ushort NativeVehicleMask, NativePedestrianMask;
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

        internal bool HasRefreshWork => saved.Count != 0 || writes.Count != 0;

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
            {  }

        internal static void ApplySourceChange(World world, Entity source)
        {  }

        protected override void OnUpdate()
        { // Native-preview-only isolation test.
         }

        private void TrimNodeCaches()
        {
            // Overflow is bounded by the nodes actually visited in this pass.
            // Persistent storage and source-change callback walks remain <=32.
            if (nodes.Count > 32) nodes.Clear();
        }

        private int Slot(Entity actor) => EntityManager.Exists(actor) && EntityManager.HasComponent<Game.Objects.Aligned>(actor)
            ? EntityManager.GetComponentData<Game.Objects.Aligned>(actor).m_SubObjectIndex : -1;

        private ushort Mask(Entity actor) => EntityManager.Exists(actor) && EntityManager.HasComponent<Game.Objects.TrafficLight>(actor)
            ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(actor).m_GroupMask0 : (ushort)0;

        private bool SameFallbackBinding(FarSideLightSystem signals, Entity source, ushort vehicle, ushort pedestrian)
        {
            if (!ReadVisual(signals, source, out _, out _, out _)) return false;
            var appearance = appearances[source];
            // All preview candidates in this changed batch may inspect the same
            // source. Read its native binding once, alongside the existing
            // appearance snapshot. OnUpdate clears these snapshots each batch.
            if (!appearance.MasksRead)
            {
                appearance.MasksValid = signals.TryPreviewMasks(source,
                    out appearance.NativeVehicleMask, out appearance.NativePedestrianMask);
                appearance.MasksRead = true;
                appearances[source] = appearance;
            }
            return appearance.MasksValid && SignalPreviewIdentityPolicy.SameFallbackBinding(vehicle, pedestrian,
                appearance.NativeVehicleMask, appearance.NativePedestrianMask);
        }

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
            if (!nodes.TryGetValue(node, out var cache) || cache.Revision != NodeRevision(node)
                || firstVisit && EntityManager.HasComponent<Updated>(node))
            {
                // Keep each verified node snapshot through this native preview pass.
                cache = new NodeCache { Revision = NodeRevision(node) };
                foreach (var item in EntityManager.GetBuffer<Game.Objects.SubObject>(node, true))
                {
                    if (!ReadVisual(signals, item.m_SubObject, out var visual, out var native, out _)) continue;
                    ushort mask = EntityManager.HasComponent<Game.Objects.TrafficLight>(item.m_SubObject)
                        ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(item.m_SubObject).m_GroupMask0 : (ushort)0;
                    if (!EntityManager.HasComponent<Owner>(item.m_SubObject)
                        || EntityManager.GetComponentData<Owner>(item.m_SubObject).m_Owner != node) continue;
                    bool secondarySource = EntityManager.HasComponent<Game.Objects.Secondary>(item.m_SubObject);
                    cache.Add(new Candidate { Source = item.m_SubObject, Role = GardenCityLightPolicy.NativeSource(visual), Mask = mask, Slot = secondarySource ? -1 : Slot(item.m_SubObject),
                        Secondary = secondarySource, Native = native });
                }
                cache.BuildIndex();
                nodes[node] = cache;
                cacheBuilds++;
            }
            Entity result = Entity.Null;
            var role = GardenCityLightPolicy.NativeSource(prefab);
            bool secondary = EntityManager.HasComponent<Game.Objects.Secondary>(actor);
            int slot = secondary ? -1 : Slot(actor);
            float best = float.MaxValue;
            bool tied = false;
            var light = EntityManager.HasComponent<Game.Objects.TrafficLight>(actor)
                ? EntityManager.GetComponentData<Game.Objects.TrafficLight>(actor) : default;
            bool indexed = !secondary && cache.HasIndex;
            for (int index = indexed ? cache.First(role, slot) : 0;
                index >= 0 && index < cache.Items.Count; index = indexed ? cache.Next(index) : index + 1)
            {
                var candidate = cache.Items[index];
                if (candidate.Role != role || !EntityManager.Exists(candidate.Source) || EntityManager.HasComponent<Deleted>(candidate.Source)) continue;
                if (!Available(actor, candidate.Source)) continue;
                if (candidate.Secondary != secondary || EntityManager.HasComponent<Game.Objects.Secondary>(candidate.Source) != secondary
                    || !EntityManager.HasComponent<Owner>(candidate.Source)
                    || EntityManager.GetComponentData<Owner>(candidate.Source).m_Owner != node) continue;
                if (!SameFallbackBinding(signals, candidate.Source, light.m_GroupMask0, light.m_GroupMask1)) continue;
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
            saved.Clear(); savedOwners.Clear();
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
            if (saved.TryGetValue(actor, out var value)) { QueueRestore(actor, value); ForgetSaved(actor); }
        }

        internal void Restore()
        {
            RestoreForRefresh();
            foreach (var actor in bindings.Keys)
                if (EntityManager.Exists(actor) && EntityManager.HasComponent<MGRSignalPreviewRequest>(actor)) EntityManager.RemoveComponent<MGRSignalPreviewRequest>(actor);
            bindings.Clear(); bindingOwners.Clear(); nodeRevisions.Clear(); revisionBase = unchecked(++revisionSerial); nodes.Clear(); checkedNodes.Clear(); appearances.Clear(); ClearClaims(); claimPool.Clear();
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
        { // Native-preview-only isolation test.
         }
    }

    // Runs only for a native network preview generation/cancellation batch.
    // Restore definition keys before native subobject/lane matching, not every
    // presentation frame. No permanent road/light reference is changed here.
    public partial class PreviewRefreshRestoreSystem : GameSystemBase
    {
        private EntityQuery ownerChanges, temporaryObjectOwners, objectEvents;
        private EntityQuery objectReaders, laneReaders;
        private readonly PreviewRefreshScope scope = new PreviewRefreshScope();
        protected override void OnCreate()
        {
            base.OnCreate();
            // Read-only dependency gates: never complete an unfinished writer
            // just to inspect a cache. Unknown game profiles use native reads.
            if (MarkingChangeNotifications.Supported)
            {
                objectReaders = GetEntityQuery(ComponentType.ReadOnly<Game.Objects.SubObject>());
                laneReaders = GetEntityQuery(ComponentType.ReadOnly<Game.Net.SubLane>());
            }
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
            var desc = new List<EntityQueryDesc>();
            foreach (var changed in new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Areas.ChangedDistrict>() })
                desc.Add(new EntityQueryDesc { All = new[] { changed },
                    Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<Game.Objects.SubObject>(), ComponentType.ReadOnly<Game.Net.SubLane>() } });
            ownerChanges = GetEntityQuery(desc.ToArray());
            temporaryObjectOwners = GetEntityQuery(new EntityQueryDesc {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() } });
            objectEvents = GetEntityQuery(new EntityQueryDesc {
                All = new[] { ComponentType.ReadOnly<Event>() },
                Any = new[] { ComponentType.ReadOnly<Game.Objects.SubObjectsUpdated>(), ComponentType.ReadOnly<Game.Buildings.RentersUpdated>() } });
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            scope.ResetHistory(); scope.Clear(); base.OnGamePreload(purpose, mode);
        }

        protected override void OnUpdate()
        { // Native-preview-only isolation test.
         }
    }
}
