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

            // Keep the original curve for ordinary crossings. A short,
            // straight native stripe can also be extended to measured curbs.
            float extension = math.min(4f, length * .5f) / length;
            if (FitExtended(MathUtils.Cut(line, new float2(-extension, 1 + extension)),
                left, right, leftSection, rightSection, width, out result)) return true;

            var axis = math.normalizesafe(line.d.xz - line.a.xz);
            if (math.lengthsq(axis) < .5f) return false;
            var chord = line.d - line.a;
            float leftAt = math.dot(left.a.xz - line.a.xz, axis);
            float rightAt = math.dot(right.a.xz - line.a.xz, axis);
            float span = math.abs(rightAt - leftAt);
            if (!math.isfinite(span) || span < 1f) return false;
            float chordLength = math.length(chord.xz);
            float reach = math.max(4f, math.max(-math.min(leftAt,rightAt),
                math.max(leftAt,rightAt) - chordLength) + span * .5f) / chordLength;
            bool straight = math.distancesq(line.b.xz, (line.a + chord / 3f).xz) <= .04f &&
                math.distancesq(line.c.xz, (line.d - chord / 3f).xz) <= .04f;
            if (!straight)
            {
                // A longer cubic extrapolation can turn back or drift onto
                // another branch. Grow only while its control polygon keeps
                // moving across the same approach, then prove both curb hits.
                for (float step = extension * 1.5f; step < reach * 1.5f; step *= 1.5f)
                {
                    float lengthRatio = math.min(step,reach);
                    var candidate = MathUtils.Cut(line, new float2(-lengthRatio,1+lengthRatio));
                    if (!MovesAcross(candidate,axis)) break;
                    if (FitExtended(candidate,left,right,leftSection,rightSection,width,out result)) return true;
                    if (lengthRatio == reach) break;
                }
                return false;
            }
            var start = line.a - chord * reach;
            var end = line.d + chord * reach;
            return FitExtended(new Bezier4x3(start, math.lerp(start,end,1f/3f),
                math.lerp(start,end,2f/3f), end), left, right, leftSection, rightSection, width, out result);
        }

        private static bool MovesAcross(Bezier4x3 line, float2 axis)
            => math.dot((line.b-line.a).xz,axis) > 0f &&
                math.dot((line.c-line.b).xz,axis) > 0f &&
                math.dot((line.d-line.c).xz,axis) > 0f;

        private static bool FitExtended(Bezier4x3 extended, Bezier4x3 left, Bezier4x3 right,
            Bezier4x3 leftSection, Bezier4x3 rightSection, float width, out Bezier4x3 result)
        {
            result = default;
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
            bool fittedToCurbs = true, List<Bezier4x3> curbBoundaries = null, float unjoinedCurbSetback = 0,
            float railWidth = 0, List<Bezier4x3> emittedCurbEdges = null, float curbLineOffset = .3f,
            bool requireCurbEvidence = false)
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
                    // A fitted tip is already at a physical curb or median. Only
                    // nearby paint can replace that tip. An inside turn stripe
                    // can cross the same probe far from either boundary.
                    float inwardLimit = curbLineOffset + .35f;
                    if (fittedToCurbs && at > 0 &&
                        math.distancesq(point.xz, oriented.a.xz) > inwardLimit * inwardLimit) continue;
                    if (!fittedToCurbs && requireCurbEvidence &&
                        ((at > 0 && math.distancesq(point.xz, oriented.a.xz) > inwardLimit * inwardLimit)
                            || !NearCurb(point, inwardLimit))) continue;
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
                    float nearest = float.PositiveInfinity;
                    foreach (var edge in emittedCurbEdges)
                    {
                        if (!EdgeHit(cap, edge, out float t) &&
                            !TouchesStripeCap(cap, edge, railWidth, out t)) continue;
                        float at = math.lerp(-capReach, reach, t);
                        if (math.abs(at) >= math.abs(nearest)) continue;
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
            bool NearCurb(float3 point, float limit)
            {
                if (curbBoundaries != null)
                    foreach (var curb in curbBoundaries)
                        if (PreviewPaintGeometry.Valid(curb) &&
                            MathUtils.Distance(curb.xz, point.xz, out float t) <= limit &&
                            math.abs(MathUtils.Position(curb,t).y - point.y) <= .3f) return true;
                return false;
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

        internal static bool TryMergeMedianSpans(float2 first, float2 second, out float2 span)
        {
            span = new float2(math.min(first.x, second.x), math.max(first.y, second.y));
            return first.x <= second.y + .05f && second.x <= first.y + .05f;
        }

        internal static bool CrossesBothBoundaries(Bezier4x3 rail, Bezier4x3 left, Bezier4x3 right)
            => EdgeHit(rail,left,out float a) && EdgeHit(rail,right,out float b) && math.abs(a-b) > .005f;

        internal static bool BridgeMatchesOpening(Bezier4x3 bridge, Bezier4x3 left, Bezier4x3 right, float width)
        {
            var middle = MathUtils.Position(bridge, .5f);
            MathUtils.Distance(left.xz, middle.xz, out float a);
            MathUtils.Distance(right.xz, middle.xz, out float b);
            var l = MathUtils.Position(left, a);
            var r = MathUtils.Position(right, b);
            if (math.abs(middle.y - (l.y + r.y) * .5f) > .3f) return false;
            var axis = math.normalizesafe(r.xz - l.xz);
            float span = math.distance(l.xz, r.xz);
            if (!math.isfinite(span) || span < .05f || span > width * 1.75f + 1f) return false;
            float first = math.dot(bridge.a.xz - l.xz, axis);
            float last = math.dot(bridge.d.xz - l.xz, axis);
            // Both bridge ends must belong to this opening, in either road
            // orientation. A bridge across several islands cannot claim one.
            float reach = math.min(.65f, span * .4f);
            return math.abs(math.min(first,last)) <= reach &&
                math.abs(math.max(first,last) - span) <= reach;
        }

        internal static bool BracketsMedianOpening(float2 median, float3 leftOuter, float3 leftInner,
            float3 rightInner, float3 rightOuter, float rowTolerance = .12f)
        {
            // Some divided roads author two crossing spans with no center span.
            // Require the inside tips to bracket the same median on one row and deck.
            return math.all(math.isfinite(leftOuter)) && math.all(math.isfinite(leftInner))
                && math.all(math.isfinite(rightInner)) && math.all(math.isfinite(rightOuter))
                && leftOuter.x < median.x - .65f && rightOuter.x > median.y + .65f
                && rightInner.x - leftInner.x >= .08f
                && math.abs(leftInner.x - median.x) <= .45f && math.abs(rightInner.x - median.y) <= .45f
                && math.abs(leftInner.z - rightInner.z) <= rowTolerance
                && math.abs(leftInner.y - rightInner.y) <= .05f
                && math.abs(leftOuter.y - leftInner.y) <= .08f
                && math.abs(rightOuter.y - rightInner.y) <= .08f;
        }

        internal static bool TryBridgeRails(Bezier4x3 first, Bezier4x3 second, Bezier4x3 otherFirst, Bezier4x3 otherSecond,
            out Bezier4x3 bridgeA, out Bezier4x3 bridgeB, float maximumSpan = 5f, float rowTolerance = .2f)
        {
            if (TryBridgeRail(first, otherFirst, maximumSpan, rowTolerance, out bridgeA) && TryBridgeRail(second, otherSecond, maximumSpan, rowTolerance, out bridgeB))
                return true;
            bridgeB = default;
            return TryBridgeRail(first, otherSecond, maximumSpan, rowTolerance, out bridgeA) && TryBridgeRail(second, otherFirst, maximumSpan, rowTolerance, out bridgeB);
        }

        private static bool TryBridgeRail(Bezier4x3 first, Bezier4x3 last, float maximumSpan, float rowTolerance, out Bezier4x3 bridge)
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
            float alignment = rowTolerance > .2f ? .9f : .98f;
            if (math.dot(atStart, atEnd) < .98f || math.dot(atStart, axis) < alignment || math.dot(atEnd, axis) < alignment ||
                math.length(delta.xz - atStart.xz * math.dot(delta.xz, atStart.xz)) > rowTolerance ||
                math.length(delta.xz - atEnd.xz * math.dot(delta.xz, atEnd.xz)) > rowTolerance) return false;
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

        private static bool TouchesStripeCap(Bezier4x3 rail, Bezier4x3 edge, float width, out float t)
        {
            t = 0;
            if (!PreviewPaintGeometry.Valid(edge)) return false;
            float best = math.min(.07f, width * .5f - .005f);
            bool found = false;
            for (int end = 0; end <= 1; end++)
            {
                var endpoint = end == 0 ? edge.a : edge.d;
                float distance = MathUtils.Distance(rail.xz, endpoint.xz, out float at);
                if (distance >= best || at <= .001f || at >= .999f) continue;
                if (math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(rail,at).xz),
                    math.normalizesafe(MathUtils.Tangent(edge,end).xz))) >= .95f) continue;
                if (math.abs(MathUtils.Position(rail,at).y - endpoint.y) > .12f) continue;
                // The end of this already-emitted stripe falls inside the
                // painted width of the rail, even without a centerline hit.
                best = distance; t = at; found = true;
            }
            return found;
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
