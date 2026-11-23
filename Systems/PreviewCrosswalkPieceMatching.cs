using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class PreviewCrosswalkPieceMatching
    {
        // Called only inside an already-proved road/node approach. Native road
        // compositions can have several crossing pieces with regenerated IDs.
        // Compare their spans in either direction, not their prefab names.
        internal static bool TryScore(Bezier4x3 preview, Bezier4x3 source, out float score)
        {
            score = float.MaxValue;
            var a = preview.d - preview.a; var b = source.d - source.a;
            float aa = math.dot(a, a), bb = math.dot(b, b), ab = math.dot(a, b);
            if (!math.isfinite(aa) || !math.isfinite(bb) || !math.isfinite(ab)
                || aa < .01f || bb < .01f || aa > bb * 4f || bb > aa * 4f
                || ab * ab < .81f * aa * bb) return false;
            if (ab < 0) source = new Bezier4x3(source.d, source.c, source.b, source.a);
            // Require overlapping spans along the crossing axis, so a missing
            // half cannot borrow the adjoining half's saved paint.
            float start = math.dot(source.a - preview.a, a) / aa;
            float end = math.dot(source.d - preview.a, a) / aa;
            float overlap = math.min(1f, end) - math.max(0f, start);
            if (!math.isfinite(overlap) || overlap < .5f) return false;
            float d0 = math.distancesq(preview.a, source.a), d1 = math.distancesq(preview.b, source.b);
            float d2 = math.distancesq(preview.c, source.c), d3 = math.distancesq(preview.d, source.d);
            if (!math.isfinite(d0 + d1 + d2 + d3) || math.max(math.max(d0, d1), math.max(d2, d3)) >= 144f) return false;
            score = (d0 + d1 + d2 + d3) * .25f;
            return true;
        }
    }
}
