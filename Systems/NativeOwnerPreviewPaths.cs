using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class NativePaintProjectionSystem
    {
        private struct PreviewPathPair { internal Bezier4x3 Original, Preview; }
        private readonly Dictionary<Entity, PreviewPathPair> ownerPaths = new Dictionary<Entity, PreviewPathPair>();
        private readonly HashSet<Entity> ambiguousOwnerPaths = new HashSet<Entity>();

        private void ReadOwnerPaths(Entity parent, Entity source)
        {
            ownerPaths.Clear(); ambiguousOwnerPaths.Clear();
            if (!Live(parent) || !EntityManager.HasComponent<Node>(source) || !EntityManager.HasBuffer<SubLane>(parent)) return;
            // Native topology is authoritative. The committed planner already
            // records NativeSource on its native-derived junction marks.
            foreach (var child in EntityManager.GetBuffer<SubLane>(parent, true))
            {
                var actor = child.m_SubLane;
                if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor) || !EntityManager.HasComponent<Curve>(actor)
                    || !EntityManager.HasComponent<Owner>(actor) || EntityManager.GetComponentData<Owner>(actor).m_Owner != parent) continue;
                var temp = EntityManager.GetComponentData<Temp>(actor); var original = temp.m_Original;
                if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0 || !Live(original)
                    || !EntityManager.HasComponent<Curve>(original) || !EntityManager.HasComponent<Owner>(original)
                    || EntityManager.GetComponentData<Owner>(original).m_Owner != source || ambiguousOwnerPaths.Contains(original)) continue;
                var pair = new PreviewPathPair { Original = EntityManager.GetComponentData<Curve>(original).m_Bezier,
                    Preview = EntityManager.GetComponentData<Curve>(actor).m_Bezier };
                if (ownerPaths.TryGetValue(original, out var previous)
                    && !MarkingPreviewPolicy.SameCurve(previous.Preview, pair.Preview))
                { ownerPaths.Remove(original); ambiguousOwnerPaths.Add(original); }
                else ownerPaths[original] = pair;
            }
        }

        private bool SameOwnerPaths(Dictionary<Entity, PreviewPathPair> cached)
        {
            if (cached.Count != ownerPaths.Count) return false;
            foreach (var pair in ownerPaths)
                if (!cached.TryGetValue(pair.Key, out var before)
                    || !MarkingPreviewPolicy.SameCurve(before.Original, pair.Value.Original)
                    || !MarkingPreviewPolicy.SameCurve(before.Preview, pair.Value.Preview)) return false;
            return true;
        }
    }
}
