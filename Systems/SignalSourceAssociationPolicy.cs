using System.Collections.Generic;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalSourceAssociationPolicy
    {
        // Committed-node work only. Keep existing near-curb associations;
        // median actors need a proven phase and a unique local road mouth,
        // rather than a larger nearest-curb radius. No actor or World scan.
        internal static int Find(Transform original,
            IList<FarSideLightPolicy.Approach> roads, ushort phase)
        {
            if (roads == null || !math.all(math.isfinite(original.m_Position))) return -1;
            int nearby = GardenCityLightPolicy.NearestForPhase(original, roads, phase);
            return nearby >= 0 ? nearby : SignalMedianApproachPolicy.Find(original.m_Position, phase, roads);
        }
    }
}
