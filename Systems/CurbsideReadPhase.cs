using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Lookup acquisition records type access on a system. Keep that bookkeeping
    // off the feature systems so their native job dependencies do not broaden.
    // This service is created once per world and never scheduled for updates.
    [DisableAutoCreation]
    internal partial class CurbsideReadLookupSystem : SystemBase
    {
        protected override void OnUpdate() { }
        // The offline transform emits the exact closed readonly type set here.
        // Register it at bootstrap: adding a lookup type can complete all types
        // already registered on this host, and must not first happen on hover.
        internal void PrepareReadTypes() { }
    }
    // A read phase is bounded by one managed system update. No borrowed view
    // survives a mutation fence, nested update, job scheduling or callback exit.
    internal static class CurbsideReadPhase
    {
        // Set by the opt-in build transform; never a runtime polling switch.
        public static bool Enabled => false;
        [ThreadStatic] private static ConditionalWeakTable<World, Session> sessions;
        [ThreadStatic] private static Session current;
        [ThreadStatic] private static bool reusable;

        // Creating a system is structural work. Do it at mod/system bootstrap,
        // before feature code can borrow native buffers, never at hover entry.
        internal static void Initialize(World world)
        {
            if (!PaintInputReadScope.Supported || world == null || !world.IsCreated) return;
            if (sessions == null) sessions = new ConditionalWeakTable<World, Session>();
            if (!sessions.TryGetValue(world, out _)) sessions.Add(world, new Session(world));
        }

        private sealed class Session
        {
            internal readonly World World;
            internal readonly CurbsideReadLookupSystem Lookups;
            private readonly Dictionary<Type, Reader> readers = new Dictionary<Type, Reader>();
            private readonly List<Reader> used = new List<Reader>();
            internal bool Disabled;
            internal Session(World world) { World = world; Lookups = world.GetOrCreateSystemManaged<CurbsideReadLookupSystem>(); Lookups.PrepareReadTypes(); }
            internal void Begin(bool disabled = false) { End(); Disabled = disabled; }
            internal void End() { foreach (var reader in used) reader.Reset(); used.Clear(); }
            internal void Fence()
            {
                // A writing callback stays on native reads after its first
                // mutation. Rebuilding lookups after every write is a new tax.
                if (Disabled) return;
                End(); Disabled = true;
            }
            internal bool Matches(EntityManager manager) => !Disabled && World.IsCreated && ReferenceEquals(World, manager.World);
            internal R ReaderFor<R>() where R : Reader, new()
            {
                if (!readers.TryGetValue(typeof(R), out var reader)) readers.Add(typeof(R), reader = new R());
                used.Add(reader);
                return (R)reader;
            }
        }

        private abstract class Reader { internal abstract void Reset(); }
        private sealed class ComponentReader<T> : Reader where T : unmanaged, IComponentData
        {
            private static readonly bool zeroSized = TypeManager.GetTypeInfo<T>().IsZeroSized;
            [ThreadStatic] private static Session owner;
            [ThreadStatic] private static ComponentReader<T> active;
            private ComponentLookup<T> lookup;
            public ComponentReader() { }
            internal static T Read(Session phase, EntityManager manager, Entity entity)
            {
                if (!ReferenceEquals(owner, phase))
                {
                    // Lookup<T> returns default for a zero-sized tag, whereas
                    // EntityManager.GetComponentData preserves a native error.
                    // Missing first inputs must not introduce an earlier wait.
                    if (zeroSized || !manager.HasComponent<T>(entity)) return manager.GetComponentData<T>(entity);
                    manager.CompleteDependencyBeforeRO<T>();
                    var reader = phase.ReaderFor<ComponentReader<T>>();
                    reader.lookup = phase.Lookups.GetComponentLookup<T>(true);
                    owner = phase; active = reader;
                }
                return active.lookup[entity];
            }
            internal override void Reset() { lookup = default; if (ReferenceEquals(active, this)) { owner = null; active = null; } }
        }
        private sealed class BufferReader<T> : Reader where T : unmanaged, IBufferElementData
        {
            [ThreadStatic] private static Session owner;
            [ThreadStatic] private static BufferReader<T> active;
            private BufferLookup<T> lookup;
            public BufferReader() { }
            internal static DynamicBuffer<T> Read(Session phase, EntityManager manager, Entity entity)
            {
                if (!ReferenceEquals(owner, phase))
                {
                    if (!manager.HasBuffer<T>(entity)) return manager.GetBuffer<T>(entity, true);
                    manager.CompleteDependencyBeforeRO<T>();
                    var reader = phase.ReaderFor<BufferReader<T>>();
                    reader.lookup = phase.Lookups.GetBufferLookup<T>(true);
                    owner = phase; active = reader;
                }
                return active.lookup[entity];
            }
            internal override void Reset() { lookup = default; if (ReferenceEquals(active, this)) { owner = null; active = null; } }
        }

        public readonly struct Lease : IDisposable
        {
            private readonly Session entered, previous;
            private readonly bool previousDisabled;
            private Lease(Session entered, Session previous, bool previousDisabled)
            { this.entered = entered; this.previous = previous; this.previousDisabled = previousDisabled; }
            public void Dispose()
            {
                if (entered == null) return;
                entered.End();
                current = previous;
                previous?.Begin(previousDisabled);
                reusable = previous != null && !previousDisabled;
            }
            internal static Lease Open(World world)
            {
                if (!PaintInputReadScope.Supported || world == null || !world.IsCreated) return default;
                if (sessions == null || !sessions.TryGetValue(world, out var next)) return default;
                // Pure helper calls can nest inside the same readonly phase.
                // The outer lease owns its end; no reader rebuild at each leaf.
                if (ReferenceEquals(current, next) && reusable) return default;
                var prior = current;
                bool disabled = prior != null && prior.Disabled;
                prior?.End();
                next.Begin(); current = next; reusable = true;
                return new Lease(next, prior, disabled);
            }
        }

        // Entry is tolerant of late/destroyed callbacks; the original system
        // retains responsibility for its existing state/lifetime checks.
        public static Lease Enter(ComponentSystemBase system)
        {
            try { return Lease.Open(system.World); }
            catch (InvalidOperationException) { return default; }
        }
        public static Lease EnterManager(ref EntityManager manager)
        {
            try { return Lease.Open(manager.World); }
            catch (InvalidOperationException) { return default; }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BeforeMutation()
        {
            if (reusable) EndForMutation();
        }
        private static void EndForMutation() { reusable = false; current?.Fence(); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BorrowMutable() => BeforeMutation();
        internal static void Reset(World world)
        {
            if (current != null && ReferenceEquals(current.World, world)) { reusable = false; current.End(); current.Disabled = true; }
            if (sessions != null && sessions.TryGetValue(world, out var session)) session.End();
        }

        // Ref receivers match EntityManager's IL calling convention. The
        // original argument evaluation, exceptions and mutable buffer receiver
        // semantics remain intact; the build transform changes only call targets.
        public static T GetComponentData<T>(ref EntityManager manager, Entity entity) where T : unmanaged, IComponentData
        {
            if (!reusable) return manager.GetComponentData<T>(entity);
            var phase = current;
            if (phase != null && phase.Matches(manager))
                return ComponentReader<T>.Read(phase, manager, entity);
            return manager.GetComponentData<T>(entity);
        }
        public static DynamicBuffer<T> GetBuffer<T>(ref EntityManager manager, Entity entity, bool isReadOnly = false)
            where T : unmanaged, IBufferElementData
        {
            if (!isReadOnly) { BorrowMutable(); return manager.GetBuffer<T>(entity, false); }
            if (!reusable) return manager.GetBuffer<T>(entity, true);
            var phase = current;
            if (phase != null && phase.Matches(manager))
                return BufferReader<T>.Read(phase, manager, entity);
            return manager.GetBuffer<T>(entity, true);
        }
    }
}
