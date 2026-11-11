using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Creation-time ancestry is needed even after an intermediate ECS copy is
    // destroyed. A child's geometry is copied; its save identity must still be
    // traced back to the original definition, not another disposable copy.
    internal sealed class VisualDefinitionOrigins
    {
        private readonly Dictionary<Entity, Entity> parents = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, int> children = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, string> producers = new Dictionary<Entity, string>();

        internal void Track(Entity copy, Entity original, string producer)
        {
            if (parents.ContainsKey(copy)) return; // First clone source is authoritative.
            parents.Add(copy, original);
            producers[copy] = producer;
            children.TryGetValue(original, out var count);
            children[original] = count + 1;
        }

        internal bool Contains(Entity copy) => parents.ContainsKey(copy);
        internal bool HasDependents(Entity copy) => children.ContainsKey(copy);
        internal string Producer(Entity copy) => producers.TryGetValue(copy, out var value) ? value : "retirement-only";

        internal bool Resolve(Entity copy, Func<Entity, bool> exists, out Entity original)
        {
            original = copy;
            // A finite graph can visit at most this many edges without cycling.
            // Missing intermediate entities are valid if their ancestry remains.
            int remaining = parents.Count + 1;
            while (parents.TryGetValue(original, out var parent))
            {
                if (--remaining == 0 || parent == original) return false;
                original = parent;
            }
            return original != Entity.Null && exists(original);
        }

        internal void ReleaseDestroyed(Entity copy, Func<Entity, bool> exists, Action<Entity> released = null)
        {
            int remaining = parents.Count + 1;
            while (remaining-- > 0 && !exists(copy) && !children.ContainsKey(copy)
                && parents.TryGetValue(copy, out var parent))
            {
                parents.Remove(copy); producers.Remove(copy);
                if (children.TryGetValue(parent, out var count))
                {
                    if (count == 1) { children.Remove(parent); released?.Invoke(parent); }
                    else children[parent] = count - 1;
                }
                copy = parent;
            }
        }

        internal void SweepDestroyed(Func<Entity, bool> exists, Action<Entity> released = null)
        {
            // Save boundary only, not a recurring world scan.
            foreach (var copy in new List<Entity>(parents.Keys)) ReleaseDestroyed(copy, exists, released);
        }

        internal void Clear() { parents.Clear(); children.Clear(); producers.Clear(); }
    }
}
