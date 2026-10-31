using Colossal.Mathematics;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class NodeLinePlacement

    {

        internal static bool CanJoin(float3 a,float3 exitA,float3 b,float3 exitB,int kind,int sideA,int sideB)

        {

            if(!math.all(math.isfinite(a))||!math.all(math.isfinite(b))||!math.all(math.isfinite(exitA))||!math.all(math.isfinite(exitB)))return false;

            float gap=math.distance(a.xz,b.xz);

            var delta=b.xz-a.xz;
            // Both endpoint tangents must lead into the gap, not back along a road
            // or sideways across a physical median.
            bool facing=math.dot(math.normalizesafe(exitA.xz),delta)>=gap*.05f&&math.dot(math.normalizesafe(exitB.xz),-delta)>=gap*.05f;

            return facing&&gap>.1f&&gap<=80f&&math.abs(a.y-b.y)<=math.max(.5f,gap*.15f)&&math.lengthsq(exitA.xz)>.01f&&math.lengthsq(exitB.xz)>.01f&&math.dot(math.normalizesafe(exitA.xz),math.normalizesafe(exitB.xz))<=.5f&&(kind==2||kind==3||sideA==-sideB);

        }



        internal static bool TryRoadEnd(float3 point,float3 startLeft,float3 startRight,float3 endLeft,float3 endRight,out bool start)

        {

            float Distance(float3 a,float3 b){var axis=b-a;float length=math.lengthsq(axis);if(length<.01f)return float.MaxValue;return math.distance(point,a+axis*math.saturate(math.dot(point-a,axis)/length));}

            float aDistance=Distance(startLeft,startRight),bDistance=Distance(endLeft,endRight);start=aDistance<=bDistance;return math.isfinite(math.min(aDistance,bDistance))&&math.min(aDistance,bDistance)<=1f;

        }



        internal static bool TryMedianTaper(float3 center,float3 exitCenter,float3 a,float3 exitA,float3 b,float3 exitB,float halfSpacing,out Bezier4x3 first,out Bezier4x3 second)

        {

            first=default;second=default;

            if(!math.all(math.isfinite(center))||!math.all(math.isfinite(a))||!math.all(math.isfinite(b))||!math.all(math.isfinite(exitCenter))||!math.all(math.isfinite(exitA))||!math.all(math.isfinite(exitB))||!math.isfinite(halfSpacing)||halfSpacing<0)return false;

            var direction=math.normalizesafe(new float3(exitCenter.x,0,exitCenter.z));var da=math.normalizesafe(exitA);var db=math.normalizesafe(exitB);

            float ga=math.distance(center.xz,a.xz),gb=math.distance(center.xz,b.xz);var right=new float3(direction.z,0,-direction.x);

            float lateralA=math.dot(a-center,right),lateralB=math.dot(b-center,right);

            if(math.lengthsq(direction)<.5f||math.dot(da,db)<.5f||math.dot(direction,da)>-.25f||math.dot(direction,db)>-.25f||ga<.1f||gb<.1f||ga>80||gb>80||math.abs(lateralA-lateralB)<.1f||math.abs(lateralA-lateralB)>20||math.abs(a.y-center.y)>math.max(.5f,ga*.15f)||math.abs(b.y-center.y)>math.max(.5f,gb*.15f))return false;

            float sign=lateralA<lateralB?-1:1;

            first=Connect(center+right*halfSpacing*sign,direction,a,da);

            second=Connect(center-right*halfSpacing*sign,direction,b,db);return true;

        }



        internal static bool TryPairedTaper(float3 a,float3 exitA,float3 b,float3 exitB,float3 c,float3 exitC,float3 d,float3 exitD,out Bezier4x3 first,out Bezier4x3 second)
        {
            first=second=default;
            if(!math.all(math.isfinite(a))||!math.all(math.isfinite(b))||!math.all(math.isfinite(c))||!math.all(math.isfinite(d))||!math.all(math.isfinite(exitA))||!math.all(math.isfinite(exitB))||!math.all(math.isfinite(exitC))||!math.all(math.isfinite(exitD)))return false;
            var source=math.normalizesafe((exitA+exitB).xz);var target=math.normalizesafe((exitC+exitD).xz);
            if(math.lengthsq(source)<.5f||math.lengthsq(target)<.5f||math.dot(source,target)>-.25f||math.dot(math.normalizesafe(exitA.xz),math.normalizesafe(exitB.xz))<.5f||math.dot(math.normalizesafe(exitC.xz),math.normalizesafe(exitD.xz))<.5f)return false;
            var right=new float2(source.y,-source.x);
            float sourceOrder=math.dot((b-a).xz,right),targetOrder=math.dot((d-c).xz,right);
            if(math.abs(sourceOrder)<.05f||math.abs(targetOrder)<.05f)return false;
            if(sourceOrder*targetOrder<0){var point=c;c=d;d=point;var tangent=exitC;exitC=exitD;exitD=tangent;}
            float ac=math.distance(a.xz,c.xz),bd=math.distance(b.xz,d.xz);
            if(ac<.1f||bd<.1f||ac>80||bd>80||math.abs(a.y-c.y)>math.max(.5f,ac*.15f)||math.abs(b.y-d.y)>math.max(.5f,bd*.15f)||math.dot((c-a).xz,source)<-.1f||math.dot((d-b).xz,source)<-.1f)return false;
            first=Connect(a,exitA,c,exitC);second=Connect(b,exitB,d,exitD);return true;
        }

        internal static Bezier4x3 Connect(float3 a, float3 exitA, float3 b, float3 exitB)

        {

            float length = math.distance(a.xz, b.xz) / 3f;

            return new Bezier4x3(a, a + math.normalizesafe(exitA) * length, b + math.normalizesafe(exitB) * length, b);

        }

    }

}
