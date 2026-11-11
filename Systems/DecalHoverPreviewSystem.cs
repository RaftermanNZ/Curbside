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
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    public struct MGRDecalPreviewRequest : IComponentData { }

    internal static class DecalHoverPolicy
    {
        internal static bool SamePose(Transform a, Transform b)
            => math.all(math.isfinite(a.m_Position)) && math.all(math.isfinite(b.m_Position))
                && math.all(math.isfinite(a.m_Rotation.value)) && math.all(math.isfinite(b.m_Rotation.value))
                && math.abs(math.lengthsq(a.m_Rotation.value) - 1f) <= .0001f
                && math.abs(math.lengthsq(b.m_Rotation.value) - 1f) <= .0001f
                && math.distancesq(a.m_Position, b.m_Position) <= .000001f
                && math.abs(math.dot(a.m_Rotation.value, b.m_Rotation.value)) >= .999999f;

        internal static bool Hide(TempFlags flags, bool sameOwner, bool samePose, byte kind, byte hidden)
            => PreviewVisualPolicy.Active(flags) && sameOwner && samePose && kind != 0 && (kind & hidden) != 0;

        internal static int VisibilityDelta(bool alive, bool temporary, bool wantHidden, bool hidden, bool owned, bool parentHidden)
            => !alive || !temporary ? 0 : wantHidden ? (hidden ? 0 : 1) : owned && hidden && !parentHidden ? -1 : 0;
    }

    public partial class DecalVisibilitySystem
    {
        internal byte PreviewKind(Entity prefab) => Kind(prefab);

        internal bool ReadPreviewChoice(Entity source, out Entity road, out byte hidden)
        {
            road = Entity.Null; hidden = 0;
            if (!Live(source) || !EntityManager.HasComponent<Game.Objects.ObjectGeometry>(source)
                || !EntityManager.HasComponent<PrefabRef>(source)) return false;
            road = RoadFor(source);
            if (!Live(road)) return false;
            hidden = EntityManager.HasComponent<RDCUpgrade>(road) ? RoadSideUpgrades.HiddenDecals(EntityManager, road, source) : (byte)0;
            return true;
        }
    }

    // Native Created/Updated/Deleted batches only, after native object visibility.
    // No timer, camera/cursor polling, actor creation or renderer definition copy.
    public partial class DecalHoverPreviewSystem : GameSystemBase, IPreSerialize
    {
        private sealed class Binding
        {
            internal Entity Road, Owner;
            internal bool OwnsHidden;
        }
        private static DecalHoverPreviewSystem active;
        private EntityQuery changes;
        private readonly Dictionary<Entity, Binding> bindings = new Dictionary<Entity, Binding>();
        private readonly PreviewOwnerIndex owners = new PreviewOwnerIndex();
        internal PreviewOwnerIndex RefreshOwners => owners;
        private readonly List<Entity> stale = new List<Entity>();
        private readonly Dictionary<Entity, bool> ownerGeometry = new Dictionary<Entity, bool>();
        private readonly Dictionary<Entity, List<Entity>> candidates = new Dictionary<Entity, List<Entity>>();

        internal bool HasRefreshWork => bindings.Count != 0;

        protected override void OnCreate()
        {
            base.OnCreate(); active = this;
            changes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Game.Objects.ObjectGeometry>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<MGRDecalPreviewRequest>() }
            });
            RequireForUpdate(changes);
        }

        private bool Alive(Entity entity) => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);
        private Entity Original(Entity entity)
        {
            for (int i = 0; i < 8 && Alive(entity); i++)
            {
                if (!EntityManager.HasComponent<Temp>(entity)) return entity;
                var temp = EntityManager.GetComponentData<Temp>(entity);
                if (!PreviewVisualPolicy.Active(temp.m_Flags) || temp.m_Original == entity) return Entity.Null;
                entity = temp.m_Original;
            }
            return Entity.Null;
        }

        private bool SameEdge(Entity preview, Entity original)
        {
            if (!Alive(original) || !EntityManager.HasComponent<Edge>(preview) || !EntityManager.HasComponent<Edge>(original)
                || !EntityManager.HasComponent<Curve>(preview) || !EntityManager.HasComponent<Curve>(original)
                || !EntityManager.HasComponent<PrefabRef>(preview) || !EntityManager.HasComponent<PrefabRef>(original)) return false;
            return EntityManager.GetComponentData<PrefabRef>(preview).m_Prefab == EntityManager.GetComponentData<PrefabRef>(original).m_Prefab
                && PreviewPaintGeometry.SamePath(EntityManager.GetComponentData<Curve>(preview).m_Bezier, EntityManager.GetComponentData<Curve>(original).m_Bezier)
                && EntityManager.HasComponent<EdgeGeometry>(preview) == EntityManager.HasComponent<EdgeGeometry>(original)
                && (!EntityManager.HasComponent<EdgeGeometry>(preview)
                    || EntityManager.GetComponentData<EdgeGeometry>(preview).Equals(EntityManager.GetComponentData<EdgeGeometry>(original)));
        }

        private bool UnchangedOwner(Entity owner)
        {
            if (ownerGeometry.TryGetValue(owner, out var unchanged)) return unchanged;
            var original = Original(owner);
            unchanged = Alive(original);
            if (unchanged && owner != original)
            {
                if (EntityManager.HasComponent<Edge>(owner)) unchanged = SameEdge(owner, original);
                else if (EntityManager.HasComponent<Node>(owner) && EntityManager.HasComponent<Node>(original)
                    && EntityManager.HasBuffer<ConnectedEdge>(owner) && EntityManager.HasBuffer<ConnectedEdge>(original))
                {
                    unchanged = EntityManager.GetComponentData<Node>(owner).Equals(EntityManager.GetComponentData<Node>(original));
                    var expected = new HashSet<Entity>();
                    foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(original, true)) expected.Add(edge.m_Edge);
                    foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(owner, true))
                    {
                        var source = Original(edge.m_Edge);
                        if (!Alive(edge.m_Edge) || !expected.Remove(source) || !SameEdge(edge.m_Edge, source)) { unchanged = false; break; }
                    }
                    unchanged &= expected.Count == 0;
                }
                else unchanged = false; // Unproven attachment/geometry stays on the existing native path.
            }
            ownerGeometry[owner] = unchanged;
            return unchanged;
        }

        private bool SameObject(Entity actor, Entity source, Entity prefab, Transform pose, Entity owner)
            => Alive(source) && !EntityManager.HasComponent<Temp>(source)
                && EntityManager.HasComponent<PrefabRef>(source) && EntityManager.GetComponentData<PrefabRef>(source).m_Prefab == prefab
                && EntityManager.HasComponent<Owner>(source) && EntityManager.GetComponentData<Owner>(source).m_Owner == Original(owner)
                && EntityManager.HasComponent<Transform>(source) && DecalHoverPolicy.SamePose(pose, EntityManager.GetComponentData<Transform>(source));

        private Entity Source(Entity actor, Temp temp, Entity prefab, Transform pose, Entity owner)
        {
            // A real native link wins, even if it does not qualify. Never guess
            // a different source after an authoritative link fails validation.
            if (temp.m_Original != Entity.Null) return SameObject(actor, temp.m_Original, prefab, pose, owner) ? temp.m_Original : Entity.Null;
            var originalOwner = Original(owner);
            if (!Alive(originalOwner) || originalOwner == owner || !EntityManager.HasBuffer<Game.Objects.SubObject>(originalOwner)) return Entity.Null;
            if (!candidates.TryGetValue(originalOwner, out var list))
            {
                list = new List<Entity>();
                foreach (var child in EntityManager.GetBuffer<Game.Objects.SubObject>(originalOwner, true)) list.Add(child.m_SubObject);
                candidates[originalOwner] = list;
            }
            Entity match = Entity.Null;
            foreach (var candidate in list)
                if (SameObject(actor, candidate, prefab, pose, owner))
                { if (match != Entity.Null) return Entity.Null; match = candidate; }
            return match;
        }

        internal static void RoadChanged(Entity road)
        {
            var system = active;
            if (system == null || Mod.Saving) return;
            system.owners.Select(new[] { road }, system.stale);
            foreach (var actor in system.stale)
                if (system.Alive(actor) && system.EntityManager.HasComponent<Temp>(actor)
                    && !system.EntityManager.HasComponent<MGRDecalPreviewRequest>(actor)) system.EntityManager.AddComponent<MGRDecalPreviewRequest>(actor);
        }

        private bool Release(Entity actor, EntityCommandBuffer commands)
        {
            bool changed = false;
            if (EntityManager.Exists(actor) && EntityManager.HasComponent<MGRDecalPreviewRequest>(actor))
            { commands.RemoveComponent<MGRDecalPreviewRequest>(actor); changed = true; }
            if (!bindings.TryGetValue(actor, out var binding)) return changed;
            bindings.Remove(actor); owners.Remove(actor);
            bool parentHidden = EntityManager.HasComponent<Owner>(actor)
                && EntityManager.HasComponent<Hidden>(EntityManager.GetComponentData<Owner>(actor).m_Owner);
            if (DecalHoverPolicy.VisibilityDelta(Alive(actor), EntityManager.HasComponent<Temp>(actor), false,
                EntityManager.HasComponent<Hidden>(actor), binding.OwnsHidden, parentHidden) != -1) return changed;
            commands.RemoveComponent<Hidden>(actor);
            if (!EntityManager.HasComponent<BatchesUpdated>(actor)) commands.AddComponent<BatchesUpdated>(actor);
            return true;
        }

        private void Apply(EntityCommandBuffer commands, bool changed)
        { if (changed) { NativeVisualInvalidation.CompleteReaders(World); commands.Playback(EntityManager); } }

        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            ownerGeometry.Clear(); candidates.Clear(); stale.Clear();
            foreach (var pair in bindings)
                if (!Alive(pair.Key) || !EntityManager.HasComponent<Temp>(pair.Key)) stale.Add(pair.Key);
            var visibility = World.GetOrCreateSystemManaged<DecalVisibilitySystem>();
            using (var actors = changes.ToEntityArray(Allocator.Temp))
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            {
                bool changed = false;
                foreach (var actor in stale) changed |= Release(actor, commands);
                foreach (var actor in actors)
                {
                    if (EntityManager.HasComponent<MGRDecalPreviewRequest>(actor)) EntityManager.RemoveComponent<MGRDecalPreviewRequest>(actor);
                    var temp = EntityManager.GetComponentData<Temp>(actor);
                    var prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
                    var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
                    byte kind = Alive(actor) && PreviewVisualPolicy.Active(temp.m_Flags) ? visibility.PreviewKind(prefab) : (byte)0;
                    if (kind == 0 || !UnchangedOwner(owner)) { changed |= Release(actor, commands); continue; }
                    var pose = EntityManager.GetComponentData<Transform>(actor);
                    var source = Source(actor, temp, prefab, pose, owner);
                    if (source == Entity.Null || !visibility.ReadPreviewChoice(source, out var road, out byte hidden))
                    { changed |= Release(actor, commands); continue; }
                    bool hide = DecalHoverPolicy.Hide(temp.m_Flags, true, true, kind, hidden);
                    if (!hide) { changed |= Release(actor, commands); }
                    if (!bindings.TryGetValue(actor, out var binding)) binding = new Binding();
                    binding.Road = road; binding.Owner = owner;
                    if (DecalHoverPolicy.VisibilityDelta(true, true, hide, EntityManager.HasComponent<Hidden>(actor), binding.OwnsHidden, false) == 1)
                    {
                        commands.AddComponent<Hidden>(actor);
                        if (!EntityManager.HasComponent<BatchesUpdated>(actor)) commands.AddComponent<BatchesUpdated>(actor);
                        binding.OwnsHidden = true; changed = true;
                    }
                    bindings[actor] = binding; owners.Track(actor, owner, road, source);
                }
                Apply(commands, changed);
            }
        }

        internal void RestoreForOwners(PreviewRefreshScope scope)
        {
            if (!scope.Complete) { Restore(); return; }
            owners.Select(scope.Owners, stale);
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            { bool changed = false; foreach (var actor in stale) changed |= Release(actor, commands); Apply(commands, changed); }
        }

        internal void Restore()
        {
            stale.Clear(); stale.AddRange(bindings.Keys);
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            { bool changed = false; foreach (var actor in stale) changed |= Release(actor, commands); Apply(commands, changed); }
            ownerGeometry.Clear(); candidates.Clear(); owners.Clear();
        }
        public void PreSerialize(Context context) { Restore(); }
        protected override void OnGamePreload(Purpose purpose, GameMode mode) { Restore(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { Restore(); if (active == this) active = null; base.OnDestroy(); }
    }
}
