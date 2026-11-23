using Colossal.Mathematics;
using Game.Net;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class CrosswalkCurbGeometry
    {
        internal static bool Fit(Bezier4x3 line, Bezier4x3 left, Bezier4x3 right,
            float width, out Bezier4x3 result)
            => Fit(line, left, right, default, default, width, out result);

        internal static bool Fit(Bezier4x3 line, Bezier4x3 left, Bezier4x3 right,
            Bezier4x3 leftSection, Bezier4x3 rightSection, float width, out Bezier4x3 result)
        {
            result = default;
            if (!PreviewPaintGeometry.Valid(line) || !math.isfinite(width) || width <= 0) return false;
            float length = MathUtils.Length(line);
            if (!math.isfinite(length) || length < .5f) return false;

            // Finite curb intersections may lie beyond the native paint span.
            float extension = math.min(4f, length * .5f) / length;
            var extended = MathUtils.Cut(line, new float2(-extension, 1 + extension));
            float low = 0, high = 1;
            bool leftFirst = false;
            for (int side = -1; side <= 1; side++)
            {
                var probe = NetUtils.OffsetCurveLeftSmooth(extended, side * (width * .5f + .1f));
                if (!Hit(probe, left, leftSection, out float a) || !Hit(probe, right, rightSection, out float b)
                    || (a < .5f) == (b < .5f)) return false;
                if (side == -1) leftFirst = a < b;
                else if (leftFirst != (a < b)) return false;
                low = math.max(low, math.min(a, b));
                high = math.min(high, math.max(a, b));
            }

            if (high <= low) return false;
            var fitted = MathUtils.Cut(extended, new float2(low, high));
            if (!PreviewPaintGeometry.Valid(fitted) || MathUtils.Length(fitted) < .5f) return false;
            result = fitted;
            return true;
        }

        private static bool Hit(Bezier4x3 probe, Bezier4x3 corner, Bezier4x3 section, out float t)
        {
            bool onCorner = Hit(probe, corner, out float a);
            bool onSection = Hit(probe, section, out float b);
            // At a seam, take the intersection that leaves the full painted
            // strip on its own carriageway.
            t = onCorner && (!onSection || math.abs(a - .5f) <= math.abs(b - .5f)) ? a : b;
            return onCorner || onSection;
        }

        private static bool Hit(Bezier4x3 probe, Bezier4x3 curb, out float t)
        {
            t = 0;
            if (!PreviewPaintGeometry.Valid(curb) || math.distancesq(curb.a.xz, curb.d.xz) < .0001f
                || !CrosswalkPlacement.TryFiniteIntersection(probe, curb, out t)) return false;
            var point = MathUtils.Position(probe, t);
            MathUtils.Distance(curb.xz, point.xz, out float at);
            return math.abs(point.y - MathUtils.Position(curb, at).y) <= .3f;
        }
    }
}
