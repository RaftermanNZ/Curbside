using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class CrosswalkCurbGeometry
    {
        private const float FitEndClearance = .06f;
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
            float fittedLength = MathUtils.Length(fitted);
            if (!PreviewPaintGeometry.Valid(fitted) || !math.isfinite(fittedLength) || fittedLength < .5f + 2 * FitEndClearance) return false;
            float trim = FitEndClearance / fittedLength;
            result = MathUtils.Cut(fitted, new float2(trim, 1 - trim));
            return true;
        }

        internal static Bezier4x3 JoinEdgeEnds(Bezier4x3 rail, List<Bezier4x3> visibleEdges,
            bool fittedToCurbs = true, List<Bezier4x3> curbBoundaries = null, float unjoinedCurbSetback = 0, float railWidth = 0, List<Bezier4x3> emittedCurbEdges = null)
        {
            float length = MathUtils.Length(rail);
            if (!math.isfinite(length) || length < .5f) return rail;
            float reach = math.min(.35f, 1.75f / length);
            float setback = fittedToCurbs ? math.max(0, unjoinedCurbSetback - FitEndClearance) : 0;
            if (visibleEdges.Count == 0 && setback == 0) return rail;
            float start = EndParameter(rail);
            float end = 1 - EndParameter(MathUtils.Invert(rail));
            if (start == 0 && end == 1) return rail;
            var joined = MathUtils.Cut(rail, new float2(start, end));
            return PreviewPaintGeometry.Valid(joined) && MathUtils.Length(joined) >= .5f ? joined : rail;

            float EndParameter(Bezier4x3 oriented)
            {
                float speed = 3f * math.distance(oriented.a.xz, oriented.b.xz);
                float extension = 0;
                if (speed >= .1f)
                    extension = fittedToCurbs
                        ? ProvenExtension(oriented, speed)
                        : math.min(.5f, 4f / speed);
                var probe = MathUtils.Cut(oriented, new float2(-extension, reach));
                float minimum = -extension;
                if (!fittedToCurbs && extension > 0 && curbBoundaries != null)
                {
                    foreach (var curb in curbBoundaries)
                    {
                        if (!EdgeHit(probe, curb, out float t)) continue;
                        float at = math.lerp(-extension, reach, t);
                        if (at <= 0) minimum = math.max(minimum, at + .06f / speed);
                    }
                }
                float best = float.PositiveInfinity;
                foreach (var edge in visibleEdges)
                {
                    if (!EdgeHit(probe, edge, out float t)) continue;
                    float at = math.lerp(-extension, reach, t);
                    if (at < minimum) continue;
                    var point = MathUtils.Position(oriented, at);
                    if (at < 0 && (math.distancesq(point.xz, oriented.a.xz) > 16 ||
                        math.dot(MathUtils.Tangent(oriented, at).xz, MathUtils.StartTangent(oriented).xz) <= 0)) continue;
                    // Choose the outer stripe within the proven curb span. Beyond a short
                    // footprint stop at the first stripe reached from its tip.
                    if (best >= 0) best = math.min(best, at);
                    else if (at < 0) best = math.max(best, at);
                }
                if (math.isfinite(best)) return best;
                if (fittedToCurbs && speed >= .1f && railWidth > 0 && emittedCurbEdges?.Count > 0)
                {
                    // A curb guide can end at a seam while the painted stripe continues.
                    // Only bridge a short gap to paint already emitted on this carriageway.
                    float capReach = math.min(.35f, .28f / speed);
                    var cap = MathUtils.Cut(oriented, new float2(-capReach, reach));
                    float nearest = float.NegativeInfinity;
                    foreach (var edge in emittedCurbEdges)
                    {
                        if (!EdgeHit(cap, edge, out float t)) continue;
                        float at = math.lerp(-capReach, reach, t);
                        if (at >= 0 || at <= nearest) continue;
                        var point = MathUtils.Position(oriented, at);
                        if (math.distancesq(point.xz, oriented.a.xz) > .28f * .28f) continue;
                        MathUtils.Distance(edge.xz, point.xz, out float edgeAt);
                        if (math.abs(point.y - MathUtils.Position(edge, edgeAt).y) > .12f) continue;
                        nearest = at;
                    }
                    if (math.isfinite(nearest))
                    {
                        float requiredStart = nearest - .02f / speed;
                        var footprint = MathUtils.Cut(oriented, new float2(requiredStart, reach));
                        bool blocked = false;
                        if (curbBoundaries != null)
                        {
                            for (int side = -1; side <= 1 && !blocked; side++)
                            {
                                var curbProbe = side == 0 ? footprint : NetUtils.OffsetCurveLeftSmooth(footprint, side * railWidth * .5f);
                                foreach (var curb in curbBoundaries)
                                    if (Hit(curbProbe, curb, out _)) { blocked = true; break; }
                            }
                        }
                        if (!blocked) return nearest;
                    }
                }
                if (setback <= 0) return 0;
                var trim = new Bounds1(0f, 1f);
                MathUtils.ClampLength(oriented.xz, ref trim, setback);
                return trim.max;
            }
            float ProvenExtension(Bezier4x3 oriented, float speed)
            {
                if (curbBoundaries == null || curbBoundaries.Count == 0
                    || !math.isfinite(railWidth) || railWidth <= 0) return 0f;
                float limit = math.min(.45f, 1.5f / speed);
                float safe = limit;
                // All three probes must find the curb beyond the fitted end.
                // The nearest face limits the whole width of the stripe.
                var extended = MathUtils.Cut(oriented, new float2(-limit, reach));
                for (int side = -1; side <= 1; side++)
                {
                    var probe = side == 0 ? extended : NetUtils.OffsetCurveLeftSmooth(extended, side * railWidth * .5f);
                    float nearest = float.NegativeInfinity;
                    foreach (var curb in curbBoundaries)
                    {
                        if (!EdgeHit(probe, curb, out float at)) continue;
                        nearest = math.max(nearest, math.lerp(-limit, reach, at));
                    }
                    if (!math.isfinite(nearest) || nearest >= 0) return 0f;
                    safe = math.min(safe, -nearest - FitEndClearance / speed);
                }
                return math.max(0f, safe);
            }
        }

        internal static bool CoversMedianOpening(float2 median, float start, float end)
            => math.all(math.isfinite(median)) && median.y - median.x >= .15f
                && math.min(start, end) <= median.x + .05f && math.max(start, end) >= median.y - .05f;

        internal static bool CoversMedianOpening(float2 median, float3 start, float3 end)
            => math.all(math.isfinite(start)) && math.all(math.isfinite(end))
                && math.abs(start.y - end.y) <= .08f
                && CoversMedianOpening(median, start.x, end.x);

        internal static bool BracketsMedianOpening(float2 median, float3 leftOuter, float3 leftInner,
            float3 rightInner, float3 rightOuter)
        {
            // Some divided roads author two crossing spans with no center span.
            // Require the inside tips to bracket the same median on one row and deck.
            return math.all(math.isfinite(leftOuter)) && math.all(math.isfinite(leftInner))
                && math.all(math.isfinite(rightInner)) && math.all(math.isfinite(rightOuter))
                && leftOuter.x < median.x - .65f && rightOuter.x > median.y + .65f
                && rightInner.x - leftInner.x >= .08f
                && math.abs(leftInner.x - median.x) <= .45f && math.abs(rightInner.x - median.y) <= .45f
                && math.abs(leftInner.z - rightInner.z) <= .12f
                && math.abs(leftInner.y - rightInner.y) <= .05f
                && math.abs(leftOuter.y - leftInner.y) <= .08f
                && math.abs(rightOuter.y - rightInner.y) <= .08f;
        }

        internal static bool TryBridgeRails(Bezier4x3 first, Bezier4x3 second, Bezier4x3 otherFirst, Bezier4x3 otherSecond,
            out Bezier4x3 bridgeA, out Bezier4x3 bridgeB, float maximumSpan = 5f)
        {
            if (TryBridgeRail(first, otherFirst, maximumSpan, out bridgeA) && TryBridgeRail(second, otherSecond, maximumSpan, out bridgeB))
                return true;
            bridgeB = default;
            return TryBridgeRail(first, otherSecond, maximumSpan, out bridgeA) && TryBridgeRail(second, otherFirst, maximumSpan, out bridgeB);
        }

        private static bool TryBridgeRail(Bezier4x3 first, Bezier4x3 last, float maximumSpan, out Bezier4x3 bridge)
        {
            bridge = default;
            // Orient each selected rail toward the nearest endpoint of the other.
            bool flipFirst = math.min(math.distancesq(first.a, last.a), math.distancesq(first.a, last.d))
                < math.min(math.distancesq(first.d, last.a), math.distancesq(first.d, last.d));
            if (flipFirst) first = MathUtils.Invert(first);
            if (math.distancesq(first.d, last.d) < math.distancesq(first.d, last.a)) last = MathUtils.Invert(last);
            var delta = last.a - first.d;
            float length = math.length(delta);
            if (!math.isfinite(length) || length < .08f || length > maximumSpan || math.abs(delta.y) > .22f) return false;
            var atStart = math.normalizesafe(MathUtils.EndTangent(first));
            var atEnd = math.normalizesafe(MathUtils.StartTangent(last));
            var axis = delta / length;
            if (math.dot(atStart, axis) < .98f || math.dot(atEnd, axis) < .98f ||
                math.length(delta.xz - atStart.xz * math.dot(delta.xz, atStart.xz)) > .2f) return false;
            bridge = new Bezier4x3(first.d, first.d + atStart * (length / 3f),
                last.a - atEnd * (length / 3f), last.a);
            return true;
        }

        private static bool EdgeHit(Bezier4x3 rail, Bezier4x3 edge, out float t)
        {
            t = 0;
            static float2 Min(Bezier4x3 path) => math.min(math.min(path.a.xz, path.b.xz), math.min(path.c.xz, path.d.xz));
            static float2 Max(Bezier4x3 path) => math.max(math.max(path.a.xz, path.b.xz), math.max(path.c.xz, path.d.xz));
            if (math.any(Min(rail) > Max(edge)) || math.any(Min(edge) > Max(rail)) || !Hit(rail, edge, out t)) return false;
            var point = MathUtils.Position(rail, t);
            MathUtils.Distance(edge.xz, point.xz, out float edgeAt);
            return math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(rail, t).xz),
                math.normalizesafe(MathUtils.Tangent(edge, edgeAt).xz))) < .95f;
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
