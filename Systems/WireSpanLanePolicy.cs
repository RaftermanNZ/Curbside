using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Copied geometry only, evaluated during an existing network-layout task.
    // Lane fractions survive phase filtering; no traffic state is modified.
    internal static class WireSpanLanePolicy
    {
        private static double Cross(float2 a, float2 b) => (double)a.x*b.y-(double)a.y*b.x;
        private static double Value(double a,double b,double c,double d,double t) => ((a*t+b)*t+c)*t+d;
        private static void Root(List<double> values,double t)
        {
            if(t<=0 || t>=1 || double.IsNaN(t) || double.IsInfinity(t))return;
            foreach(var old in values)if(Math.Abs(old-t)<1e-8)return;
            values.Add(t);
        }
        private static bool Intersections(Bezier4x3 curve,float2 origin,float2 delta,List<float> hits)
        {
            if(!math.all(math.isfinite(curve.a))||!math.all(math.isfinite(curve.b))||
                !math.all(math.isfinite(curve.c))||!math.all(math.isfinite(curve.d)))return false;
            var pa=curve.a.xz-origin;var pb=curve.b.xz-origin;
            var pc=curve.c.xz-origin;var pd=curve.d.xz-origin;
            double v0=Cross(pa,delta),v1=Cross(pb,delta),v2=Cross(pc,delta),v3=Cross(pd,delta);
            double a=-v0+3*v1-3*v2+v3,b=3*v0-6*v1+3*v2,c=-3*v0+3*v1,d=v0;
            double scale=Math.Max(1,Math.Max(Math.Abs(a),Math.Max(Math.Abs(b),Math.Abs(c))));
            // Coincident curb/cable has no unique crossing interval.
            if(Math.Abs(a)+Math.Abs(b)+Math.Abs(c)+Math.Abs(d)<1e-8)return false;
            var splits=new List<double>{0,1};
            if(Math.Abs(a)<1e-12*scale){if(Math.Abs(b)>1e-12*scale)Root(splits,-c/(2*b));}
            else
            {
                double disc=b*b-3*a*c;
                if(disc>=0){double s=Math.Sqrt(disc);Root(splits,(-b-s)/(3*a));Root(splits,(-b+s)/(3*a));}
            }
            splits.Sort();var roots=new List<double>();
            for(int i=0;i<splits.Count;i++)
            {
                double at=splits[i],v=Value(a,b,c,d,at);
                if(Math.Abs(v)<1e-7*scale)roots.Add(at);
                if(i+1==splits.Count)continue;
                double next=splits[i+1],other=Value(a,b,c,d,next);
                if(v*other>=0)continue;
                double lo=at,hi=next;
                for(int step=0;step<28;step++)
                {double mid=(lo+hi)*.5,m=Value(a,b,c,d,mid);if((m<0)==(v<0))lo=mid;else hi=mid;}
                roots.Add((lo+hi)*.5);
            }
            double squared=math.lengthsq(delta);
            foreach(double t in roots)
            {
                double q=1-t;
                double x=q*q*q*pa.x+3*q*q*t*pb.x+3*q*t*t*pc.x+t*t*t*pd.x;
                double y=q*q*q*pa.y+3*q*q*t*pb.y+3*q*t*t*pc.y+t*t*t*pd.y;
                float fraction=(float)((x*delta.x+y*delta.y)/squared);
                if(fraction<=0 || fraction>=1)continue;
                bool duplicate=false;foreach(float old in hits)if(Math.Abs(old-fraction)*Math.Sqrt(squared)<.02)duplicate=true;
                if(!duplicate)hits.Add(fraction);
            }
            return true;
        }
        internal static bool TryMap(IList<float3> lanes,float3 left,float3 right,float3 pole,float3 target,
            float3 inward,IList<Bezier4x3> curbs,out float[] heads)
        {
            heads=null;
            if(lanes==null||lanes.Count<1||lanes.Count>16||curbs==null||curbs.Count<4||curbs.Count>12)return false;
            var span=target.xz-pole.xz;float length=math.length(span),projection=math.dot(span,inward.xz);
            var bank=right.xz-left.xz;float width2=math.lengthsq(bank);
            if(!math.all(math.isfinite(span))||!math.all(math.isfinite(bank))||!math.all(math.isfinite(inward))||
                !math.isfinite(projection)||length<2||length>60||projection<=1||width2<1)return false;
            var hits=new List<float>();
            foreach(var curb in curbs)if(!Intersections(curb,pole.xz,span,hits))return false;
            // A ring-adjacent span must cross one unambiguous outer road band.
            // Multiple bands are not silently collapsed across another road.
            if(hits.Count!=2)return false;hits.Sort();
            float low=hits[0]+.2f/length,high=hits[1]-.2f/length;
            if(high<=low)return false;
            bool reverse=math.dot(bank,span)<0;heads=new float[lanes.Count];
            for(int i=0;i<lanes.Count;i++)
            {
                if(!math.all(math.isfinite(lanes[i])))return false;
                float t=math.dot(lanes[i].xz-left.xz,bank)/width2;
                // Do not clamp malformed/mismatched lane banks into validity.
                if(!math.isfinite(t)||t<0||t>1)return false;
                if(reverse)t=1-t;
                float distance=math.lerp(low,high,t)*projection;
                if(!math.isfinite(distance)||distance<.349f||distance>29.501f||distance>projection-.3f)return false;
                heads[i]=distance;
            }
            Array.Sort(heads);
            for(int i=1;i<heads.Length;i++)if(heads[i]-heads[i-1]<1.2f)return false;
            return true;
        }
    }

    internal static partial class GardenCityLightPolicy
    {
        internal static bool TryWireLayout(FarSideLightPolicy.Approach road,float3 pole,IList<float3> supports,
            byte style,out float3 inward,out float[] heads,out int target)
        {
            heads=null;target=-1;inward=default;
            if(road.WireHeads!=null&&road.WireHeads.TryRead(road,pole,supports,style,out inward,out heads,out target))return true;
            var bank=road.WireBank??road.Incoming;
            bool straight=TryLayout(bank,road.Outward,pole,out inward,out var complete);
            if(math.lengthsq(inward)<.9f)return false;
            int origin=-1;float closest=float.MaxValue;
            if(supports==null)return false;
            for(int i=0;i<supports.Count;i++){float distance=math.distancesq(pole,supports[i]);if(distance<closest){closest=distance;origin=i;}}
            if(origin<0||closest>.001f)return false;
            target=WireNeighborForLayout(supports,origin,inward,true);if(target<0)return false;
            float projection=math.dot(supports[target]-pole,inward);
            straight=straight&&projection>=complete[complete.Length-1]+.3f;
            if(straight)
            {
                if(!TryLayout(road.Incoming,road.Outward,pole,out inward,out heads))return false;
            }
            else
            {
                // Validate the full bank first. A protected phase must use the
                // same mapping as its siblings, even if its own old fit passed.
                if(!WireSpanLanePolicy.TryMap(bank,road.Left,road.Right,pole,supports[target],inward,road.WireCurbs,out complete)||
                    !WireSpanLanePolicy.TryMap(road.Incoming,road.Left,road.Right,pole,supports[target],inward,road.WireCurbs,out heads))return false;
            }
            if(global::GardenCitySignals.SignalPackPolicy.UseRigidSignLayout)
                foreach(float d in complete)if(!global::GardenCitySignals.SignalMountClearance.AllowHead(style,true,d))return false;
            return true;
        }
    }
}
