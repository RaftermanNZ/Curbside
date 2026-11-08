using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal struct PreviewCrosswalkApproach
    {
        internal Entity Road, Node;
        internal EdgeGeometry Geometry;
        internal bool Start;
    }

    internal static class CrosswalkPreviewChoicePolicy
    {
        // An explicit native appearance wins over older hidden/line defaults.
        internal static byte Appearance(bool hasNodeIntent, byte nodeIntent, bool invisible, bool hasRoadStyle, bool lined)
            => hasNodeIntent ? nodeIntent : invisible ? (byte)2 : hasRoadStyle && lined ? (byte)1 : (byte)0;

        internal static bool Better(float score, float best) => score < best - .01f;
        internal static bool Tied(float score, float best) => Unity.Mathematics.math.abs(score - best) <= .01f;
    }

    public partial class CrosswalkUpgradeSystem
    {
        internal byte CommittedAppearance(Entity road, Entity node)
        {
            if (!EntityManager.Exists(road) || EntityManager.HasComponent<Deleted>(road)
                || EntityManager.HasComponent<Temp>(road) || !EntityManager.HasComponent<Edge>(road)) return 0;
            var edge = EntityManager.GetComponentData<Edge>(road);
            if (edge.m_Start != node && edge.m_End != node) return 0;
            bool start = edge.m_Start == node;
            bool intent = NodeIntent(road, node, out byte appearance);
            bool invisible = EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road)
                && EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start);
            bool line = false;
            bool style = EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road)
                && EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road).TryGet(start, out line);
            return CrosswalkPreviewChoicePolicy.Appearance(intent, appearance, invisible, style, line);
        }
    }

    public partial class IntersectionPaintSystem
    {
        internal void RevealCommittedCrosswalkPaint(HashSet<Entity> sources)
        {
            using (var visibility = new EntityCommandBuffer(Unity.Collections.Allocator.Temp))
            {
                bool changed = false;
                var seen = new HashSet<Entity>();
                foreach (var source in sources)
                    if (_replacements.TryGetValue(source, out var replacement) && replacement.RoutedCrossing)
                        foreach (var actor in replacement.Paint)
                            if (Alive(actor) && seen.Add(actor) && EntityManager.HasComponent<Hidden>(actor))
                            {
                                visibility.RemoveComponent<Hidden>(actor);
                                if (!EntityManager.HasComponent<BatchesUpdated>(actor)) visibility.AddComponent<BatchesUpdated>(actor);
                                changed = true;
                            }
                if (!changed) return;
                NativeVisualInvalidation.CompleteReaders(World);
                visibility.Playback(EntityManager);
            }
        }

        // The approach, rather than a transient lane ID/curve, is the appearance
        // identity. Reuse the committed crossing's actors; do not replan its lines.
        internal void ReadCommittedCrosswalkSources(Entity road, Entity node, List<Entity> output, List<Entity> scratch)
        {
            output.Clear(); scratch.Clear();
            _sourceIndex.CopyOwner(node, scratch);
            _sourceIndex.CopyOwner(road, scratch);
            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            foreach (var source in scratch)
            {
                if (!Alive(source) || !_replacements.TryGetValue(source, out var replacement) || !replacement.RoutedCrossing) continue;
                if (choices.TryResolveApproach(source, out var sourceRoad, out var sourceNode) && sourceRoad == road && sourceNode == node)
                    output.Add(source);
            }
        }
    }
}
