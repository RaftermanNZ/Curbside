using Colossal.Mathematics;

using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    internal static class ParkingSetback

    {

        internal static bool JunctionEnd(float edgeDelta, float projectedRoadPosition, bool roadStartJunction, bool roadEndJunction)

        {

            bool nearStart = edgeDelta == 0f || (math.isfinite(projectedRoadPosition) && projectedRoadPosition <= 0.05f);

            bool nearEnd = edgeDelta == 1f || (math.isfinite(projectedRoadPosition) && projectedRoadPosition >= 0.95f);

            return (nearStart && roadStartJunction) || (nearEnd && roadEndJunction);

        }



        internal static bool TryLimits(Bezier4x3 curve, float lower, float upper,

            bool startJunction, bool endJunction, float setback, out float first, out float last)

        {

            first = 0f; last = 1f;

            if (!math.isfinite(setback) || setback < 0f || lower < 0f || upper > 1f || upper <= lower) return false;

            float length = MathUtils.Length(MathUtils.Cut(curve, new float2(lower, upper)));

            if (!math.isfinite(length) || length <= 0.1f) return false;

            bool trimStart = startJunction || lower > 0f, trimEnd = endJunction || upper < 1f;

            if (length <= setback * ((trimStart ? 1 : 0) + (trimEnd ? 1 : 0)) + 0.2f)

            { first = last = 0.5f; return false; }

            first = trimStart ? Advance(curve, lower, upper, setback, true) : lower;

            last = trimEnd ? Advance(curve, lower, upper, setback, false) : upper;

            return last > first;

        }



        private static float Advance(Bezier4x3 curve, float lower, float upper, float distance, bool start)

        {

            float lo = lower, hi = upper;

            for (int i = 0; i < 24; i++)

            {

                float middle = (lo + hi) * 0.5f;

                float length = MathUtils.Length(MathUtils.Cut(curve, start ? new float2(lower, middle) : new float2(middle, upper)));

                if (start ? length < distance : length > distance) lo = middle; else hi = middle;

            }

            return (lo + hi) * 0.5f;

        }

    }

}

