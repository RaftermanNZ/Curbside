using Colossal.Mathematics;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class RoadAttachmentPolicy

    {

        internal static bool TryInherit(Bezier4x3 source,Bezier4x3 target,byte sides,out byte inherited)

        {

            inherited=0;float direction=0;

            for(int i=0;i<=4;i++)

            {

                float t=i*.25f;float distance=MathUtils.Distance(source,MathUtils.Position(target,t),out float sourceT);

                if(!math.isfinite(distance) || distance>.35f) return false;

                float alignment=math.dot(math.normalizesafe(MathUtils.Tangent(source,sourceT)),math.normalizesafe(MathUtils.Tangent(target,t)));

                if(math.abs(alignment)<.985f) return false;

                direction+=alignment;

            }

            inherited=direction<0?(byte)(((sides&1)<<1)|((sides&2)>>1)):(byte)(sides&3);return inherited!=0;

        }

    }

}

