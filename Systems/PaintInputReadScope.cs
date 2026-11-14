using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Borrowed read-only views are valid only within one non-mutating phase.
    // Each consumed type joins its writers before its first view is acquired.
    // Nothing is reused across End/Begin, structural changes or job scheduling.
    internal sealed class PaintInputReadScope
    {
        internal static readonly bool Supported = typeof(Entity).Assembly.ManifestModule.ModuleVersionId
            == new Guid("bfb47e8c-9b6e-4acf-8c6d-c80a7bff1c41");
        private const int MaximumLocations = 4096;
        private const int MaximumChunksPerType = 64;
        private readonly Dictionary<Entity, EntityStorageInfo> locations = new Dictionary<Entity, EntityStorageInfo>();
        private readonly Dictionary<Type, Reader> components = new Dictionary<Type, Reader>();
        private readonly Dictionary<Type, Reader> buffers = new Dictionary<Type, Reader>();
        private readonly List<Reader> used = new List<Reader>();
        private bool active;

        private abstract class Reader { internal abstract void Reset(); }
        private sealed class Components<T> : Reader where T : unmanaged, IComponentData
        {
            private ComponentTypeHandle<T> handle;
            private bool ready, haveHandle;
            private readonly Dictionary<ulong, NativeArray<T>> chunks = new Dictionary<ulong, NativeArray<T>>();
            private readonly Dictionary<ulong, bool> present = new Dictionary<ulong, bool>();
            internal bool Has(EntityManager manager, EntityStorageInfo location)
            {
                if (!haveHandle) { handle = manager.GetComponentTypeHandle<T>(true); haveHandle = true; }
                ulong id = location.Chunk.SequenceNumber;
                if (present.TryGetValue(id, out bool value)) return value;
                value = location.Chunk.Has(ref handle);
                if (present.Count < MaximumChunksPerType) present.Add(id, value);
                return value;
            }
            internal T Read(EntityManager manager, EntityStorageInfo location)
            {
                if (!ready)
                {
                    manager.CompleteDependencyBeforeRO<T>();
                    handle = manager.GetComponentTypeHandle<T>(true); haveHandle = ready = true;
                }
                ulong id = location.Chunk.SequenceNumber;
                if (!chunks.TryGetValue(id, out var values))
                {
                    values = location.Chunk.GetNativeArray(ref handle);
                    if (chunks.Count < MaximumChunksPerType) chunks.Add(id, values);
                }
                return values[location.IndexInChunk];
            }
            internal override void Reset() { chunks.Clear(); present.Clear(); handle = default; haveHandle = ready = false; }
        }
        private sealed class Buffers<T> : Reader where T : unmanaged, IBufferElementData
        {
            private BufferTypeHandle<T> handle;
            private bool ready, haveHandle;
            private readonly Dictionary<ulong, BufferAccessor<T>> chunks = new Dictionary<ulong, BufferAccessor<T>>();
            private readonly Dictionary<ulong, bool> present = new Dictionary<ulong, bool>();
            internal bool Has(EntityManager manager, EntityStorageInfo location)
            {
                if (!haveHandle) { handle = manager.GetBufferTypeHandle<T>(true); haveHandle = true; }
                ulong id = location.Chunk.SequenceNumber;
                if (present.TryGetValue(id, out bool value)) return value;
                value = location.Chunk.Has(ref handle);
                if (present.Count < MaximumChunksPerType) present.Add(id, value);
                return value;
            }
            internal DynamicBuffer<T> Read(EntityManager manager, EntityStorageInfo location)
            {
                if (!ready)
                {
                    manager.CompleteDependencyBeforeRO<T>();
                    handle = manager.GetBufferTypeHandle<T>(true); haveHandle = ready = true;
                }
                ulong id = location.Chunk.SequenceNumber;
                if (!chunks.TryGetValue(id, out var values))
                {
                    values = location.Chunk.GetBufferAccessor(ref handle);
                    if (chunks.Count < MaximumChunksPerType) chunks.Add(id, values);
                }
                return values[location.IndexInChunk];
            }
            internal override void Reset() { chunks.Clear(); present.Clear(); handle = default; haveHandle = ready = false; }
        }

        internal void Begin() { End(); active = true; }
        internal void End()
        {
            active = false;
            foreach (var reader in used) reader.Reset();
            used.Clear(); usedSet.Clear(); locations.Clear();
        }

        private EntityStorageInfo Location(EntityManager manager, Entity entity)
        {
            if (locations.TryGetValue(entity, out var location)) return location;
            location = manager.GetStorageInfo(entity);
            if (locations.Count < MaximumLocations) locations.Add(entity, location);
            return location;
        }

        internal T Component<T>(EntityManager manager, Entity entity) where T : unmanaged, IComponentData
        {
            if (!active) return manager.GetComponentData<T>(entity);
            if (!components.TryGetValue(typeof(T), out var reader))
                components.Add(typeof(T), reader = new Components<T>());
            // A reader joins the used set exactly once per batch. No added
            // component types or system-wide dependencies are registered here.
            var typed = (Components<T>)reader;
            return ReadComponent(manager, entity, typed);
        }

        private T ReadComponent<T>(EntityManager manager, Entity entity, Components<T> reader) where T : unmanaged, IComponentData
        {
            if (usedSet.Add(reader)) used.Add(reader);
            return reader.Read(manager, Location(manager, entity));
        }

        private readonly HashSet<Reader> usedSet = new HashSet<Reader>();

        internal bool Exists(EntityManager manager, Entity entity)
            => active && locations.ContainsKey(entity) || manager.Exists(entity);

        internal bool HasComponent<T>(EntityManager manager, Entity entity) where T : unmanaged, IComponentData
        {
            if (!active) return manager.HasComponent<T>(entity);
            if (!Exists(manager, entity)) return false;
            if (!components.TryGetValue(typeof(T), out var reader)) components.Add(typeof(T), reader = new Components<T>());
            if (usedSet.Add(reader)) used.Add(reader);
            return ((Components<T>)reader).Has(manager, Location(manager, entity));
        }

        internal bool HasBuffer<T>(EntityManager manager, Entity entity) where T : unmanaged, IBufferElementData
        {
            if (!active) return manager.HasBuffer<T>(entity);
            if (!Exists(manager, entity)) return false;
            if (!buffers.TryGetValue(typeof(T), out var reader)) buffers.Add(typeof(T), reader = new Buffers<T>());
            if (usedSet.Add(reader)) used.Add(reader);
            return ((Buffers<T>)reader).Has(manager, Location(manager, entity));
        }

        internal DynamicBuffer<T> Buffer<T>(EntityManager manager, Entity entity) where T : unmanaged, IBufferElementData
        {
            if (!active) return manager.GetBuffer<T>(entity, true);
            if (!buffers.TryGetValue(typeof(T), out var reader))
                buffers.Add(typeof(T), reader = new Buffers<T>());
            if (usedSet.Add(reader)) used.Add(reader);
            return ((Buffers<T>)reader).Read(manager, Location(manager, entity));
        }
    }
}
