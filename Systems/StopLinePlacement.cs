using Colossal.Mathematics;

using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    internal static class StopLinePlacement

    {

        private static float Side(float3 point, float3 a, float2 direction)

            => direction.x * (point.z - a.z) - direction.y * (point.x - a.x);



        // Stop bars are straight runtime curves. Their infinite transverse

        // plane must also intersect the curb beyond the original bar endpoint.

        internal static bool TryIntersect(Bezier4x3 curb, Bezier4x3 stop, out float t, out float3 point)

        {

            t = 0f; point = default(float3);

            float2 direction = stop.d.xz - stop.a.xz;

            if (!math.all(math.isfinite(direction)) || math.lengthsq(direction) < 0.01f) return false;

            direction = math.normalize(direction);

            float before = Side(curb.a, stop.a, direction);

            for (int i = 1; i <= 64; i++)

            {

                float upper = i / 64f;

                float after = Side(MathUtils.Position(curb, upper), stop.a, direction);

                if (math.abs(before) < 0.00001f || before * after <= 0f)

                {

                    float lower = (i - 1) / 64f;

                    for (int j = 0; j < 20; j++)

                    {

                        float middle = (lower + upper) * 0.5f;

                        float side = Side(MathUtils.Position(curb, middle), stop.a, direction);

                        if (math.abs(before) < 0.00001f || before * side <= 0f) upper = middle;

                        else { lower = middle; before = side; }

                    }

                    t = (lower + upper) * 0.5f;

                    point = MathUtils.Position(curb, t);

                    if (math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(curb, t).xz), direction)) > 0.85f) return false;

                    // Do not match a different deck of a stacked junction.

                    return math.abs(point.y - (stop.a.y + stop.d.y) * 0.5f) < 1f;

                }

                before = after;

            }

            return false;

        }



        internal static void AccumulateStopCut(float cut, ref float lower, ref float upper)

        {

            if (!math.isfinite(cut) || cut < 0f || cut > 1f) return;

            if (cut <= 0.5f) lower = math.max(lower, cut);

            else upper = math.min(upper, cut);

        }



        internal static void AccumulateStopCut(float cut, bool lowerEnd, ref float lower, ref float upper)

        {

            if (!math.isfinite(cut) || cut < 0f || cut > 1f) return;

            if (lowerEnd) lower = math.max(lower, cut);

            else upper = math.min(upper, cut);

        }



        internal static bool TrimRange(Bezier4x3 original, float lower, float upper, float gap, out Bezier4x3 result)

        {

            result = default(Bezier4x3);

            if (upper <= lower || !math.isfinite(gap) || gap < 0f) return false;

            result = MathUtils.Cut(original, new float2(lower, upper));

            if (lower > 0f && !TrimAtStop(result, 0f, true, gap, out result)) return false;

            if (upper < 1f && !TrimAtStop(result, 1f, false, gap, out result)) return false;

            return true;

        }



        internal static bool TrimAtStop(Bezier4x3 original, float cut, bool start, float gap, out Bezier4x3 result)

        {

            result = default(Bezier4x3);

            if (!math.isfinite(gap) || gap < 0f || !math.isfinite(cut) || cut < 0f || cut > 1f) return false;

            float remaining = MathUtils.Length(MathUtils.Cut(original, start ? new float2(cut, 1f) : new float2(0f, cut)));

            if (remaining <= gap + 0.1f) return false;

            float lower = start ? cut : 0f, upper = start ? 1f : cut;

            for (int i = 0; i < 24; i++)

            {

                float middle = (lower + upper) * 0.5f;

                float distance = MathUtils.Length(MathUtils.Cut(original, start ? new float2(cut, middle) : new float2(middle, cut)));

                if (start ? distance < gap : distance > gap) lower = middle; else upper = middle;

            }

            float endpoint = (lower + upper) * 0.5f;

            result = MathUtils.Cut(original, start ? new float2(endpoint, 1f) : new float2(0f, endpoint));

            return true;

        }



        internal static float MedianGap(bool align,float clearance,float stopWidth)

        {

            if(!math.isfinite(clearance)||clearance<0)return 0;

            return clearance+(align&&math.isfinite(stopWidth)&&stopWidth>0?stopWidth*.5f:0);

        }



        internal static bool TryExtension(Bezier4x3 stop, float3 curb, float3 roadCenter,

            float maxLength, out Bezier4x3 extension)

            => TryExtension(stop, curb, roadCenter, maxLength, 0f, out extension);



        internal static bool TryExtension(Bezier4x3 stop, float3 curb, float3 roadCenter,

            float maxLength, float gap, out Bezier4x3 extension)

        {

            extension = default(Bezier4x3);

            if (!math.isfinite(gap) || gap < 0f) return false;

            float2 axis = stop.d.xz - stop.a.xz;

            float length = math.length(axis);

            if (!math.isfinite(length) || length < 0.1f) return false;

            axis /= length;

            float projected = math.dot(curb.xz - stop.a.xz, axis);

            float center = math.dot(roadCenter.xz - stop.a.xz, axis);

            if (projected >= -0.02f && projected <= length + 0.02f) return false;

            // Extend only the approach-side outer endpoint, never carry an

            // incoming half-road stop bar across the opposing carriageway.

            if ((projected - center) * (length * 0.5f - center) < -0.01f) return false;

            float3 source = projected < 0f ? stop.a : stop.d;

            float distance = math.distance(source.xz, curb.xz);

            if (!math.isfinite(distance) || distance > maxLength || distance < 0.02f) return false;

            // Retain the actual stop-bar height, including its decal bias.

            if (distance <= gap + 0.02f) return false;

            float3 target = math.lerp(source, curb, (distance - gap) / distance); target.y = source.y;

            extension = new Bezier4x3(source, math.lerp(source, target, 1f / 3f), math.lerp(source, target, 2f / 3f), target);

            return true;

        }

    }

}
