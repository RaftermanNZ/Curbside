using System.Collections.Generic;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    // Derived managed values only. Every authoritative association write is
    // mirrored here; readers still verify the authoritative record before use.
    internal sealed class NotificationLookup<TKey> where TKey : struct
    {
        private readonly Dictionary<TKey, HashSet<Entity>> targets = new Dictionary<TKey, HashSet<Entity>>();
        private readonly Dictionary<Entity, HashSet<TKey>> keys = new Dictionary<Entity, HashSet<TKey>>();
        private readonly HashSet<TKey> incoming = new HashSet<TKey>();
        internal int Count => keys.Count;
        internal int KeyCount => targets.Count;
        internal void Replace(Entity actor, TKey key)
        { incoming.Clear(); incoming.Add(key); try { Commit(actor); } finally { incoming.Clear(); } }
        internal void Replace(Entity actor, IEnumerable<TKey> values)
        { incoming.Clear(); try { foreach (var value in values) incoming.Add(value); Commit(actor); } finally { incoming.Clear(); } }
        private void Commit(Entity actor)
        {
            if (incoming.Count == 0) { Remove(actor); return; }
            if (!keys.TryGetValue(actor, out var previous))
            { previous = new HashSet<TKey>(); keys.Add(actor, previous); }
            if (previous.SetEquals(incoming)) return;
            foreach (var key in previous)
                if (!incoming.Contains(key) && targets.TryGetValue(key, out var bucket))
                { bucket.Remove(actor); if (bucket.Count == 0) targets.Remove(key); }
            foreach (var key in incoming)
                if (!previous.Contains(key))
                { if (!targets.TryGetValue(key, out var bucket)) targets.Add(key, bucket = new HashSet<Entity>()); bucket.Add(actor); }
            previous.Clear(); previous.UnionWith(incoming);
            if (previous.Count == 0) keys.Remove(actor);
        }
        internal void Remove(Entity actor)
        {
            if (!keys.TryGetValue(actor, out var previous)) return;
            foreach (var key in previous)
                if (targets.TryGetValue(key, out var bucket))
                { bucket.Remove(actor); if (bucket.Count == 0) targets.Remove(key); }
            keys.Remove(actor);
        }
        internal void Collect(TKey key, HashSet<Entity> output)
        { if (targets.TryGetValue(key, out var bucket)) foreach (var actor in bucket) output.Add(actor); }
        internal void Clear() { targets.Clear(); keys.Clear(); incoming.Clear(); }
    }
    internal struct PreviewNotificationKey : System.IEquatable<PreviewNotificationKey>
    {
        private Entity first, second; private bool approach;
        internal static PreviewNotificationKey Source(Entity source) => new PreviewNotificationKey { first = source };
        internal static PreviewNotificationKey Approach(Entity road, Entity node) => new PreviewNotificationKey { first = road, second = node, approach = true };
        public bool Equals(PreviewNotificationKey other) => first == other.first && second == other.second && approach == other.approach;
        public override bool Equals(object other) => other is PreviewNotificationKey value && Equals(value);
        public override int GetHashCode() => unchecked((first.GetHashCode() * 397 ^ second.GetHashCode()) * 397 ^ (approach ? 1 : 0));
    }
}
