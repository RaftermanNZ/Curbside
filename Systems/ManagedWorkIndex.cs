using System.Collections.Generic;
namespace MedianGrassReplacement.Systems
{
    // Managed IDs/value records only; array rotation avoids per-item linked nodes.
    internal sealed class ManagedWorkIndex<TOwner,TItem>
    {
        private struct Entry {internal TOwner Owner;internal int Slot;}
        private readonly Dictionary<TItem,Entry> items=new Dictionary<TItem,Entry>();
        private readonly Dictionary<TOwner,HashSet<TItem>> owners=new Dictionary<TOwner,HashSet<TItem>>();
        private readonly List<TItem> rotation=new List<TItem>();private int next;
        internal int Count=>items.Count;
        internal void Add(TOwner owner,TItem item)
        {
            if(items.TryGetValue(item,out var existing)){if(EqualityComparer<TOwner>.Default.Equals(existing.Owner,owner))return;Remove(item);}
            if(!owners.TryGetValue(owner,out var group))owners[owner]=group=new HashSet<TItem>();
            group.Add(item);items[item]=new Entry{Owner=owner,Slot=rotation.Count};rotation.Add(item);
        }
        internal void Remove(TItem item)
        {
            if(!items.TryGetValue(item,out var entry))return;
            int last=rotation.Count-1;
            if(entry.Slot!=last){var moved=rotation[last];rotation[entry.Slot]=moved;var data=items[moved];data.Slot=entry.Slot;items[moved]=data;}
            rotation.RemoveAt(last);items.Remove(item);
            if(entry.Slot<next)next--;if(next>=rotation.Count)next=0;
            if(owners.TryGetValue(entry.Owner,out var group)){group.Remove(item);if(group.Count==0)owners.Remove(entry.Owner);}
        }
        internal void CopyOwner(TOwner owner,List<TItem> output)
        {if(owners.TryGetValue(owner,out var group))foreach(var item in group)output.Add(item);}
        internal void Rotate(List<TItem> output,int budget)
        {
            int count=System.Math.Min(System.Math.Max(0,budget),rotation.Count);
            for(int i=0;i<count;i++){if(next>=rotation.Count)next=0;output.Add(rotation[next++]);}
        }
        internal void Clear(){items.Clear();owners.Clear();rotation.Clear();next=0;}
    }
}
