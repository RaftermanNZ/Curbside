using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class CrosswalkIntentPolicy
    {
        internal static float Score(float3 a, float3 b)
        {
            if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) ||
                math.lengthsq(a.xz) < .001f || math.lengthsq(b.xz) < .001f) return -1f;
            return math.dot(math.normalize(a.xz), math.normalize(b.xz));
        }

        internal static bool Matches(float3 a, float3 b) => Score(a,b) >= .95f;

        internal struct LegacyMatch
        {
            private float best, next;
            // Scores are positive in range. The zero-initialized second
            // score is therefore immaterial until another road qualifies.
            internal bool Unique => best >= .95f && best - next > .0001f;

            internal bool Consider(float score)
            {
                if (score < .95f) return false;
                if (score <= best)
                {
                    next = math.max(next,score);
                    return false;
                }
                next = best;
                best = score;
                return true;
            }
        }
    }
}
