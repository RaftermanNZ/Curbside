using System;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Only copied Entity IDs and numeric stamps survive a batch. Never retain
    // a DynamicBuffer, chunk, lookup, query result or safety handle in a cache.
    internal sealed class PreviewNativeLists
    {
        private readonly PreviewBufferList<Game.Objects.SubObject> objects =
            new PreviewBufferList<Game.Objects.SubObject>(value => value.m_SubObject);
        private readonly PreviewBufferList<SubLane> lanes = new PreviewBufferList<SubLane>(value => value.m_SubLane);
        private int previousFrame;
        private uint previousVersion;
        private bool haveFrame;

        internal void BeginBatch(EntityManager manager, EntityQuery objectReaders, EntityQuery laneReaders,
            bool supported, int frame, ulong selection, Func<Entity, bool> interested)
        {
            // Idle gaps and version wrap are cold starts. No independent sweep
            // is needed to prune old tools, saves, entity identities or roots.
            uint version = manager.GlobalSystemVersion;
            if (!haveFrame || frame < previousFrame || frame - previousFrame > 1 || version < previousVersion) Reset();
            previousFrame = frame; previousVersion = version; haveFrame = true;
            objects.BeginBatch(manager, objectReaders, supported, selection, interested);
            lanes.BeginBatch(manager, laneReaders, supported, selection, interested);
        }

        internal bool Objects(EntityManager manager, Entity owner, bool cacheable, Action<Entity> visit,
            out DynamicBuffer<Game.Objects.SubObject> native)
            => objects.TryVisit(manager, owner, cacheable, visit, out native);
        internal bool Lanes(EntityManager manager, Entity owner, bool cacheable, Action<Entity> visit,
            out DynamicBuffer<SubLane> native)
            => lanes.TryVisit(manager, owner, cacheable, visit, out native);
        internal void EndBatch() { objects.EndBatch(); lanes.EndBatch(); }
        internal void Reset() { objects.Reset(); lanes.Reset(); haveFrame = false; }
    }

    internal sealed class PreviewBufferList<T> where T : unmanaged, IBufferElementData
    {
        internal const int Slots = 64;
        internal const int MinimumChildren = 16;
        internal const int MaximumChildren = 512;
        internal const int ChangedVisitBypass = 16;

        private sealed class Entry
        {
            internal bool Valid;
            internal Entity Owner;
            internal ulong Chunk, Selection;
            internal uint Order, Data, CapturedSystemVersion;
            internal Entity[] All = Array.Empty<Entity>(), Selected = Array.Empty<Entity>();
            internal int Count, SelectedCount;
            internal int ChangedReads, Bypass;
        }

        private readonly Entry[] entries = new Entry[Slots];
        private readonly Func<T, Entity> identity;
        private BufferTypeHandle<T> type;
        private bool supported, readersReady, haveType;
        private int storedEntries;
        private ulong selection;
        private Func<Entity, bool> interested;

        internal PreviewBufferList(Func<T, Entity> identity) { this.identity = identity; }

        internal void BeginBatch(EntityManager manager, EntityQuery readers, bool supported,
            ulong selection, Func<Entity, bool> interested)
        {
            this.supported = supported;
            this.selection = selection;
            this.interested = interested;
            readersReady = false;
            haveType = false;
            type = default;
            if (!supported || storedEntries == 0) return;
            // GetDependency covers writers of this buffer TYPE, not merely
            // owners returned by the query. A finished handle still needs its
            // bookkeeping retired; never Complete an unfinished handle here.
            var writers = readers.GetDependency();
            if (writers.IsCompleted)
            {
                writers.Complete();
                readersReady = true;
            }
            type = manager.GetBufferTypeHandle<T>(true);
            haveType = true;
        }

        internal void EndBatch()
        {
            type = default;
            interested = null;
            supported = readersReady = haveType = false;
        }

        internal void Reset()
        {
            EndBatch();
            storedEntries = 0;
            foreach (var entry in entries) if (entry != null) { entry.Valid = false; entry.ChangedReads = entry.Bypass = 0; }
        }

        internal bool TryVisit(EntityManager manager, Entity owner, bool cacheable, Action<Entity> visit,
            out DynamicBuffer<T> native)
        {
            native = default;
            int slot = unchecked(owner.Index * 397 ^ owner.Version) & (Slots - 1);
            var entry = entries[slot];
            bool bypass = entry != null && entry.Owner == owner && entry.Bypass > 0;
            if (!bypass && supported && cacheable && readersReady && entry != null && entry.Valid && entry.Owner == owner
                && entry.CapturedSystemVersion != manager.GlobalSystemVersion)
            {
                var current = manager.GetChunk(owner);
                if (entry.Chunk == current.SequenceNumber && entry.Order == current.GetOrderVersion()
                    && entry.Data == current.GetChangeVersion(ref type))
                {
                    entry.ChangedReads = 0;
                    if (entry.Selection != selection) Select(entry);
                    for (int i = 0; i < entry.SelectedCount; i++) visit(entry.Selected[i]);
                    return true;
                }
            }

            // Pending writers can stamp their version only WHEN their job runs.
            // A header that looks unchanged while a writer is pending is NOT
            // proof of unchanged data. Keep the baseline dependency-safe read.
            var children = manager.GetBuffer<T>(owner, true);
            native = children;
            if (supported) readersReady = true; // GetBuffer completed this type's writers.
            if (bypass || !supported || !cacheable || children.Length < MinimumChildren || children.Length > MaximumChildren)
            {
                if (bypass) entry.Bypass--;
                if (entry != null && entry.Owner == owner && entry.Valid) { entry.Valid = false; storedEntries--; }
                return false;
            }

            if (entry == null) entries[slot] = entry = new Entry();
            bool wasValid = entry.Valid;
            bool same = entry.Valid && entry.Owner == owner && entry.Count == children.Length;
            if (same)
            {
                int i = 0;
                foreach (var value in children) if (entry.All[i++] != identity(value)) { same = false; break; }
            }
            // Repeated real membership churn cannot amortize copying/filtering.
            // Use the ordinary native path for sixteen requested visits, then
            // reconsider. This is an admission hint, not deferred visibility.
            if (entry.Valid && entry.Owner == owner && !same)
            {
                if (++entry.ChangedReads >= 2)
                {
                    entry.Valid = false; storedEntries--; entry.ChangedReads = 0; entry.Bypass = ChangedVisitBypass;
                    return false;
                }
            }
            else entry.ChangedReads = 0;
            entry.Valid = false;
            entry.Owner = owner;
            entry.Bypass = 0;
            entry.Count = children.Length;
            if (entry.All.Length < entry.Count)
            {
                int capacity = 1;
                while (capacity < entry.Count) capacity <<= 1;
                entry.All = new Entity[capacity];
                entry.Selected = new Entity[capacity];
            }
            if (!same)
            {
                int i = 0;
                foreach (var value in children) entry.All[i++] = identity(value);
            }
            if (!haveType) { type = manager.GetBufferTypeHandle<T>(true); haveType = true; }
            var chunk = manager.GetChunk(owner);
            entry.Chunk = chunk.SequenceNumber;
            entry.Order = chunk.GetOrderVersion();
            entry.Data = chunk.GetChangeVersion(ref type);
            // Multiple writes inside one system update share a version number.
            // Never use version-only reuse within that same update interval.
            entry.CapturedSystemVersion = manager.GlobalSystemVersion;
            // Native write access can mark an entire chunk changed even when
            // this owner's IDs are identical. Exact equality, not a hash,
            // allows reuse of the selected keys after the safe read too.
            if (!same || entry.Selection != selection) Select(entry);
            entry.Valid = true;
            if (!wasValid) storedEntries++;
            for (int i = 0; i < entry.SelectedCount; i++) visit(entry.Selected[i]);
            return true;
        }

        private void Select(Entry entry)
        {
            entry.SelectedCount = 0;
            for (int i = 0; i < entry.Count; i++)
            {
                var child = entry.All[i];
                if (interested == null || interested(child)) entry.Selected[entry.SelectedCount++] = child;
            }
            entry.Selection = selection;
        }
    }
}
