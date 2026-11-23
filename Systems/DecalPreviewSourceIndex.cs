using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    // Scratch index for one read-only decal-preview pass. Only IDs and copied
    // cell coordinates persist inside that pass; no ECS views or cross-frame proofs.
    internal sealed class DecalPreviewSourceIndex
    {
        internal struct Range { internal int Head, Tail; }
        private struct Work { internal int Queries; internal long Scanned; }
        private sealed class Bucket
        {
            internal readonly List<Entity> Members = new List<Entity>();
            internal Dictionary<int3, Range> Cells;
            internal int[] Next;
            internal bool Prepared, Unbounded;
        }
        private sealed class OwnerBuckets
        {
            internal readonly Dictionary<Entity, Bucket> Prefabs = new Dictionary<Entity, Bucket>();
        }
        private readonly Dictionary<Entity, OwnerBuckets> owners = new Dictionary<Entity, OwnerBuckets>();
        private readonly Dictionary<Entity, Work> observed = new Dictionary<Entity, Work>();
        internal void Clear() { owners.Clear(); observed.Clear(); }

        internal void Record(Entity owner, int scanned)
        {
            observed.TryGetValue(owner, out var work);
            work.Queries++; work.Scanned += scanned; observed[owner] = work;
        }

        internal struct Query
        {
            private readonly List<Entity> members;
            private readonly Dictionary<int3, Range> cells;
            private readonly int[] next;
            private readonly int3 center;
            private int index, neighbour;
            internal Entity Current;
            internal Query(List<Entity> members, Dictionary<int3, Range> cells, int[] next, int3 center)
            {
                this.members = members; this.cells = cells; this.next = next; this.center = center;
                index = cells == null ? 0 : -1; neighbour = 0; Current = default;
            }
            internal bool MoveNext()
            {
                if (members == null) return false;
                if (cells == null)
                {
                    if (index >= members.Count) return false;
                    Current = members[index++]; return true;
                }
                while (index < 0 && neighbour < 27)
                {
                    int at = neighbour++;
                    var key = center + new int3(at / 9 - 1, at / 3 % 3 - 1, at % 3 - 1);
                    if (cells.TryGetValue(key, out var range)) index = range.Head;
                }
                if (index < 0) return false;
                Current = members[index]; index = next[index]; return true;
            }
        }

        private static bool TryCell(float3 position, out int3 cell)
        {
            cell = default;
            if (!math.all(math.isfinite(position)) || math.any(math.abs(position) > 1048576f)) return false;
            // Power-of-two scaling is exact for these float inputs. Cell width
            // 1/512 m exceeds the existing 1 mm acceptance distance; all true
            // matches therefore lie in the same or an immediately adjacent cell.
            cell = (int3)math.floor(position * 512f);
            return true;
        }

        internal bool TryQuery(EntityManager manager, Entity owner, Entity prefab, Transform pose,
            List<Entity> allSources, out Query query)
        {
            query = default;
            if (!owners.TryGetValue(owner, out var indexed))
            {
                // Sparse requests and quick ambiguous matches keep the existing
                // scan. Build only after substantial repeated work in this pass.
                if (!observed.TryGetValue(owner, out var work) || work.Queries < 2
                    || work.Scanned < 2L * allSources.Count
                    || work.Scanned < (long)work.Queries * allSources.Count / 4) return false;
                indexed = new OwnerBuckets();
                foreach (var source in allSources)
                {
                    if (!manager.Exists(source) || manager.HasComponent<Deleted>(source)
                        || manager.HasComponent<Temp>(source) || !manager.HasComponent<PrefabRef>(source)) continue;
                    var key = manager.GetComponentData<PrefabRef>(source).m_Prefab;
                    if (!indexed.Prefabs.TryGetValue(key, out var bucket))
                        indexed.Prefabs.Add(key, bucket = new Bucket());
                    bucket.Members.Add(source); // Duplicate memberships stay ambiguous.
                }
                owners.Add(owner, indexed);
            }
            if (!indexed.Prefabs.TryGetValue(prefab, out var selected)) return true;
            if (selected.Members.Count <= 32 || !TryCell(pose.m_Position, out var center))
            { query = new Query(selected.Members, null, null, default); return true; }

            if (!selected.Prepared)
            {
                selected.Cells = new Dictionary<int3, Range>(selected.Members.Count);
                selected.Next = new int[selected.Members.Count];
                for (int i = 0; i < selected.Members.Count; i++)
                {
                    selected.Next[i] = -1;
                    var source = selected.Members[i];
                    if (!manager.Exists(source) || manager.HasComponent<Deleted>(source)
                        || manager.HasComponent<Temp>(source) || !manager.HasComponent<Owner>(source)
                        || manager.GetComponentData<Owner>(source).m_Owner != owner
                        || !manager.HasComponent<Transform>(source)) continue;
                    var native = manager.GetComponentData<Transform>(source);
                    if (!TryCell(native.m_Position, out var cell))
                    {
                        // Finite out-of-range coordinates can still match under
                        // the old predicate. Retain its full scan for this bucket.
                        if (math.all(math.isfinite(native.m_Position))) selected.Unbounded = true;
                        continue;
                    }
                    if (selected.Cells.TryGetValue(cell, out var range))
                    { selected.Next[range.Tail] = i; range.Tail = i; }
                    else range = new Range { Head = i, Tail = i };
                    selected.Cells[cell] = range;
                }
                selected.Prepared = true;
            }
            if (selected.Unbounded) { query = new Query(selected.Members, null, null, default); return true; }
            // A <=1 mm match can cross a cell face, edge or corner. Enumerate
            // all neighbours, then let the unchanged pose/ownership predicate decide.
            query = new Query(selected.Members, selected.Cells, selected.Next, center);
            return true;
        }
    }
}
