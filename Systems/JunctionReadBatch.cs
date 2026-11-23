using System;
using System.Collections.Generic;

namespace MedianGrassReplacement.Systems
{
    // A single synchronous read phase owns this memo. It never suppresses the
    // first visit, persists a result across updates, or drops work at capacity.
    internal sealed class JunctionReadBatch<TKey>
    {
        private readonly int capacity;
        private readonly HashSet<TKey> owners = new HashSet<TKey>();
        private readonly Dictionary<TKey,int> roadCounts = new Dictionary<TKey,int>();
        internal JunctionReadBatch(int capacity=4096) { this.capacity=Math.Max(1,capacity); }
        internal void Clear() { owners.Clear(); roadCounts.Clear(); }
        internal bool FirstOwner(TKey owner)
        {
            if(owners.Contains(owner)) return false;
            if(owners.Count<capacity) owners.Add(owner);
            return true;
        }
        internal int RoadCount(TKey node, Func<TKey,int> read)
        {
            if(roadCounts.TryGetValue(node,out int count)) return count;
            count=read(node);
            if(roadCounts.Count<capacity) roadCounts.Add(node,count);
            return count;
        }
    }
}
