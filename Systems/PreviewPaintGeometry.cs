using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class PreviewPaintGeometry
    {
        internal static bool Valid(Bezier4x3 path)
            => math.all(math.isfinite(path.a)) && math.all(math.isfinite(path.b))
                && math.all(math.isfinite(path.c)) && math.all(math.isfinite(path.d));

        internal static bool SamePath(Bezier4x3 a, Bezier4x3 b)
            => Valid(a) && Valid(b) && math.distancesq(a.a, b.a) <= .000001f
                && math.distancesq(a.b, b.b) <= .000001f && math.distancesq(a.c, b.c) <= .000001f
                && math.distancesq(a.d, b.d) <= .000001f;

        // Keep the saved stroke's lateral/height offsets while the native
        // preview provides the proposed path. Hovering unchanged geometry is
        // an exact fast path: no closest-point searches or replanning.
        internal static bool Map(Bezier4x3 stroke, Bezier4x3 original, Bezier4x3 preview, out Bezier4x3 result)
        {
            result = stroke;
            if (!Valid(stroke) || !Valid(original) || !Valid(preview)) return false;
            if (Point(stroke) || Point(original) || Point(preview)) return false;
            if (SamePath(original, preview)) return true;
            if (MathUtils.Length(original) < .1f || MathUtils.Length(preview) < .1f) return false;
            result = new Bezier4x3(MapPoint(stroke.a, original, preview), MapPoint(stroke.b, original, preview),
                MapPoint(stroke.c, original, preview), MapPoint(stroke.d, original, preview));
            return Valid(result) && MathUtils.Length(result) > .1f;
        }

        private static bool Point(Bezier4x3 path) => path.a.Equals(path.b) && path.b.Equals(path.c) && path.c.Equals(path.d);

        private static float3 MapPoint(float3 point, Bezier4x3 original, Bezier4x3 preview)
        {
            MathUtils.Distance(original, point, out float t);
            var from = MathUtils.Position(original, t);
            var to = MathUtils.Position(preview, t);
            float2 before = math.normalizesafe(MathUtils.Tangent(original, t).xz);
            float2 after = math.normalizesafe(MathUtils.Tangent(preview, t).xz);
            float2 delta = point.xz - from.xz;
            to.xz += after * math.dot(delta, before)
                + new float2(-after.y, after.x) * math.dot(delta, new float2(-before.y, before.x));
            to.y += point.y - from.y;
            return to;
        }
    }
}
