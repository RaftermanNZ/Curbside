using Colossal.Mathematics;

using Game.Net;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class CrosswalkPlacement

    {

        // If an approach's curb metadata is unavailable, stay inside the native
        // crossing's swept paint band rather than waiting for an unrelated road edit.
        internal static bool TryNativeFootprint(Bezier4x3 source, float nativeWidth, float requestedWidth,
            float lineWidth, out Bezier4x3 first, out Bezier4x3 second)
        {
            first = second = default;
            const float clearance = .25f;
            if (!math.isfinite(nativeWidth) || !math.isfinite(requestedWidth) || !math.isfinite(lineWidth)
                || lineWidth <= 0 || requestedWidth <= lineWidth || nativeWidth <= lineWidth + clearance * 2)
                return false;
            float width = math.min(requestedWidth, nativeWidth - clearance * 2);
            if (!TryLines(source, width, lineWidth, out var a, out var b)) return false;
            bool TrimAndCheck(Bezier4x3 line, out Bezier4x3 trimmed)
            {
                trimmed = default;
                float length = MathUtils.Length(line);
                if (!math.isfinite(length) || length <= clearance * 2 + .1f) return false;
                // Endcaps are inset as well; never extend beyond the original crossing.
                float lower = clearance / length, upper = 1 - lower;
                var candidate = MathUtils.Cut(line, new float2(lower, upper));
                float previous = -1;
                for (int i = 0; i <= 32; i++)
                {
                    var p = MathUtils.Position(candidate, i / 32f);
                    if (!math.all(math.isfinite(p))) return false;
                    float distance = MathUtils.Distance(source, p, out float t);
                    if (!math.isfinite(distance) || distance + lineWidth * .5f + .1f > nativeWidth * .5f - .1f
                        || t < previous - .002f) return false;
                    previous = t;
                }
                trimmed = candidate; return true;
            }
            if (!TrimAndCheck(a, out var clippedA) || !TrimAndCheck(b, out var clippedB)) return false;
            first = clippedA; second = clippedB; return true;
        }

        internal static bool TryApproachTrim(Bezier4x3 line, Bezier4x3 left, Bezier4x3 right, float paintWidth, out Bezier4x3 clipped)

        {

            clipped = default; if (!math.isfinite(paintWidth) || paintWidth <= 0f) return false;

            var lp = MathUtils.Position(left, .5f).xz; var rp = MathUtils.Position(right, .5f).xz;

            var ld = math.normalizesafe((left.d - left.a).xz); var rd = math.normalizesafe((right.d - right.a).xz);

            float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

            float ls = math.sign(Cross(ld, (lp + rp) * .5f - lp)), rs = math.sign(Cross(rd, (lp + rp) * .5f - rp));

            if (ls == 0f || rs == 0f) return false;

            var probes = new[] { NetUtils.OffsetCurveLeftSmooth(line, -paintWidth * .5f - .1f), line, NetUtils.OffsetCurveLeftSmooth(line, paintWidth * .5f + .1f) };

            bool Inside(float t) { foreach (var probe in probes) { var point = MathUtils.Position(probe, t).xz; if (!math.all(math.isfinite(point)) || Cross(ld, point - lp) * ls < .25f || Cross(rd, point - rp) * rs < .25f) return false; } return true; }

            int first = -1, last = -1; bool gap = false;

            for (int i = 0; i <= 64; i++) { if (Inside(i / 64f)) { if (gap) return false; if (first < 0) first = i; last = i; } else if (first >= 0) gap = true; }

            if (first < 0) return false; float lower = first / 64f, upper = last / 64f;

            if (first > 0) { float outside = (first - 1) / 64f; for (int i = 0; i < 16; i++) { float mid = (outside + lower) * .5f; if (Inside(mid)) lower = mid; else outside = mid; } }

            if (last < 64) { float outside = (last + 1) / 64f; for (int i = 0; i < 16; i++) { float mid = (upper + outside) * .5f; if (Inside(mid)) upper = mid; else outside = mid; } }

            if (upper <= lower) return false; clipped = MathUtils.Cut(line, new float2(lower, upper)); return MathUtils.Length(clipped) > .1f;

        }

        // Finite curve intersections follow the rendered node instead of infinite tangent planes.

        internal static bool TryCurvedTrim(Bezier4x3 line, Bezier4x3 left, Bezier4x3 right, float paintWidth, out Bezier4x3 clipped)

        {

            clipped = default; float lo = 0f, hi = 1f;

            if (!math.isfinite(paintWidth) || paintWidth <= 0f) return false;

            var leftSamples = Sample(left); var rightSamples = Sample(right);

            // Include both sides of the paint footprint, plus the native smoothing margin.

            for (int side = -1; side <= 1; side++)

            {

                var probe = NetUtils.OffsetCurveLeftSmooth(line, side * (paintWidth * .5f + .1f));

                if (!FiniteIntersection(probe, leftSamples, out float a) || !FiniteIntersection(probe, rightSamples, out float b)) return false;

                lo = math.max(lo, math.min(a, b)); hi = math.min(hi, math.max(a, b));

            }

            float margin = .25f / math.max(.1f, MathUtils.Length(line)); lo += margin; hi -= margin;

            if (hi <= lo) return false; clipped = MathUtils.Cut(line, new float2(lo, hi)); return math.all(math.isfinite(clipped.a)) && math.all(math.isfinite(clipped.d));

        }

        private static float2[] Sample(Bezier4x3 curve) { var points = new float2[65]; for (int i = 0; i <= 64; i++) points[i] = MathUtils.Position(curve, i / 64f).xz; return points; }

        private static bool FiniteIntersection(Bezier4x3 line, float2[] curb, out float t)

        {

            t = 0f; float best = float.MaxValue; bool found = false;

            for (int i = 0; i < 32; i++)

            {

                var a = MathUtils.Position(line, i / 32f).xz; var b = MathUtils.Position(line, (i + 1) / 32f).xz; var r = b - a;

                for (int j = 0; j < 64; j++)

                {

                    var c = curb[j]; var d = curb[j + 1]; var v = d - c;

                    float den = r.x * v.y - r.y * v.x; if (math.abs(den) < 1e-6f) continue;

                    var q = c - a; float u = (q.x * v.y - q.y * v.x) / den, w = (q.x * r.y - q.y * r.x) / den;

                    if (u < 0f || u > 1f || w < 0f || w > 1f) continue;

                    float candidate = (i + u) / 32f; float distance = math.abs(candidate - .5f);

                    if (distance < best) { best = distance; t = candidate; found = true; }

                }

            }

            return found;

        }

        internal static bool TryPavementTrim(Bezier4x3 line, Bezier4x3 left, Bezier4x3 right, float clearance, out Bezier4x3 clipped)

        {

            clipped = default(Bezier4x3);

            if (!math.isfinite(clearance) || clearance < 0f || !StopLinePlacement.TryIntersect(line, left, out float a, out var ah) || !StopLinePlacement.TryIntersect(line, right, out float b, out var bh)) return false;

            float length = MathUtils.Length(line); if (!math.isfinite(length) || length < .1f) return false;

            float lower = math.min(a, b) + clearance / length, upper = math.max(a, b) - clearance / length;

            if (upper <= lower) return false; clipped = MathUtils.Cut(line, new float2(lower, upper));

            return math.all(math.isfinite(clipped.a)) && math.all(math.isfinite(clipped.d));

        }

        // Only the junction-facing rail joins the edge guides. The other rail is
        // unchanged. Bound extensions to avoid reaching across islands or side roads.
        internal static bool TryJoinEdgeEnds(Bezier4x3 line,System.Collections.Generic.IList<Bezier4x3> edges,out Bezier4x3 joined)
        {
            joined=line;float length=MathUtils.Length(line);if(!math.isfinite(length)||length<.5f||edges==null||edges.Count==0)return false;
            float pad=2f/length;var extended=MathUtils.Cut(line,new float2(-pad,1+pad));
            float first=-1,last=-1,firstScore=2.01f,lastScore=2.01f;
            foreach(var edge in edges)
            {
                if(!FiniteIntersection(extended,Sample(edge),out float t))continue;
                var point=MathUtils.Position(extended,t);MathUtils.Distance(edge,point,out float edgeAt);if(math.abs(MathUtils.Position(edge,edgeAt).y-point.y)>.3f)continue;float da=math.distance(point.xz,line.a.xz),dd=math.distance(point.xz,line.d.xz);
                if(t<.5f&&da<firstScore&&math.abs(point.y-line.a.y)<.3f){first=t;firstScore=da;}
                if(t>.5f&&dd<lastScore&&math.abs(point.y-line.d.y)<.3f){last=t;lastScore=dd;}
            }
            if(first<0||last<=first)return false;
            var candidate=MathUtils.Cut(extended,new float2(first,last));
            if(!math.all(math.isfinite(candidate.a))||!math.all(math.isfinite(candidate.d))||MathUtils.Length(candidate)<.5f)return false;
            joined=candidate;return true;
        }

        internal static bool TryLines(Bezier4x3 curve, float crossingWidth, float lineWidth, out Bezier4x3 first, out Bezier4x3 second)

        {

            first = second = default(Bezier4x3);

            if (!math.isfinite(crossingWidth) || !math.isfinite(lineWidth) || lineWidth <= 0f || crossingWidth <= lineWidth

             || !math.all(math.isfinite(curve.a)) || !math.all(math.isfinite(curve.b)) || !math.all(math.isfinite(curve.c)) || !math.all(math.isfinite(curve.d)) || MathUtils.Length(curve) < 0.5f) return false;

            float offset = (crossingWidth - lineWidth) * 0.5f;

            first = NetUtils.OffsetCurveLeftSmooth(curve, offset); second = NetUtils.OffsetCurveLeftSmooth(curve, -offset);

            return math.all(math.isfinite(first.a)) && math.all(math.isfinite(first.d)) && math.all(math.isfinite(second.a)) && math.all(math.isfinite(second.d));

        }

    }

}

