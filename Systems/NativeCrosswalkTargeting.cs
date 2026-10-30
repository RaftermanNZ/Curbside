using Colossal.Mathematics;

using Game.Net;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class NativeCrosswalkTargeting

    {

        // Same physical-end calculation as NetToolSystem.IsNearEnd (before path inversion).

        internal static bool NearEnd(EdgeGeometry geometry, float3 point)

        {

            var first = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, .5f);

            var last = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, .5f);

            float a = MathUtils.Distance(first.xz, point.xz, out float ta), b = MathUtils.Distance(last.xz, point.xz, out float tb);

            float la = geometry.m_Start.middleLength, lb = geometry.m_End.middleLength;

            return math.select(ta * la, la + tb * lb, b < a) > (la + lb) * .5f;

        }

    }

}

