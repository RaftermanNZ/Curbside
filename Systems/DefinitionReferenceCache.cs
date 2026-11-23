using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal struct DefinitionReference
    {
        internal Entity Actor, Prefab;
    }

    internal struct DefinitionReferenceStamp
    {
        internal ulong Sequence;
        internal uint Order, Data, SystemVersion;
        internal int Count;
    }

    // Contains owned managed copies only; never holds ECS arrays or chunk pointers.
    // The payload is distinct prefab IDs and references to retiring definitions,
    // rather than a persistent per-entity index of the whole world.
    internal sealed class DefinitionReferenceCache
    {
        internal const int MaximumChunks=8192,MaximumValues=262144,MaximumReferences=65536;
        private sealed class Entry
        {
            internal DefinitionReferenceStamp Stamp;
            internal Entity[] Values;
            internal DefinitionReference[] Matches;
            internal int Revision,Seen;
        }
        private sealed class EntityOrder : IComparer<Entity>
        {
            internal static readonly EntityOrder Instance=new EntityOrder();
            public int Compare(Entity a,Entity b)
            { int c=a.Index.CompareTo(b.Index);return c!=0?c:a.Version.CompareTo(b.Version); }
        }
        private readonly Dictionary<ulong,Entry> entries=new Dictionary<ulong,Entry>();
        private readonly List<ulong> stale=new List<ulong>();
        private int pass,values,references;
        private bool haveVersion;
        private uint lastVersion;
        internal int ChunkCount=>entries.Count;
        internal int ValueCount=>values;
        internal int ReferenceCount=>references;

        internal void Begin(uint version)
        {
            // A wrap or backwards version jump cannot certify retained stamps.
            if((haveVersion && version<lastVersion) || pass==int.MaxValue)Clear();
            lastVersion=version;haveVersion=true;
            pass++;
        }
        internal bool TryAppend(DefinitionReferenceStamp stamp,int revision,
            IDictionary<Entity,Entity> targets,List<DefinitionReference> output)
        {
            if(!entries.TryGetValue(stamp.Sequence,out var entry))return false;
            entry.Seen=pass;
            var saved=entry.Stamp;
            // A stamp written in the capturing update can be written again with
            // the same value AFTER capture. Reread it at the next checkpoint.
            // Same-update reuse is also deliberately refused.
            if(saved.Order!=stamp.Order || saved.Data!=stamp.Data || saved.Count!=stamp.Count
                || saved.SystemVersion==stamp.SystemVersion
                || saved.Order==saved.SystemVersion || saved.Data==saved.SystemVersion)return false;
            if(entry.Revision!=revision)
            {
                // New retiring definitions can already have references in old,
                // unchanged chunks. The distinct-value inventory detects them.
                // Large target sets take a linear chunk payload pass instead.
                if(targets.Count>64)return false;
                foreach(var target in targets.Keys)
                    if(Array.BinarySearch(entry.Values,target,EntityOrder.Instance)>=0)return false;
                references-=entry.Matches.Length;
                entry.Matches=Array.Empty<DefinitionReference>();
                entry.Revision=revision;
            }
            foreach(var reference in entry.Matches)
                if(targets.ContainsKey(reference.Prefab))output.Add(reference);
            return true;
        }
        internal void Store(DefinitionReferenceStamp stamp,int revision,
            ICollection<Entity> distinct,List<DefinitionReference> matches)
        {
            Remove(stamp.Sequence);
            if(entries.Count>=MaximumChunks || values+distinct.Count>MaximumValues
                || references+matches.Count>MaximumReferences)return;
            var copied=new Entity[distinct.Count];distinct.CopyTo(copied,0);
            Array.Sort(copied,EntityOrder.Instance);
            entries.Add(stamp.Sequence,new Entry{Stamp=stamp,Revision=revision,Seen=pass,
                Values=copied,Matches=matches.ToArray()});
            values+=copied.Length;references+=matches.Count;
        }
        internal void End()
        {
            stale.Clear();
            foreach(var pair in entries)if(pair.Value.Seen!=pass)stale.Add(pair.Key);
            foreach(var sequence in stale)Remove(sequence);
            stale.Clear();
        }
        private void Remove(ulong sequence)
        {
            if(!entries.TryGetValue(sequence,out var entry))return;
            values-=entry.Values.Length;references-=entry.Matches.Length;
            entries.Remove(sequence);
        }
        // Retirement targets can disappear while the bounded, value-only
        // inventory remains useful for the next local edit. No idle polling.
        internal void ReleaseMatches()
        {
            if(references==0)return;
            foreach(var entry in entries.Values)entry.Matches=Array.Empty<DefinitionReference>();
            references=0;
        }
        internal void Clear()
        {
            entries.Clear();stale.Clear();pass=values=references=0;
            haveVersion=false;lastVersion=0;
        }
    }
}
