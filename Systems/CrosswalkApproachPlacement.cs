using Colossal.Mathematics;
using Game.Net;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class CrosswalkApproachPlacement
    {
        internal static bool TryScore(EdgeGeometry geometry, bool start, Bezier4x3 crossing, out float score)
        {
            var segment = start ? geometry.m_Start : geometry.m_End;
            return TryScoreMouth(start ? segment.m_Left.a : segment.m_Left.d,
                start ? segment.m_Right.a : segment.m_Right.d, crossing, out score);
        }

        internal static bool TryScoreMouth(float3 left, float3 right, Bezier4x3 crossing, out float score)
        {
            score = float.MaxValue;
            float width = math.distance(left.xz, right.xz);
            if (!math.isfinite(width) || width < .1f) return false;
            var axis = (right.xz - left.xz) / width;
            var tangent = math.normalizesafe(MathUtils.Tangent(crossing, .5f).xz);
            float alignment = math.abs(math.dot(axis, tangent));
            if (alignment < .7f) return false;

            var midpoint = MathUtils.Position(crossing, .5f);
            if (!math.all(math.isfinite(midpoint))) return false;
            float along = math.dot(midpoint.xz - left.xz, axis);
            var mouth = math.lerp(left, right, math.saturate(along / width));
            float elevation = math.abs(midpoint.y - mouth.y);
            float depth = math.distance(midpoint.xz, mouth.xz);
            if (elevation > 3f || depth > math.max(12f, width * .5f)) return false;

            float a = math.dot(crossing.a.xz - left.xz, axis);
            float b = math.dot(crossing.d.xz - left.xz, axis);
            // Test the whole crossing span. A wide mouth can contain the
            // midpoint of a different approach at a skewed split or merge.
            float outside = math.max(0f, -math.min(a,b)) + math.max(0f, math.max(a,b) - width);
            score = depth + outside * 2f + elevation * 2f + (1f - alignment) * math.max(4f, math.abs(b-a));
            return math.isfinite(score);
        }
    }
}
