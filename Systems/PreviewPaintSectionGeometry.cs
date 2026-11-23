using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class PreviewPaintSectionGeometry
    {
        // Returns false only for invalid/unrepresentable input. An empty section
        // is successful with included=false, so omitted dashes cannot reappear.
        internal static bool Map(Bezier4x3 stroke, Bezier4x3 original, Bezier4x3 preview,
            float2 range, out Bezier4x3 result, out bool included)
        {
            result = stroke; included = false;
            if (!PreviewPaintGeometry.Valid(stroke) || !PreviewPaintGeometry.Valid(original)
                || !PreviewPaintGeometry.Valid(preview) || !math.all(math.isfinite(range))) return false;
            if (range.Equals(new float2(0, 1)))
            { included = PreviewPaintGeometry.Map(stroke, original, preview, out result); return included; }
            if (math.abs(range.y - range.x) < .000001f || math.any(range < 0) || math.any(range > 1)) return false;
            float At(float t) { MathUtils.Distance(original, MathUtils.Position(stroke, t), out float p); return p; }
            float first = At(0), last = At(1), low = math.min(range.x, range.y), high = math.max(range.x, range.y);
            if (!math.isfinite(first) || !math.isfinite(last)) return false;
            float direction = last >= first ? 1 : -1;
            float previous = first;
            // Reject a folded stroke rather than assigning disconnected portions
            // to a single curve. Fixed probes bound work per affected stroke.
            for (int i = 1; i <= 8; i++)
            {
                float current = At(i / 8f);
                if (!math.isfinite(current) || (current - previous) * direction < -.0001f) return false;
                previous = current;
            }
            if (math.max(first, last) < low || math.min(first, last) > high) return true;
            if (math.abs(first - last) < .000001f && high < 1 && math.abs(first - high) < .000001f) return true;
            float Find(float target)
            {
                float a = 0, b = 1;
                for (int i = 0; i < 18; i++)
                { float mid = (a + b) * .5f; if ((At(mid) - target) * direction < 0) a = mid; else b = mid; }
                return (a + b) * .5f;
            }
            float from = direction > 0 ? (first < low ? Find(low) : 0) : (first > high ? Find(high) : 0);
            float to = direction > 0 ? (last > high ? Find(high) : 1) : (last < low ? Find(low) : 1);
            if (to - from <= .000001f) return true;
            var clipped = MathUtils.Cut(stroke, new float2(from, to));
            if (MathUtils.Length(clipped) <= .1f) return true;
            // Cutting in native endpoint order also handles reversed segments.
            var sourceSection = MathUtils.Cut(original, range);
            included = PreviewPaintGeometry.Map(clipped, sourceSection, preview, out result);
            return included;
        }
    }
}
