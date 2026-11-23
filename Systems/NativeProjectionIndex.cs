using System;
using System.Collections.Generic;

namespace MedianGrassReplacement.Systems
{
    // Batch-local role identity. Never choose an arbitrary member of a tie.
    internal sealed class NativeProjectionRoleIndex<TKey, TEntity>
        where TKey : struct, IEquatable<TKey>
        where TEntity : struct, IEquatable<TEntity>
    {
        private struct Entry { internal TEntity Entity; internal bool Ambiguous; }
        private readonly Dictionary<TKey, Entry> entries = new Dictionary<TKey, Entry>();
        internal void Clear() => entries.Clear();
        internal bool Contains(TKey key) => entries.ContainsKey(key);
        internal void Add(TKey key, TEntity entity)
        {
            if (entries.TryGetValue(key, out var old))
            {
                if (!old.Entity.Equals(entity)) { old.Ambiguous = true; entries[key] = old; }
            }
            else entries.Add(key, new Entry { Entity = entity });
        }
        internal bool TryUnique(TKey key, out TEntity entity)
        {
            if (entries.TryGetValue(key, out var entry) && !entry.Ambiguous)
            { entity = entry.Entity; return true; }
            entity = default; return false;
        }
    }

    // Active-preview dependency index, not a second index of committed choices.
    // Dictionary/set operations are expected O(1); CopyDependents costs O(A_s).
    internal sealed class NativeProjectionLeaseIndex<TEntity, TValue>
        where TEntity : struct, IEquatable<TEntity>
    {
        private struct Entry { internal TEntity Source; internal TValue Value; }
        private readonly Dictionary<TEntity, Entry> actors = new Dictionary<TEntity, Entry>();
        private readonly Dictionary<TEntity, HashSet<TEntity>> sources = new Dictionary<TEntity, HashSet<TEntity>>();
        private readonly Stack<HashSet<TEntity>> pool = new Stack<HashSet<TEntity>>();
        internal int Count => actors.Count;
        internal bool TryGet(TEntity actor, out TValue value)
        {
            if (actors.TryGetValue(actor, out var entry)) { value = entry.Value; return true; }
            value = default; return false;
        }
        internal void Set(TEntity actor, TEntity source, TValue value)
        {
            if (actors.TryGetValue(actor, out var old) && !old.Source.Equals(source)) Remove(actor);
            actors[actor] = new Entry { Source = source, Value = value };
            if (!sources.TryGetValue(source, out var dependents))
            {
                dependents = pool.Count == 0 ? new HashSet<TEntity>() : pool.Pop();
                sources.Add(source, dependents);
            }
            dependents.Add(actor);
        }
        internal void Remove(TEntity actor)
        {
            if (!actors.TryGetValue(actor, out var entry)) return;
            actors.Remove(actor);
            if (!sources.TryGetValue(entry.Source, out var dependents)) return;
            dependents.Remove(actor);
            if (dependents.Count == 0)
            {
                sources.Remove(entry.Source);
                if (pool.Count < 64) pool.Push(dependents);
            }
        }
        internal void CopyDependents(TEntity source, List<TEntity> output)
        {
            output.Clear();
            if (sources.TryGetValue(source, out var dependents)) output.AddRange(dependents);
        }
        internal void CopyActors(List<TEntity> output) { output.Clear(); output.AddRange(actors.Keys); }
        internal void Clear()
        {
            foreach (var dependents in sources.Values)
            { dependents.Clear(); if (pool.Count < 64) pool.Push(dependents); }
            sources.Clear(); actors.Clear();
        }
    }
}
