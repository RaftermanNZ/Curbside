using System.Collections.Generic;
using Game.Objects;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class NativeSignalProjectionSystem
    {
        private struct NativeSignalCandidate { internal Entity Source; internal Transform Pose; }
        // Retain the earlier enabled preview's <12 m secondary matching limit.
        // This is distance from the generated prop to its original prop inside
        // one owner/role bucket, never a cursor radius or neighbouring-node search.
        private readonly NativePreviewSpatialIndex<NativeSignalRole, NativeSignalCandidate> signalPositions
            = new NativePreviewSpatialIndex<NativeSignalRole, NativeSignalCandidate>(12);
        private readonly List<NativeSignalCandidate> signalCandidates = new List<NativeSignalCandidate>();

        private bool TrySpatialSource(NativeSignalRole role, Transform proposed, out Entity source, out float score)
        {
            source = Entity.Null; score = float.MaxValue;
            if (!signalPositions.Query(role, proposed.m_Position, signalCandidates)) return false;
            bool tied = false;
            foreach (var candidate in signalCandidates)
            {
                if (!SignalPreviewIdentityPolicy.TrySecondaryScore(proposed, candidate.Pose, out float distance)) continue;
                // Primary objects have stronger native slot identity. Only
                // tolerate sub-millimetre arithmetic drift for their pose fallback.
                if (!role.Secondary && distance > .000001f) continue;
                if (SignalPreviewIdentityPolicy.Better(distance, score)) { score = distance; source = candidate.Source; tied = false; }
                else if (SignalPreviewIdentityPolicy.Tied(distance, score) && candidate.Source != source) tied = true;
            }
            return source != Entity.Null && !tied;
        }
    }
}
