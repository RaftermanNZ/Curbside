using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Exact memoization of a pure math predicate. A changed road/guide/corner
    // supplies a different key immediately; no native state is cached.
    internal sealed class CornerClearanceCache
    {
        internal static readonly CornerClearanceCache Shared = new CornerClearanceCache();
        private const int Capacity = 128;
        private struct Entry
        {
            internal bool Valid, Result;
            internal Bezier4x3 Guide, Candidate;
            internal float Clearance;
        }
        private readonly Entry[] entries = new Entry[Capacity];

        private static bool Same(float3 a, float3 b) => math.all(math.asuint(a) == math.asuint(b));
        private static bool Same(Bezier4x3 a, Bezier4x3 b)
            => Same(a.a,b.a) && Same(a.b,b.b) && Same(a.c,b.c) && Same(a.d,b.d);

        internal bool Clear(Bezier4x3 guide, Bezier4x3 candidate, float clearance)
        {
            // Hash collisions only replace a slot. Full bit equality is required.
            uint hash = math.hash(guide.a) ^ math.hash(guide.d) * 397u
                ^ math.hash(candidate.a) * 17u ^ math.hash(candidate.d) ^ math.asuint(clearance);
            int slot = (int)(hash & (Capacity - 1));
            var entry = entries[slot];
            if (entry.Valid && math.asuint(entry.Clearance) == math.asuint(clearance)
                && Same(entry.Guide,guide) && Same(entry.Candidate,candidate)) return entry.Result;
            bool clear = true;
            for (int sample=0;sample<=12;sample++)
            {
                var point=MathUtils.Position(candidate,sample/12f);
                float distance=MathUtils.Distance(guide,point,out float at);
                if(distance+.02f<clearance || math.abs(point.y-MathUtils.Position(guide,at).y)>.5f)
                { clear=false; break; }
            }
            entries[slot] = new Entry { Valid=true, Result=clear, Guide=guide, Candidate=candidate, Clearance=clearance };
            return clear;
        }
    }
}
