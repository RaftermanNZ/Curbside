using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Accessed only by the existing committed-junction reader. Entries contain
    // copied geometry and owned managed outlines, never buffers, views or jobs.
    internal sealed class SignalJunctionFootprintCache
    {
        private const int Limit = 1024;
        private sealed class Entry
        {
            internal Entity Composition;
            internal EdgeNodeGeometry Geometry;
            internal NetCompositionData Data;
            internal float Low, High;
            internal SignalJunctionFootprint Footprint;
            internal bool Matches(Entity composition, EdgeNodeGeometry geometry, NetCompositionData data, float low, float high)
                => Composition == composition && Low.Equals(low) && High.Equals(high)
                && Data.m_Width.Equals(data.m_Width) && Data.m_MiddleOffset.Equals(data.m_MiddleOffset)
                && Data.m_SyncVertexOffsetsLeft.Equals(data.m_SyncVertexOffsetsLeft) && Data.m_SyncVertexOffsetsRight.Equals(data.m_SyncVertexOffsetsRight)
                && Geometry.m_MiddleRadius.Equals(geometry.m_MiddleRadius)
                && Geometry.m_SyncVertexTargetsLeft.Equals(geometry.m_SyncVertexTargetsLeft) && Geometry.m_SyncVertexTargetsRight.Equals(geometry.m_SyncVertexTargetsRight)
                && Same(Geometry.m_Left.m_Left, geometry.m_Left.m_Left) && Same(Geometry.m_Left.m_Right, geometry.m_Left.m_Right)
                && Same(Geometry.m_Right.m_Left, geometry.m_Right.m_Left) && Same(Geometry.m_Right.m_Right, geometry.m_Right.m_Right);
        }
        private readonly Dictionary<(Entity, bool), Entry> entries = new Dictionary<(Entity, bool), Entry>();
        private readonly Queue<(Entity, bool)> order = new Queue<(Entity, bool)>();
        private static bool Same(Bezier4x3 a, Bezier4x3 b) => a.a.Equals(b.a) && a.b.Equals(b.b) && a.c.Equals(b.c) && a.d.Equals(b.d);
        internal int Count => entries.Count;
        internal void Clear() { entries.Clear(); order.Clear(); }
        internal SignalJunctionFootprint Get(Entity road, bool start, Entity composition, EdgeNodeGeometry geometry, NetCompositionData data, float low, float high)
        {
            var key = (road, start);
            if (entries.TryGetValue(key, out var previous) && previous.Matches(composition, geometry, data, low, high)) return previous.Footprint;
            var footprint = SignalJunctionFootprint.Create(geometry, data, low, high);
            if (!entries.ContainsKey(key))
            {
                if (entries.Count == Limit) entries.Remove(order.Dequeue());
                order.Enqueue(key);
            }
            entries[key] = new Entry { Composition = composition, Geometry = geometry, Data = data, Low = low, High = high, Footprint = footprint };
            return footprint;
        }
    }
}
