using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal enum ChangeConsumer
    {
        IntersectionRoad, IntersectionOwner, RoadDecals, Junction,
        Parking, Signals, HiddenDecals
    }

    // Runtime-only world service. Never serialized, registered as a prefab, or
    // updated on a timer. Native discovery stays in its established game phase.
    internal sealed class CurbsideChangePipeline
    {
        private static readonly ConditionalWeakTable<World, CurbsideChangePipeline> worlds
            = new ConditionalWeakTable<World, CurbsideChangePipeline>();
        private static readonly ConditionalWeakTable<World, CurbsideChangePipeline>.CreateValueCallback create = Create;
        private static CurbsideChangePipeline Create(World world) => new CurbsideChangePipeline();
        internal static CurbsideChangePipeline For(World world) => worlds.GetValue(world, create);
        internal static void Reset(World world)
        {
            CurbsideReadPhase.Reset(world);
            if (!worlds.TryGetValue(world, out var pipeline)) return;
            pipeline.authority?.Reset();
            foreach (var inbox in pipeline.inboxes) inbox.Clear();
        }

        private readonly HashSet<Entity>[] inboxes =
        {
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>(),
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>()
        };
        private PaintOwnerNotifications.SharedAuthority authority;
        private PrefabSystem catalog;
        private CurbDecalSystem decals;
        internal void RegisterDecals(CurbDecalSystem system) { decals = system; }

        internal void Connect(World world, PrefabSystem prefabs, PaintOwnerNotifications consumer, ExternalPaintOwnership external)
        {
            if (!OwnerInputEquality.Supported || !PaintInputReadScope.Supported) return;
            // A replacement prefab service is a new input interpretation. Do
            // not combine facts produced by different resolvers/catalogs.
            if (catalog != null && !ReferenceEquals(catalog, prefabs)) return;
            if (authority == null)
            {
                catalog = prefabs;
                authority = new PaintOwnerNotifications.SharedAuthority(world,
                    new PaintLaneReadPolicy(prefabs).IsNonPaint, new ExternalPaintOwnership());
            }
            consumer.AttachSharedAuthority(authority, external);
        }

        internal void Publish(ChangeConsumer consumer, Entity subject)
        {
            if (subject != Entity.Null) inboxes[(int)consumer].Add(subject);
        }
        internal void Clear(ChangeConsumer consumer) => inboxes[(int)consumer].Clear();

        internal void Drain(ChangeConsumer consumer, HashSet<Entity> destination)
        {
            var inbox = inboxes[(int)consumer];
            destination.UnionWith(inbox);
            inbox.Clear();
        }
        internal void CommittedRoad(Entity road)
        {
            Publish(ChangeConsumer.IntersectionRoad, road);
            decals?.NotifyMarkingRoad(road);
        }
        internal void CommittedOwner(Entity owner)
        {
            Publish(ChangeConsumer.IntersectionOwner, owner);
            Publish(ChangeConsumer.Junction, owner);
        }

        // Preview source notifications retain their immediate timing and exact
        // native identity/visibility handling. They do not publish committed work.
        internal static void PreviewPaint(World world, Entity source)
            => PreviewPaintInheritanceSystem.ApplySourceChange(world, source);
        internal static void PreviewSignal(World world, Entity source)
            => SignalPreviewSystem.ApplySourceChange(world, source);
        internal static void PreviewCrosswalk(World world, Entity source)
            => CrosswalkAppearancePreviewSystem.ApplySourceChange(world, source);
        internal static void PreviewDecal(Entity road)
            => DecalHoverPreviewSystem.ApplyRoadChange(road);
    }
}
