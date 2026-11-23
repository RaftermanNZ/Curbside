using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // One authoritative source binding per preview. This replaces the generic
    // multi-key notification index for this single-key use, and owns the exact
    // source-group AND. Only copied IDs, booleans and numeric proofs persist.
    internal sealed class PreviewSourceGroups
    {
        private sealed class Group
        {
            internal readonly HashSet<Entity> Members = new HashSet<Entity>();
            // Most groups touch one committed chunk and one preview chunk.
            // Inline those proofs instead of allocating a four-slot List/array
            // for every first hover. Larger split groups allocate only overflow.
            internal Stamp First, Second;
            internal int StampCount;
            internal List<Stamp> Extra;
            internal bool Valid, Same;
            internal uint CapturedVersion;
            internal void ClearStamps() { StampCount = 0; Extra?.Clear(); }
            internal void AddStamp(Stamp value)
            {
                if (StampCount == 0) First = value;
                else if (StampCount == 1) Second = value;
                else { if (Extra == null) Extra = new List<Stamp>(2); Extra.Add(value); }
                StampCount++;
            }
            internal Stamp GetStamp(int index) => index == 0 ? First : index == 1 ? Second : Extra[index - 2];
        }

        private struct Stamp
        {
            internal Entity Sample;
            internal ulong Sequence;
            internal uint Order, Curve;
            internal bool Valid;
        }

        private readonly Dictionary<Entity, Entity> bindings = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, Group> groups = new Dictionary<Entity, Group>();
        private readonly Dictionary<ulong, Stamp> currentChunks = new Dictionary<ulong, Stamp>();
        private readonly HashSet<ulong> gathered = new HashSet<ulong>();
        private object world;
        private uint previousVersion;
        private int previousFrame, churn, bypass;
        private bool haveHistory, haveProofs;

        internal int Count => bindings.Count;
        internal int KeyCount => groups.Count;

        internal void Replace(Entity actor, Entity source)
        {
            if (bindings.TryGetValue(actor, out var prior))
            {
                if (prior == source) return;
                Remove(actor);
            }
            if (!groups.TryGetValue(source, out var group))
                groups.Add(source, group = new Group());
            group.Members.Add(actor); group.Valid = false;
            bindings.Add(actor, source);
        }

        internal void Remove(Entity actor)
        {
            if (!bindings.TryGetValue(actor, out var source)) return;
            bindings.Remove(actor);
            if (!groups.TryGetValue(source, out var group)) return;
            group.Members.Remove(actor); group.Valid = false;
            if (group.Members.Count == 0) groups.Remove(source);
        }

        internal void Collect(Entity source, HashSet<Entity> output)
        {
            if (groups.TryGetValue(source, out var group))
                foreach (var actor in group.Members) output.Add(actor);
        }

        internal void Clear()
        {
            bindings.Clear(); groups.Clear(); currentChunks.Clear(); gathered.Clear();
            world = null; previousVersion = 0; previousFrame = churn = bypass = 0; haveHistory = haveProofs = false;
        }

        private void InvalidateProofs()
        {
            if (haveProofs) foreach (var group in groups.Values) group.Valid = false;
            haveProofs = false;
            currentChunks.Clear();
        }

        // Returns false before touching output when the old aggregation path
        // should run. A pending Curve writer is never completed by this proof.
        internal bool TryUpdate(EntityManager manager, EntityQuery readers, bool supported,
            int frame, Dictionary<Entity, bool> output)
        {
            if (groups.Count == 0) { output.Clear(); return true; }
            uint version = manager.GlobalSystemVersion;
            if (!haveHistory || !ReferenceEquals(world, manager.World) || frame < previousFrame
                || frame - previousFrame > 256 || version < previousVersion)
            {
                InvalidateProofs(); churn = bypass = 0;
            }
            world = manager.World; previousFrame = frame; previousVersion = version; haveHistory = true;
            if (!supported) { InvalidateProofs(); return false; }
            if (bypass != 0) { bypass--; return false; }
            var pending = readers.GetDependency();
            if (!pending.IsCompleted) return false;
            pending.Complete();
            var type = manager.GetComponentTypeHandle<Curve>(true);
            int reused = 0, failedWarm = 0;
            currentChunks.Clear();
            output.Clear();
            try
            {
                foreach (var pair in groups)
                {
                    var group = pair.Value;
                    bool wasValid = group.Valid;
                    if (CanReuse(manager, group, version, ref type)) reused++;
                    else
                    {
                        if (wasValid) failedWarm++;
                        group.Valid = false;
                        group.Same = Compare(manager, pair.Key, group, version, ref type);
                    }
                    output.Add(pair.Key, group.Same);
                }
                // Coarse writable access can invalidate every chunk without a
                // value change. Do not keep paying proof-construction overhead
                // on that workload: use the established aggregation for a short
                // number of existing callbacks, with no timer or extra scan.
                churn = failedWarm > reused ? churn + 1 : 0;
                if (churn >= 2) { InvalidateProofs(); bypass = 8; churn = 0; }
                return true;
            }
            catch
            {
                InvalidateProofs(); churn = bypass = 0;
                throw;
            }
            finally
            {
                // Type handles/native chunk locals never escape this read-only
                // method. The following actor/alias writes may change storage.
                currentChunks.Clear(); gathered.Clear();
            }
        }

        private bool CanReuse(EntityManager manager, Group group, uint version,
            ref ComponentTypeHandle<Curve> type)
        {
            // Writers within one system update can share the same version.
            if (!group.Valid || group.CapturedVersion == version) return false;
            for (int index = 0; index < group.StampCount; index++)
            {
                var saved = group.GetStamp(index);
                if (!currentChunks.TryGetValue(saved.Sequence, out var current))
                {
                    current = Read(manager, saved.Sample, ref type);
                    currentChunks[saved.Sequence] = current;
                }
                if (!current.Valid || current.Sequence != saved.Sequence
                    || current.Order != saved.Order || current.Curve != saved.Curve) return false;
            }
            return true;
        }

        private bool Compare(EntityManager manager, Entity source, Group group, uint version,
            ref ComponentTypeHandle<Curve> type)
        {
            group.ClearStamps(); gathered.Clear();
            bool available = manager.Exists(source) && !manager.HasComponent<Deleted>(source)
                && manager.HasComponent<Curve>(source);
            if (!available) return false;
            var original = ReadCurve(manager, source, group, ref type).m_Bezier;
            bool same = true;
            foreach (var actor in group.Members)
                same &= PreviewPaintGeometry.SamePath(original,
                    ReadCurve(manager, actor, group, ref type).m_Bezier);
            group.CapturedVersion = version;
            group.Valid = true;
            // A stamp produced in this very system interval can be rewritten
            // later in the interval without changing its number. Do not carry
            // that ambiguous snapshot into a later interval as a valid proof.
            for (int index = 0; index < group.StampCount; index++)
            {
                var stamp = group.GetStamp(index);
                if (stamp.Order == version || stamp.Curve == version) { group.Valid = false; break; }
            }
            haveProofs |= group.Valid;
            return same;
        }

        private Curve ReadCurve(EntityManager manager, Entity actor, Group group,
            ref ComponentTypeHandle<Curve> type)
        {
            // Collect a scalar proof while reading the curve, not by walking
            // every member a second time. The NativeArray is local to this
            // access and no structural/managed callback occurs before copying.
            var storage = manager.GetStorageInfo(actor);
            var chunk = storage.Chunk;
            if (!currentChunks.TryGetValue(chunk.SequenceNumber, out var stamp)
                || stamp.Sequence != chunk.SequenceNumber)
            {
                stamp = new Stamp { Sample = actor, Sequence = chunk.SequenceNumber,
                    Order = chunk.GetOrderVersion(), Curve = chunk.GetChangeVersion(ref type),
                    Valid = chunk.Has(ref type) };
                currentChunks[stamp.Sequence] = stamp;
            }
            // Match the old failed component-read behavior; never index an
            // absent native array even when release safety checks are disabled.
            if (!stamp.Valid) return manager.GetComponentData<Curve>(actor);
            if (gathered.Add(stamp.Sequence)) group.AddStamp(stamp);
            return chunk.GetNativeArray(ref type)[storage.IndexInChunk];
        }

        private static Stamp Read(EntityManager manager, Entity actor,
            ref ComponentTypeHandle<Curve> type)
        {
            if (!manager.Exists(actor)) return default;
            var chunk = manager.GetChunk(actor);
            return new Stamp { Sample = actor, Sequence = chunk.SequenceNumber,
                Order = chunk.GetOrderVersion(), Curve = chunk.GetChangeVersion(ref type), Valid = chunk.Has(ref type) };
        }
    }
}
