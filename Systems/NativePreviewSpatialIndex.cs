using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Batch-local, owner/role-partitioned lookup. Cell width must be at least
    // the caller's acceptance radius. Query all 27 neighbours so a cell edge
    // never becomes an appearance boundary. Overfull searches fail closed.
    // Expected O(S) build and O(27 + 64) per lookup, O(S) memory; no city query.
    internal sealed class NativePreviewSpatialIndex<TKey, TValue> where TKey : struct, IEquatable<TKey>
    {
        internal const int CandidateLimit = 64;
        private struct Cell : IEquatable<Cell>
        {
            internal TKey Role;
            internal int X, Y, Z;
            public bool Equals(Cell other) => Role.Equals(other.Role) && X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object other) => other is Cell cell && Equals(cell);
            public override int GetHashCode()
            { unchecked { return ((Role.GetHashCode() * 397 ^ X) * 397 ^ Y) * 397 ^ Z; } }
        }
        private sealed class Bucket
        {
            internal readonly List<TValue> Values = new List<TValue>();
            internal bool Overflow;
        }
        private readonly double width;
        private readonly Dictionary<Cell, Bucket> cells = new Dictionary<Cell, Bucket>();
        internal NativePreviewSpatialIndex(double width)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            this.width = width;
        }
        internal void Clear() => cells.Clear();
        private bool TryCell(TKey role, float3 position, out Cell cell)
        {
            cell = default;
            double x = Math.Floor(position.x / width), y = Math.Floor(position.y / width), z = Math.Floor(position.z / width);
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)
                || x < int.MinValue + 1d || x > int.MaxValue - 1d
                || y < int.MinValue + 1d || y > int.MaxValue - 1d
                || z < int.MinValue + 1d || z > int.MaxValue - 1d) return false;
            cell = new Cell { Role = role, X = (int)x, Y = (int)y, Z = (int)z }; return true;
        }
        internal void Add(TKey role, float3 position, TValue value)
        {
            if (!TryCell(role, position, out var cell)) return;
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new Bucket();
            if (bucket.Values.Count == CandidateLimit) bucket.Overflow = true;
            else bucket.Values.Add(value);
        }
        internal bool Query(TKey role, float3 position, List<TValue> result)
        {
            result.Clear();
            if (!TryCell(role, position, out var center)) return false;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            {
                var cell = new Cell { Role = role, X = center.X + x, Y = center.Y + y, Z = center.Z + z };
                if (!cells.TryGetValue(cell, out var bucket)) continue;
                if (bucket.Overflow || result.Count + bucket.Values.Count > CandidateLimit) { result.Clear(); return false; }
                result.AddRange(bucket.Values);
            }
            return true;
        }
    }
}
