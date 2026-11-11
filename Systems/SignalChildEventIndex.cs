using System.Collections.Generic;

namespace MedianGrassReplacement.Systems
{
    // One modification pass only. Native secondary actors already exist after
    // the 4B barrier, before their owner buffers are repaired later in phase 5.
    // Never write those buffers ourselves or cache a native buffer view.
    internal sealed class SignalChildEventIndex<T>
    {
        private readonly Dictionary<T,HashSet<T>> owners = new Dictionary<T,HashSet<T>>();
        private readonly Stack<HashSet<T>> pool = new Stack<HashSet<T>>();
        internal int Count { get; private set; }
        internal void BeginPass()
        {
            foreach(var set in owners.Values){set.Clear();pool.Push(set);}
            owners.Clear();Count=0;
        }
        internal void Record(T owner,T actor)
        {
            if(!owners.TryGetValue(owner,out var set))
            {set=pool.Count>0?pool.Pop():new HashSet<T>();owners.Add(owner,set);}
            if(set.Add(actor))Count++;
        }
        internal IEnumerable<T> Pending(T owner)
        {
            if(owners.TryGetValue(owner,out var set))return set;
            return System.Array.Empty<T>();
        }
    }
}
