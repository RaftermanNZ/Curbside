using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class NativePaintCornerGeometry
    {
        private static bool Attach(Bezier4x3 curve, float3 cap, float3 direction,
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
            var tangent=math.normalizesafe(MathUtils.StartTangent(curve));var delta=cap-curve.a;
            float along=math.dot(delta,tangent);
            if(along>0 || along< -12 || math.length(delta-tangent*along)>.05f
                || math.dot(tangent,direction)<.9f)return false;
            lead=new Bezier4x3(cap,math.lerp(cap,curve.a,1f/3),math.lerp(cap,curve.a,2f/3),curve.a);
            return true;
        }

        internal static bool TryJoin(Bezier4x3 a, Bezier4x3 b, float3 start, float3 outwardStart,
            float3 end, float3 outwardEnd, out Bezier4x3 first, out Bezier4x3 second,
            out Bezier4x3? startLead, out Bezier4x3? endLead)
        {
            first = second = default;
            startLead = endLead = null;
            if (!PreviewPaintGeometry.Valid(a) || !PreviewPaintGeometry.Valid(b)
                || math.distancesq(a.d, b.d) > .01f
                || !Attach(a, start, outwardStart, out a, out var leadA)
                || !Attach(b, end, outwardEnd, out b, out var leadB)) return false;

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
