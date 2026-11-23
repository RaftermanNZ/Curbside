using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Used only while the inheritance system aggregates its read-only source
    // mappings, before processing requests, creating actors or applying writes.
    // Copied curves/IDs only; no ECS views or cross-batch validity assumption.
    internal sealed class PreviewSourcePaths
    {
        private struct Source
        {
            internal bool Available, Unchanged;
            internal Bezier4x3 Curve;
        }

        private readonly Dictionary<Entity, int> paths = new Dictionary<Entity, int>();
        private Source[] values = new Source[16];
        private int count;

        internal void Clear() { paths.Clear(); count = 0; }

        internal void Include(EntityManager manager, Entity source, Entity preview)
        {
            if (!paths.TryGetValue(source, out var index))
            {
                var path = new Source();
                path.Available = manager.Exists(source) && !manager.HasComponent<Deleted>(source)
                    && manager.HasComponent<Curve>(source);
                if (path.Available) path.Curve = manager.GetComponentData<Curve>(source).m_Bezier;
                path.Unchanged = path.Available && PreviewPaintGeometry.SamePath(path.Curve,
                    manager.GetComponentData<Curve>(preview).m_Bezier);
                if (count == values.Length) Array.Resize(ref values, values.Length * 2);
                values[count] = path;
                paths.Add(source, count++);
                return;
            }
            // Each preview participates, including a second copy that differs
            // after a split. Do not let a first matching copy authorize siblings.
            ref var saved = ref values[index];
            bool same = saved.Available && PreviewPaintGeometry.SamePath(saved.Curve,
                manager.GetComponentData<Curve>(preview).m_Bezier);
            saved.Unchanged &= same;
        }

        internal bool TryGetValue(Entity source, out bool unchanged)
        {
            bool found = paths.TryGetValue(source, out var index);
            unchanged = found && values[index].Unchanged;
            return found;
        }

        internal void CopyTo(Dictionary<Entity, bool> output)
        {
            foreach (var pair in paths) output.Add(pair.Key, values[pair.Value].Unchanged);
        }
    }
}
