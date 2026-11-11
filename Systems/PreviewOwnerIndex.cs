using System.Collections.Generic;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Managed value index only. It holds no native buffers, query results or
    // renderer data across a native modification boundary.
    internal sealed class PreviewOwnerIndex
    {
        private readonly Dictionary<Entity, HashSet<Entity>> actors = new Dictionary<Entity, HashSet<Entity>>();
        private readonly Dictionary<Entity, HashSet<Entity>> owners = new Dictionary<Entity, HashSet<Entity>>();
        private readonly HashSet<Entity> selected = new HashSet<Entity>();

        // Matches the two ways Select can find an actor: a tracked actor key,
        // or a native/source owner key. No native state is retained here.
        internal bool MaySelect(Entity key) => owners.ContainsKey(key) || actors.ContainsKey(key);

        internal void Track(Entity actor, Entity a, Entity b = default, Entity c = default, Entity d = default)
        {
            if (owners.TryGetValue(actor, out var keys))
            {
                int count = a != Entity.Null ? 1 : 0;
                if (b != Entity.Null && b != a) count++;
                if (c != Entity.Null && c != a && c != b) count++;
                if (d != Entity.Null && d != a && d != b && d != c) count++;
                if (keys.Count == count && (a == Entity.Null || keys.Contains(a))
                    && (b == Entity.Null || keys.Contains(b)) && (c == Entity.Null || keys.Contains(c))
                    && (d == Entity.Null || keys.Contains(d))) return;
            }
            Remove(actor);
            Add(actor, a); Add(actor, b); Add(actor, c); Add(actor, d);
        }

        private void Add(Entity actor, Entity owner)
        {
            if (owner == Entity.Null) return;
            if (!actors.TryGetValue(owner, out var values)) actors.Add(owner, values = new HashSet<Entity>());
            values.Add(actor);
            if (!owners.TryGetValue(actor, out var keys)) owners.Add(actor, keys = new HashSet<Entity>());
            keys.Add(owner);
        }

        internal void Remove(Entity actor)
        {
            if (!owners.TryGetValue(actor, out var keys)) return;
            foreach (var key in keys)
                if (actors.TryGetValue(key, out var values))
                {
                    values.Remove(actor);
                    if (values.Count == 0) actors.Remove(key);
                }
            owners.Remove(actor);
        }

        internal void Select(IEnumerable<Entity> keys, List<Entity> output)
        {
            output.Clear(); selected.Clear();
            foreach (var key in keys)
            {
                // Native owner buffers are authoritative if another system
                // reparented an actor since the managed index was last updated.
                if (owners.ContainsKey(key) && selected.Add(key)) output.Add(key);
                if (actors.TryGetValue(key, out var values))
                    foreach (var actor in values) if (selected.Add(actor)) output.Add(actor);
            }
            selected.Clear();
        }

        internal void Clear() { actors.Clear(); owners.Clear(); selected.Clear(); }
    }
}
