using System.Collections.Generic;

namespace MedianGrassReplacement.Systems
{
    // Managed ownership index. A change visits only the affected junction;
    // recovery advances a persistent cursor with a fixed actor budget.
    internal sealed class SignalMaintenanceIndex<T>
    {
        internal const byte Moved = 1, Lamp = 2, Hidden = 4, GeneratedLamp = 8, LayoutNode = 16;
        internal sealed class Entry
        {
            internal T Actor, Node;
            internal byte Kinds;
            internal LinkedListNode<Entry> Link;
        }
        private readonly Dictionary<T, Entry> actors = new Dictionary<T, Entry>();
        private readonly Dictionary<T, HashSet<T>> nodes = new Dictionary<T, HashSet<T>>();
        private readonly LinkedList<Entry> ring = new LinkedList<Entry>();
        private LinkedListNode<Entry> cursor;
        internal int Count => actors.Count;
        internal bool TryGet(T actor, out Entry entry) => actors.TryGetValue(actor, out entry);
        internal void Track(T actor, T node, byte kind)
        {
            if (actors.TryGetValue(actor, out var entry))
            {
                if (!EqualityComparer<T>.Default.Equals(entry.Node, node))
                { RemoveNode(entry); entry.Node = node; AddNode(entry); }
                entry.Kinds |= kind;
                return;
            }
            entry = new Entry { Actor = actor, Node = node, Kinds = kind };
            entry.Link = ring.AddLast(entry); actors.Add(actor, entry); AddNode(entry);
            if (cursor == null) cursor = entry.Link;
        }
        private void AddNode(Entry entry)
        {
            if (!nodes.TryGetValue(entry.Node, out var set)) nodes.Add(entry.Node, set = new HashSet<T>());
            set.Add(entry.Actor);
        }
        private void RemoveNode(Entry entry)
        {
            if (!nodes.TryGetValue(entry.Node, out var set)) return;
            set.Remove(entry.Actor); if (set.Count == 0) nodes.Remove(entry.Node);
        }
        internal void Forget(T actor, byte kind)
        {
            if (!actors.TryGetValue(actor, out var entry)) return;
            entry.Kinds &= (byte)~kind; if (entry.Kinds != 0) return;
            if (cursor == entry.Link) cursor = entry.Link.Next ?? ring.First;
            ring.Remove(entry.Link); if (ring.Count == 0) cursor = null;
            actors.Remove(actor); RemoveNode(entry);
        }
        internal void CopyNode(T node, List<Entry> output)
        {
            output.Clear();
            if (nodes.TryGetValue(node, out var set)) foreach (var actor in set) output.Add(actors[actor]);
        }
        internal void TakeRecovery(int budget, List<Entry> output)
        {
            output.Clear(); int count = System.Math.Min(System.Math.Max(budget, 0), ring.Count);
            for (int i = 0; i < count; i++)
            {
                if (cursor == null) cursor = ring.First;
                output.Add(cursor.Value); cursor = cursor.Next ?? ring.First;
            }
        }
        internal void Clear() { actors.Clear(); nodes.Clear(); ring.Clear(); cursor = null; }
    }
}
