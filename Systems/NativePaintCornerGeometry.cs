using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class NativePaintCornerGeometry
    {
        internal static void MatchMouths(float3 leftMouth, float3 rightMouth, Bezier4x3 a, Bezier4x3 b,
            out Bezier4x3 left, out Bezier4x3 right)
        {
            float direct = math.distancesq(leftMouth, a.a) + math.distancesq(rightMouth, b.a);
            float swapped = math.distancesq(leftMouth, b.a) + math.distancesq(rightMouth, a.a);
            left = direct <= swapped ? a : b;
            right = direct <= swapped ? b : a;
        }

        internal static void MatchAvailableMouths(float3 leftMouth, float3 rightMouth,
            ref Bezier4x3 left, ref Bezier4x3 right)
        {
            bool hasLeft = PreviewPaintGeometry.Valid(left);
            bool hasRight = PreviewPaintGeometry.Valid(right);
            if (hasLeft && hasRight)
            {
                MatchMouths(leftMouth, rightMouth, left, right, out left, out right);
                return;
            }
            if ((hasLeft && math.distancesq(left.a, rightMouth) < math.distancesq(left.a, leftMouth))
                || (hasRight && math.distancesq(right.a, leftMouth) < math.distancesq(right.a, rightMouth)))
                (left, right) = (right, left);
        }

        internal static bool TryOffsetTowardRoad(Bezier4x3 curb, float3 oppositeMouth, float distance,
            out Bezier4x3 paint)
        {
            paint = default;
            float side = CurbSide(curb, oppositeMouth);
            if (side == 0 || distance < 0) return false;
            // Keep the clearance in world metres even where the native patch narrows.
            paint = Game.Net.NetUtils.OffsetCurveLeftSmooth(curb, side * distance);
            return PreviewPaintGeometry.Valid(paint) && MathUtils.Length(paint) > .05f;
        }

        internal static bool TryCornerPaint(Bezier4x3 a, Bezier4x3 b, float3 acrossA, float3 acrossB,
            float clearance, out Bezier4x3 first, out Bezier4x3 last)
        {
            last = default;
            if (!TryOffsetTowardRoad(a, acrossA, clearance, out first) ||
                !TryOffsetTowardRoad(b, acrossB, clearance, out last)) return false;
            var gap = last.d - first.d;
            if (math.lengthsq(gap) <= .01f) return true;

            // Neighboring sidewalk compositions can end at different lateral positions
            // on the same seam. Keep the farther road-side stripe and ease the other
            // half outward to meet it. Neither road-mouth cap moves.
            var inwardA = math.normalizesafe(first.d.xz - a.d.xz);
            var inwardB = math.normalizesafe(last.d.xz - b.d.xz);
            var inward = math.normalizesafe(inwardA + inwardB);
            float lateral = math.dot(gap.xz, inward);
            if (math.dot(inwardA, inwardB) < .8f || math.abs(gap.y) > .15f ||
                math.length(gap.xz) > 2f || math.abs(lateral) < .85f * math.length(gap.xz) ||
                math.dot(math.normalizesafe(MathUtils.EndTangent(a).xz),
                    -math.normalizesafe(MathUtils.EndTangent(b).xz)) < .8f) return false;
            if (lateral > 0) { first.c += gap; first.d = last.d; }
            else { last.c -= gap; last.d = first.d; }
            return true;
        }

        internal static bool TryJoinAlongCurb(Bezier4x3 curbA, Bezier4x3 curbB, float3 acrossA, float3 acrossB,
            float clearance, float stripeRadius, Bezier4x3 sectionA, Bezier4x3 sectionB,
            float3 capA, float3 directionA, float3 capB, float3 directionB,
            out Bezier4x3 first, out Bezier4x3 second, out Bezier4x3? leadA, out Bezier4x3? leadB)
        {
            first = second = default;
            leadA = leadB = null;
            if (!TryCornerPaint(curbA, curbB, acrossA, acrossB, clearance, out var paintA, out var paintB)
                || !FitRoadMouth(curbA, sectionA, capA, directionA, stripeRadius, ref paintA)
                || !FitRoadMouth(curbB, sectionB, capB, directionB, stripeRadius, ref paintB)) return false;

            if (!TryJoin(paintA, paintB, sectionA, sectionB, capA, directionA, capB, directionB,
                out first, out second, out leadA, out leadB)) return false;
            SmoothSeam(curbA, curbB, acrossA, acrossB, math.min(clearance - .015f, stripeRadius + .01f),
                ref first, ref second);
            return true;
        }

        internal static bool TryAttachCurbLeg(Bezier4x3 curb, float3 across, float setback,
            float stripeRadius, Bezier4x3 section, float3 cap, float3 direction,
            out Bezier4x3 paint, out Bezier4x3? lead)
        {
            lead = null;
            paint = default;
            if (!PreviewPaintGeometry.Valid(curb) || !TryOffsetTowardRoad(curb, across, setback, out paint)
                || !FitRoadMouth(curb, section, cap, direction, stripeRadius, ref paint)) return false;

            var minimum = math.max(0, math.min(setback - .015f, stripeRadius + .01f));
            return Attach(paint, section, cap, direction, out paint, out lead)
                && KeepsClearance(paint, curb, CurbSide(curb, across), minimum);
        }

        internal static bool NativeSeamMatches(Bezier4x3 curbA, Bezier4x3 curbB)
            => math.distancesq(curbA.d.xz, curbB.d.xz) <= .0225f
                && math.abs(curbA.d.y - curbB.d.y) <= .1f;

        internal static bool TryRenderedCurb(Bezier4x3 left, Bezier4x3 right, float fraction,
            float4 source, float4 target, out Bezier4x3 curb)
        {
            curb = default;
            if (!math.isfinite(fraction) || !math.all(math.isfinite(source)) || !math.all(math.isfinite(target))
                || fraction < 0 || fraction > 1) return false;

            float mouthWidth = math.distance(left.a.xz, right.a.xz);
            float seamWidth = math.distance(left.d.xz, right.d.xz);
            if (mouthWidth < .01f || seamWidth < .01f) return false;

            // Work locally so finite differences do not lose precision at world coordinates.
            var origin = left.a;
            left = new Bezier4x3(left.a - origin, left.b - origin, left.c - origin, left.d - origin);
            right = new Bezier4x3(right.a - origin, right.b - origin, right.c - origin, right.d - origin);
            var start = Position(0);
            var end = Position(1);
            const float step = .001f;
            var b = start + (Position(step) - start) / (3 * step);
            var c = end + (Position(1 - step) - end) / (3 * step);
            var candidate = new Bezier4x3(start, b, c, end);
            for (int i = 1; i < 16; i++)
            {
                float t = i / 16f;
                if (math.distancesq(MathUtils.Position(candidate, t), Position(t)) > .0016f) return false;
            }
            curb = new Bezier4x3(start + origin, b + origin, c + origin, end + origin);
            return PreviewPaintGeometry.Valid(curb);

            float3 Position(float t)
            {
                float remapped = RenderedLateralFraction(fraction, t, mouthWidth, seamWidth, source, target);
                return math.lerp(MathUtils.Position(left, t), MathUtils.Position(right, t), remapped);
            }
        }

        // The standard node mesh appends a half-length patch. Its shader uses
        // twice smoothstep(t/2), then blends sync offsets weighted by patch width.
        internal static float RenderedLateralFraction(float fraction, float t, float mouthWidth,
            float seamWidth, float4 source, float4 target)
        {
            float blend = 2f * math.smoothstep(0f, 1f, t * .5f);
            var offsets = math.lerp(source * mouthWidth, target * seamWidth, blend)
                / math.max(.000001f, math.lerp(mouthWidth, seamWidth, blend));
            const float epsilon = .000001f;
            float second = math.min(source.y, source.z);
            float first = math.min(source.x, second - epsilon);
            float third = math.max(source.z, second + epsilon);
            float fourth = math.max(source.w, third + epsilon);
            float lower = math.min(0f, first - epsilon);
            float upper = math.max(1f, fourth + epsilon);
            return Ramp(lower, first) * offsets.x
                + Ramp(first, second) * (offsets.y - offsets.x)
                + Ramp(second, third) * (offsets.z - offsets.y)
                + Ramp(third, fourth) * (offsets.w - offsets.z)
                + Ramp(fourth, upper) * (1f - offsets.w);

            float Ramp(float start, float end) => math.saturate((fraction - start) / (end - start));
        }

        internal static bool TryContinueMixedCurb(Bezier4x3 a, Bezier4x3 b,
            out Bezier4x3 first, out Bezier4x3 last)
        {
            first = last = default;
            if (!PreviewPaintGeometry.Valid(a) || !PreviewPaintGeometry.Valid(b) || NativeSeamMatches(a, b)) return false;

            bool tracesA = TryTraceToMouth(a, b, out var fromA, out float errorA);
            bool tracesB = TryTraceToMouth(b, a, out var fromB, out float errorB);
            if (!tracesA && !tracesB) return false;

            // Prefer the curve whose continuation best predicts the independently
            // known position and direction of the other road mouth.
            if (tracesA && (!tracesB || errorA <= errorB))
            {
                first = a;
                last = fromA;
            }
            else
            {
                first = fromB;
                last = b;
            }
            return true;
        }

        private static bool TryTraceToMouth(Bezier4x3 source, Bezier4x3 other, out Bezier4x3 replacement, out float error)
        {
            replacement = default;
            error = float.PositiveInfinity;
            var mouthDirection = HorizontalTangent(MathUtils.StartTangent(other));
            if (math.lengthsq(mouthDirection.xz) < .99f) return false;

            // Find where the curb would reach the opposite road's cross section.
            // Limit extrapolation to one more patch length.
            float low = 1f, high = 2f;
            if (Station(low) <= 0 || Station(high) > 0) return false;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * .5f;
                if (Station(middle) > 0) low = middle;
                else high = middle;
            }

            float at = (low + high) * .5f;
            var projected = MathUtils.Position(source, at);
            var projectedDirection = HorizontalTangent(-MathUtils.Tangent(source, at));
            float difference = math.distance(projected.xz, other.a.xz);
            // A guide which cannot independently locate the mouth is not
            // evidence for continuing the curb across the second patch.
            float alignment = math.dot(projectedDirection.xz, mouthDirection.xz);
            if (difference > .15f || math.abs(projected.y - other.a.y) > .12f || alignment < .9f) return false;

            var seamDirection = HorizontalTangent(-MathUtils.EndTangent(source));
            var chord = (source.d - other.a).xz;
            float determinant = SignedNormal(mouthDirection.xz, seamDirection.xz);
            if (math.abs(determinant) < .05f) return false;
            float mouthLimit = SignedNormal(chord, seamDirection.xz) / determinant;
            float seamLimit = SignedNormal(mouthDirection.xz, chord) / determinant;
            if (math.min(mouthLimit, seamLimit) <= .08f) return false;

            var reference = MathUtils.Cut(source, new float2(1f, at));
            float best = float.PositiveInfinity;
            for (int mouth = 1; mouth <= 7; mouth++)
            for (int seam = 1; seam <= 7; seam++)
            {
                var candidate = new Bezier4x3(other.a, other.a + mouthDirection * (mouthLimit * mouth * .125f),
                    source.d - seamDirection * (seamLimit * seam * .125f), source.d);
                if (!TurnsOneWay(candidate, math.sign(determinant))) continue;
                float score = 0;
                for (int i = 1; i < 12; i++)
                {
                    float offset = MathUtils.Distance(reference.xz, MathUtils.Position(candidate, i / 12f).xz, out _);
                    score += offset * offset;
                }
                if (score >= best) continue;
                best = score;
                replacement = candidate;
            }
            if (!math.isfinite(best) || best / 11 > .01f) return false;
            error = difference + (1f - alignment) + math.sqrt(best / 11);
            return true;

            float Station(float t) => math.dot(MathUtils.Position(source, t).xz - other.a.xz, mouthDirection.xz);
        }

        internal static bool TryMixedCurbCorner(Bezier4x3 curbA, Bezier4x3 curbB, float3 acrossA, float3 acrossB,
            float setback, Bezier4x3 firstLeg, Bezier4x3 lastLeg, out Bezier4x3 curb, out Bezier4x3 paint)
        {
            curb = paint = default;
            if (!PreviewPaintGeometry.Valid(curbA) || !PreviewPaintGeometry.Valid(curbB)) return false;
            var start = HorizontalTangent(MathUtils.StartTangent(curbA));
            var end = HorizontalTangent(-MathUtils.StartTangent(curbB));
            float side = CurbSide(curbA, acrossA);
            if (side == 0 || side != -CurbSide(curbB, acrossB)
                || math.lengthsq(start.xz) < .99f || math.lengthsq(end.xz) < .99f) return false;

            // GeometrySystem builds a whole corner from the neighboring road mouths
            // before it splits the mesh into patches and synchronizes their vertices.
            // Reconstruct that whole turn at the curb's cross section.
            curb = Game.Net.NetUtils.FitCurve(curbA.a, start, end, curbB.a);
            if (!PreviewPaintGeometry.Valid(curb)) return false;
            paint = Game.Net.NetUtils.OffsetCurveLeftSmooth(curb, side * setback);

            // Keep the existing section paint connections. The allowed lateral
            // adjustment is limited to the difference between a section offset
            // and a normal offset at its skewed mouth.
            var deltaA = firstLeg.a - paint.a;
            var deltaB = lastLeg.a - paint.d;
            if (math.lengthsq(deltaA) > .04f || math.lengthsq(deltaB) > .04f) return false;

            var firstTangent = HorizontalTangent(MathUtils.StartTangent(firstLeg));
            var lastTangent = HorizontalTangent(-MathUtils.StartTangent(lastLeg));
            if (math.dot(start.xz, firstTangent.xz) < .95f || math.dot(end.xz, lastTangent.xz) < .95f)
                return false;

            float firstHandle = math.distance(paint.a.xz, paint.b.xz);
            float lastHandle = math.distance(paint.c.xz, paint.d.xz);
            paint = new Bezier4x3(firstLeg.a, firstLeg.a + firstTangent * firstHandle,
                lastLeg.a - lastTangent * lastHandle, lastLeg.a);
            float direction = math.sign(SignedNormal(start.xz, end.xz));
            // Nearly parallel approaches can have a genuine lateral transition.
            // A gentle inflection keeps both mouth tangents, whereas a chord
            // shortcuts the transition and leaves an angle at each end.
            if (direction != 0 && !TurnsOneWay(paint, direction) && !IsGentleThroughCurve(paint, start.xz, end.xz))
                return false;

            // Verify the fitted stripe against the continuous curb reference.
            // A few centimetres of tolerance cover the cubic offset fit.
            float minimum = math.max(0f, setback - .045f);
            for (int i = 0; i <= 32; i++)
            {
                var sample = MathUtils.Position(paint, i / 32f).xz;
                MathUtils.Distance(curb.xz, sample, out float at);
                var tangent = math.normalizesafe(MathUtils.Tangent(curb, at).xz);
                float distance = SignedNormal(tangent, sample - MathUtils.Position(curb, at).xz) * side;
                if (!math.isfinite(distance) || distance < minimum || distance > setback + .045f) return false;
            }
            return true;
        }

        private static bool IsGentleThroughCurve(Bezier4x3 paint, float2 start, float2 end)
        {
            if (math.dot(start, end) < .98f) return false;
            var axis = math.normalizesafe((paint.d - paint.a).xz);
            // Every derivative is a weighted combination of these three
            // edges, so the whole curve advances in the forward direction.
            if (math.dot(axis, (paint.b - paint.a).xz) <= 0
                || math.dot(axis, (paint.c - paint.b).xz) <= 0
                || math.dot(axis, (paint.d - paint.c).xz) <= 0) return false;
            for (int i = 0; i <= 16; i++)
                if (math.dot(axis, math.normalizesafe(MathUtils.Tangent(paint, i / 16f).xz)) < .94f)
                    return false;
            return true;
        }

        private static float3 HorizontalTangent(float3 direction)
        {
            var scale = math.length(direction.xz);
            if (scale <= .0001f || !math.isfinite(scale)) return default;
            direction /= scale;
            direction.y = math.clamp(direction.y, -1f, 1f);
            return direction;
        }

        internal static bool TurnsOneWay(Bezier4x3 path, float direction, float tolerance = .000001f)
        {
            if (!PreviewPaintGeometry.Valid(path) || direction == 0) return false;
            var a = (path.b - path.a).xz;
            var b = (path.c - path.b).xz;
            var c = (path.d - path.c).xz;
            // These are the Bernstein coefficients for the signed curvature
            // numerator. A shared sign rules out an inflection anywhere.
            return SignedNormal(a, b) * direction >= -tolerance
                && SignedNormal(a, c) * direction > 0
                && SignedNormal(b, c) * direction >= -tolerance;
        }

        internal static bool TryBalanceLeadIn(Bezier4x3 lead, Bezier4x3 turn, Bezier4x3 next,
            Bezier4x3 curb, Bezier4x3 nextCurb, float3 across, float3 nextAcross,
            float setback, float minimum, float curbSeamCurvature, out Bezier4x3 entry, out Bezier4x3 exit)
        {
            entry = exit = default;
            if (!PreviewPaintGeometry.Valid(lead) || !PreviewPaintGeometry.Valid(turn) || !PreviewPaintGeometry.Valid(next)
                || !PreviewPaintGeometry.Valid(curb) || !PreviewPaintGeometry.Valid(nextCurb)
                || math.distancesq(lead.d, turn.a) > .000025f || math.distancesq(turn.d, next.a) > .000025f)
                return false;
            float peakBefore = math.abs(StartCurvature(turn));
            float side = CurbSide(curb, across);
            float nextSide = CurbSide(nextCurb, nextAcross);
            float denominator = 1f - side * setback * curbSeamCurvature;
            if (peakBefore < .3f || side == 0 || nextSide != -side || denominator < .5f) return false;
            float target = curbSeamCurvature / denominator;
            float beforeSeam = StartCurvature(next);
            if (target * beforeSeam <= 0 || math.abs(target) <= math.abs(beforeSeam) + .025f) return false;

            var start = HorizontalTangent(MathUtils.StartTangent(lead));
            var middle = HorizontalTangent(MathUtils.EndTangent(turn));
            var nextStart = HorizontalTangent(MathUtils.StartTangent(next));
            var straight = math.normalizesafe((lead.d - lead.a).xz);
            if (math.dot(start.xz, straight) < .999f
                || math.dot(start.xz, HorizontalTangent(MathUtils.EndTangent(lead)).xz) < .999f
                || math.dot(start.xz, HorizontalTangent(MathUtils.StartTangent(turn)).xz) < .999f
                || math.dot(middle.xz, nextStart.xz) < .999f) return false;

            // Use the straight lead as part of the blend. A short easing
            // segment at its old end can move the curvature spike without
            // reducing it. Both new halves have the same seam curvature.
            var chord = (turn.d - lead.a).xz;
            float determinant = SignedNormal(start.xz, middle.xz);
            if (math.abs(determinant) < .1f || determinant * target <= 0) return false;
            float intersection = SignedNormal(chord, middle.xz) / determinant;
            float middleHandle = SignedNormal(start.xz, chord) / determinant;
            if (intersection <= .05f || middleHandle <= .05f) return false;
            float entryHandle = (1.5f * target * middleHandle * middleHandle
                - SignedNormal(middle.xz, -chord)) / -determinant;
            if (entryHandle <= .05f || entryHandle >= intersection) return false;

            var a = new Bezier4x3(lead.a, lead.a + start * entryHandle,
                turn.d - middle * middleHandle, turn.d);
            var b = next;
            b.b = b.a + (b.b - b.a) * math.sqrt(beforeSeam / target);
            float turnDirection = math.sign(target);
            // Rounding the collinear mouth handles back to world coordinates
            // can change the sign of an otherwise zero coefficient.
            if (!TurnsOneWay(a, turnDirection, .001f) || !TurnsOneWay(b, turnDirection)
                || !KeepsClearance(a, curb, side, minimum) || !KeepsClearance(b, nextCurb, nextSide, minimum))
                return false;
            for (int i = 0; i <= 32; i++)
            {
                float t = i / 32f;
                var position = MathUtils.Position(a, t).xz;
                float displacement = math.min(MathUtils.Distance(lead.xz, position, out _),
                    MathUtils.Distance(turn.xz, position, out _));
                float curvature = Curvature(a, t);
                if (displacement > .2f || curvature * turnDirection < -.005f || math.abs(curvature) > peakBefore * .7f
                    || MathUtils.Distance(next.xz, MathUtils.Position(b, t).xz, out _) > .1f) return false;
            }

            entry = a;
            exit = b;
            return true;
        }

        internal static float Curvature(Bezier4x3 curve, float t)
        {
            var a = (curve.b - curve.a).xz;
            var b = (curve.c - curve.b).xz;
            var c = (curve.d - curve.c).xz;
            float s = 1f - t;
            var velocity = 3f * (a * (s * s) + b * (2f * s * t) + c * (t * t));
            var acceleration = 6f * ((b - a) * s + (c - b) * t);
            float speed = math.length(velocity);
            return speed > .0001f ? SignedNormal(velocity, acceleration) / (speed * speed * speed) : 0f;
        }

        internal static bool TryEaseRoadMouth(Bezier4x3 paint, Bezier4x3 curb, float3 across, float minimum,
            out Bezier4x3 mouth, out Bezier4x3 remainder)
        {
            mouth = remainder = default;
            if (!PreviewPaintGeometry.Valid(paint) || !PreviewPaintGeometry.Valid(curb)
                || math.abs(StartCurvature(paint)) < .3f) return false;
            float side = CurbSide(curb, across);
            if (side == 0) return false;

            // Preserve the turn and its existing middle seam. Change just the
            // first eighth, matching the road's straight tangent and the
            // retained curve's tangent and curvature.
            MathUtils.Divide(paint, out var original, out var rest, .125f);
            var start = HorizontalTangent(MathUtils.StartTangent(original));
            var end = HorizontalTangent(MathUtils.EndTangent(original));
            var chord = (original.d - original.a).xz;
            float determinant = SignedNormal(start.xz, end.xz);
            if (math.abs(determinant) < .02f) return false;
            float toIntersection = SignedNormal(chord, end.xz) / determinant;
            float endHandle = SignedNormal(start.xz, chord) / determinant;
            if (toIntersection <= .04f || endHandle <= .04f) return false;
            float want = StartCurvature(rest);
            float startHandle = (1.5f * want * endHandle * endHandle
                - SignedNormal(end.xz, -chord)) / -determinant;
            if (startHandle <= .015f || startHandle >= toIntersection) return false;

            var candidate = new Bezier4x3(original.a, original.a + start * startHandle,
                original.d - end * endHandle, original.d);
            if (!TurnsOneWay(candidate, math.sign(determinant)) || !KeepsClearance(candidate, curb, side, minimum))
                return false;
            for (int i = 1; i < 16; i++)
            {
                var sample = MathUtils.Position(candidate, i / 16f);
                if (MathUtils.Distance(original.xz, sample.xz, out _) > .06f) return false;
            }
            mouth = candidate;
            remainder = rest;
            return true;
        }

        internal static float StartCurvature(Bezier4x3 curve)
        {
            return Curvature(curve, 0f);
        }

        internal static void SmoothSeam(Bezier4x3 curbA, Bezier4x3 curbB, float3 acrossA, float3 acrossB,
            float minimum, ref Bezier4x3 first, ref Bezier4x3 second)
        {
            var incoming = first.d - first.c;
            var outgoing = second.b - second.a;
            var directionA = math.normalizesafe(incoming);
            var directionB = math.normalizesafe(outgoing);
            var tangent = math.normalizesafe(directionA + directionB);
            float sideA = CurbSide(curbA, acrossA), sideB = CurbSide(curbB, acrossB);
            if (math.lengthsq(tangent.xz) < .5f || sideA == 0 || sideB == 0) return;

            float lengthA = math.length(incoming), lengthB = math.length(outgoing);
            float shoulderA = SignedNormal(tangent.xz, (first.b - first.d).xz);
            float shoulderB = SignedNormal(tangent.xz, (second.c - second.a).xz);
            bool sharesCurvature = shoulderA * shoulderB > .000001f && lengthA > .01f && lengthB > .01f;
            if (sharesCurvature)
            {
                // At a cubic endpoint, signed curvature is proportional to the
                // opposite handle's normal distance divided by this handle's
                // squared length. Preserve the geometric mean of the lengths.
                float adjustment = math.sqrt(math.sqrt(math.abs(shoulderB / shoulderA)) * lengthA / lengthB);
                if (adjustment >= .65f && adjustment <= 1.55f)
                {
                    lengthA /= adjustment;
                    lengthB *= adjustment;
                }
                else sharesCurvature = false;
            }
            if (!sharesCurvature && math.dot(directionA, directionB) >= .9999f) return;

            // Give the halves the same seam tangent. If the first fit would
            // cut the curb, shorten its seam handles and verify it again.
            for (float scale = 1f; scale >= .25f; scale *= .5f)
            {
                var a = first;
                var b = second;
                a.c = a.d - tangent * (lengthA * scale);
                b.b = b.a + tangent * (lengthB * scale);
                if (!KeepsClearance(a, curbA, sideA, minimum) || !KeepsClearance(b, curbB, sideB, minimum)) continue;
                first = a;
                second = b;
                return;
            }
        }

        private static float SignedNormal(float2 direction, float2 delta)
            => direction.x * delta.y - direction.y * delta.x;

        private static bool KeepsClearance(Bezier4x3 paint, Bezier4x3 curb, float side, float minimum)
        {
            for (int i = 0; i <= 16; i++)
            {
                float2 point = MathUtils.Position(paint, i / 16f).xz;
                MathUtils.Distance(curb.xz, point, out float t);
                var tangent = math.normalizesafe(MathUtils.Tangent(curb, t).xz);
                var normal = new float2(-tangent.y, tangent.x) * side;
                if (math.dot(point - MathUtils.Position(curb, t).xz, normal) < minimum) return false;
            }
            return true;
        }

        private static bool FitRoadMouth(Bezier4x3 curb, Bezier4x3 section, float3 cap, float3 direction,
            float stripeRadius, ref Bezier4x3 paint)
        {
            // EdgeGeometry maps the section stripe along the seam's cross section.
            // The corner uses the curb's local normal. On a skewed mouth these
            // two offsets differ, even when the curb itself is continuous.
            var mouth = cap;
            var tangent = direction;
            if (PreviewPaintGeometry.Valid(section) && math.distancesq(section.d, paint.a) < .64f
                && MathUtils.Distance(section, cap, out float at) <= .05f && at < .995f
                && math.dot(math.normalizesafe(MathUtils.Tangent(section, at)), direction) > .9f)
            {
                mouth = section.d;
                tangent = math.normalizesafe(MathUtils.EndTangent(section));
            }

            var shift = mouth - paint.a;
            if (math.lengthsq(shift.xz) > .64f || math.abs(shift.y) > .25f ||
                math.dot(tangent, math.normalizesafe(MathUtils.StartTangent(paint))) < .8f) return false;

            // Preserve the tangent at the road stripe and confine the correction
            // to the mouth end of the native curve.
            var candidate = paint;
            candidate.a = mouth;
            candidate.b = mouth + tangent * math.distance(paint.a, paint.b);

            var curbDirection = math.normalizesafe(MathUtils.StartTangent(curb).xz);
            var offset = paint.a.xz - curb.a.xz;
            float side = math.sign(math.dot(new float2(-curbDirection.y, curbDirection.x), offset));
            float minimum = math.min(stripeRadius + .01f, math.length(offset) - .015f);
            if (!KeepsClearance(candidate, curb, side, minimum)) return false;

            paint = candidate;
            return true;
        }

        // A fallback guide is only an estimate. Use the copied curb halves to
        // check it and, if needed, move the interior into the paved area.
        internal static bool TryClearFallback(Bezier4x3 candidate, Bezier4x3 curbA, Bezier4x3 curbB,
            float3 acrossA, float3 acrossB, float clearance, out Bezier4x3[] path)
        {
            path = null;
            float sideA = CurbSide(curbA, acrossA);
            float sideB = CurbSide(curbB, acrossB);
            if (sideA == 0 || sideB == 0) return false;

            float Margin(float3 position, out float2 roadward)
            {
                float distanceA = MathUtils.Distance(curbA.xz, position.xz, out float tA);
                float distanceB = MathUtils.Distance(curbB.xz, position.xz, out float tB);
                var curve = distanceA <= distanceB ? curbA : curbB;
                float t = distanceA <= distanceB ? tA : tB;
                float side = distanceA <= distanceB ? sideA : sideB;
                var tangent = math.normalizesafe(MathUtils.Tangent(curve, t).xz);
                roadward = new float2(-tangent.y, tangent.x) * side;
                return math.dot(position.xz - MathUtils.Position(curve, t).xz, roadward);
            }

            bool Clear(Bezier4x3 curve, int samples)
            {
                for (int i = 0; i <= samples; i++)
                    if (Margin(MathUtils.Position(curve, i / (float)samples), out _) < clearance) return false;
                return true;
            }

            if (Clear(candidate, 48)) return true;
            if (Margin(candidate.a, out _) < clearance || Margin(candidate.d, out _) < clearance) return false;
            var first = MathUtils.Cut(candidate, new float2(0, 1f/3));
            var middle = MathUtils.Cut(candidate, new float2(1f/3, 2f/3));
            var last = MathUtils.Cut(candidate, new float2(2f/3, 1));
            Margin(first.d, out var normalA);
            Margin(last.a, out var normalB);
            // Move both handles with each interior joint to preserve tangents.
            for (float inset = .125f; inset <= 2f; inset += .125f)
            {
                var a = first;
                var b = middle;
                var c = last;
                a.c.xz += normalA * inset; a.d.xz += normalA * inset;
                b.a = a.d; b.b.xz += normalA * inset;
                b.c.xz += normalB * inset; b.d.xz += normalB * inset;
                c.a = b.d; c.b.xz += normalB * inset;
                if (!Clear(a, 16) || !Clear(b, 16) || !Clear(c, 16)) continue;
                path = new[] { a, b, c };
                return true;
            }
            return false;
        }

        private static float CurbSide(Bezier4x3 curb, float3 oppositeMouth)
        {
            var tangent = math.normalizesafe(MathUtils.StartTangent(curb).xz);
            float across = math.dot(new float2(-tangent.y, tangent.x), oppositeMouth.xz - curb.a.xz);
            return math.isfinite(across) && math.abs(across) >= .1f ? math.sign(across) : 0;
        }

        private static bool Attach(Bezier4x3 curve, Bezier4x3 section, float3 cap, float3 direction,
            out Bezier4x3 fitted, out Bezier4x3? lead)
        {
            fitted=curve;lead=null;
            float distance=MathUtils.Distance(curve.xz,cap.xz,out float at);
            var point=MathUtils.Position(curve,at);
            if(distance<=.05f && math.abs(point.y-cap.y)<=.05f
                && math.dot(math.normalizesafe(MathUtils.Tangent(curve,at)),direction)>.9f && at<.95f)
            {
                fitted=MathUtils.Cut(curve,new float2(at,1));
                var shift=cap-fitted.a;fitted.a=cap;fitted.b+=shift;return true;
            }
            // Follow the section itself when a stop line has trimmed a curved approach.
            if (TrySectionLead(section, curve, cap, direction, out var sectionLead))
            {
                lead = sectionLead;
                return true;
            }

            var tangent=math.normalizesafe(MathUtils.StartTangent(curve));var delta=cap-curve.a;
            float along=math.dot(delta,tangent);
            if(along>0 || along< -12 || math.length(delta-tangent*along)>.05f
                || math.dot(tangent,direction)<.9f)return false;
            lead=new Bezier4x3(cap,math.lerp(cap,curve.a,1f/3),math.lerp(cap,curve.a,2f/3),curve.a);
            return true;
        }

        private static bool TrySectionLead(Bezier4x3 section, Bezier4x3 corner, float3 cap, float3 direction,
            out Bezier4x3 lead)
        {
            lead = default;
            if (!PreviewPaintGeometry.Valid(section) || math.distancesq(section.d, corner.a) > .01f
                || math.dot(math.normalizesafe(MathUtils.EndTangent(section)), math.normalizesafe(MathUtils.StartTangent(corner))) < .9f
                || MathUtils.Distance(section, cap, out float at) > .05f || at >= .995f
                || math.dot(math.normalizesafe(MathUtils.Tangent(section, at)), direction) < .9f) return false;
            var remaining = MathUtils.Cut(section, new float2(at, 1));
            if (MathUtils.Length(remaining) > 12f) return false;
            lead = new Bezier4x3(cap, remaining.b + (cap - remaining.a),
                remaining.c + (corner.a - remaining.d), corner.a);
            return true;
        }

        internal static bool TryJoin(Bezier4x3 a, Bezier4x3 b, Bezier4x3 firstSection, Bezier4x3 lastSection,
            float3 start, float3 outwardStart,
            float3 end, float3 outwardEnd, out Bezier4x3 first, out Bezier4x3 second,
            out Bezier4x3? startLead, out Bezier4x3? endLead)
        {
            first = second = default;
            startLead = endLead = null;
            if (!PreviewPaintGeometry.Valid(a) || !PreviewPaintGeometry.Valid(b)
                || math.distancesq(a.d, b.d) > .01f
                || !Attach(a, firstSection, start, outwardStart, out a, out var leadA)
                || !Attach(b, lastSection, end, outwardEnd, out b, out var leadB)) return false;

            // Keep each native curve piece. A single interpolated cubic can
            // cross the sidewalk between correct endpoints.
            var join = (a.d + b.d) * .5f;
            a.c += join - a.d; a.d = join;
            b.c += join - b.d; b.d = join;
            first = a;
            second = Reverse(b);
            startLead = leadA;
            if (leadB.HasValue) endLead = Reverse(leadB.Value);
            return true;
        }

        private static Bezier4x3 Reverse(Bezier4x3 curve)
            => new Bezier4x3(curve.d, curve.c, curve.b, curve.a);
    }
}
