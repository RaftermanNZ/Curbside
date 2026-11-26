using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class JunctionStripeCoverage
    {
        internal static bool Covers(Bezier4x3 source, float reach, System.Func<float3,float2,float,bool> covered)
        {
            if (!PreviewPaintGeometry.Valid(source)) return false;
            float length = MathUtils.Length(source);
            int samples = math.clamp((int)math.ceil(length / .6f), 4, 48);
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                if (!covered(MathUtils.Position(source,t), math.normalizesafe(MathUtils.Tangent(source,t).xz), reach))
                    return false;
            }
            return true;
        }

        internal static bool CoversPoint(Bezier4x3 stripe, float3 point, float2 tangent, float reach)
        {
            var min = math.min(math.min(stripe.a.xz,stripe.b.xz),math.min(stripe.c.xz,stripe.d.xz)) - reach;
            var max = math.max(math.max(stripe.a.xz,stripe.b.xz),math.max(stripe.c.xz,stripe.d.xz)) + reach;
            if (math.any(point.xz < min) || math.any(point.xz > max)) return false;
            if (MathUtils.Distance(stripe.xz,point.xz,out float at) > reach) return false;
            return math.abs(MathUtils.Position(stripe,at).y-point.y) <= .2f &&
                math.abs(math.dot(tangent,math.normalizesafe(MathUtils.Tangent(stripe,at).xz))) >= .9f;
        }
    }
}
