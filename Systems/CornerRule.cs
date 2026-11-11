using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace Curbside.CornerStudy
{
    public enum BoundaryKind { Curb, TrafficLaneEdge }
    public struct CurbSlice
    {
        public int Road, Side;
        public BoundaryKind Kind;
        public Bezier4x3 Curb, Back;
        public bool HasBack;
        public float3 Outside;
    }
    public sealed class CornerProposal
    {
        public int RoadA, SideA, RoadB, SideB;
        public BoundaryKind KindA, KindB;
        public float3 CurbPoint, Position, Normal;
        public float Clearance;
        public bool HasSidewalkBoundary, Fits;
        public string Reason;
    }
    // One bounded calculation on copied road geometry. No World, actors, jobs,
    // refresh requests, retries, prefab writes, clock or retained scene state.
    public static class CornerRule
    {
        public const float Setback = 1.5f;
        private const int Samples = 16;
        private const float JoinTolerance = .05f;
        private static float Error(Bezier4x3 curve)
            => 6 * math.max(math.length((curve.c - 2 * curve.b + curve.a).xz),
                math.length((curve.d - 2 * curve.c + curve.b).xz)) / (8 * Samples * Samples);
        private static bool Finite(Bezier4x3 c)
            => math.all(math.isfinite(c.a)) && math.all(math.isfinite(c.b)) && math.all(math.isfinite(c.c)) && math.all(math.isfinite(c.d));
        private static List<float3> Points(Bezier4x3 curve, bool reverse)
        {
            var result = new List<float3>(Samples + 1);
            for (int i = 0; i <= Samples; i++) result.Add(MathUtils.Position(curve, reverse ? 1 - (float)i / Samples : (float)i / Samples));
            return result;
        }
        private static float Distance(float2 point, float2 a, float2 b)
        {
            var edge = b - a;
            return math.distance(point, a + edge * math.saturate(math.dot(point - a, edge) / math.max(.0000001f, math.lengthsq(edge))));
        }
        private static float CurveGap(float2 point, Bezier4x3 curve)
        {
            float best = float.MaxValue; var previous = curve.a.xz;
            for (int i = 1; i <= Samples; i++)
            { var next = MathUtils.Position(curve, (float)i / Samples).xz; best = math.min(best, Distance(point, previous, next)); previous = next; }
            return best - Error(curve);
        }
        private static bool Contains(List<float3> polygon, float2 point, float radius)
        {
            bool inside = false;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i].xz; var b = polygon[(i + 1) % polygon.Count].xz;
                if (Distance(point, a, b) < radius) return false;
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
            }
            return inside;
        }
        private static void CircleExit(float2 origin,float2 direction,float2 centre,float radius,ref float exit)
        {
            var p=origin-centre;float b=math.dot(p,direction),disc=b*b-math.lengthsq(p)+radius*radius;
            if(disc>=0)exit=math.max(exit,-b+math.sqrt(disc));
        }
        private static bool Slab(float at,float speed,float low,float high,ref float enter,ref float exit)
        {
            if(math.abs(speed)<.000001f)return at>=low&&at<=high;
            float a=(low-at)/speed,b=(high-at)/speed;enter=math.max(enter,math.min(a,b));exit=math.min(exit,math.max(a,b));return exit>=enter;
        }
        // Analytic ray/capsule exit. This offsets the complete native corner
        // transition once; it is not an iterative placement or collision search.
        private static float OffsetDistance(List<float3> path,float2 origin,float2 direction,float radius)
        {
            float farthest=0;
            for(int i=1;i<path.Count;i++)
            {
                var a=path[i-1].xz;var b=path[i].xz;var e=b-a;float length=math.length(e);
                CircleExit(origin,direction,a,radius,ref farthest);CircleExit(origin,direction,b,radius,ref farthest);
                if(length<.000001f)continue;e/=length;var n=new float2(-e.y,e.x);var p=origin-a;
                float enter=0,exit=float.MaxValue;
                if(Slab(math.dot(p,e),math.dot(direction,e),0,length,ref enter,ref exit)&&
                   Slab(math.dot(p,n),math.dot(direction,n),-radius,radius,ref enter,ref exit))farthest=math.max(farthest,exit);
            }
            return farthest;
        }
        private static float3 Nearest(List<float3> path,float2 point)
        {
            float distance=float.MaxValue;float3 chosen=default;
            for(int i=1;i<path.Count;i++)
            {
                var a=path[i-1];var edge=path[i]-a;float t=math.saturate(math.dot(point-a.xz,edge.xz)/math.max(.0000001f,math.lengthsq(edge.xz)));
                var p=a+edge*t;float d=math.distancesq(p.xz,point);if(d<distance){distance=d;chosen=p;}
            }
            return chosen;
        }
        private static CornerProposal Propose(CurbSlice a, CurbSlice b, IList<CurbSlice> all,
            float setback, float envelope, IList<Bezier4x3> walking, float distanceFraction = .5f)
        {
            var result = new CornerProposal { RoadA = a.Road, SideA = a.Side, RoadB = b.Road, SideB = b.Side, KindA = a.Kind, KindB = b.Kind };
            var first = Points(a.Curb, false); var second = Points(b.Curb, true);
            // Different native curb widths can leave an inner seam transition.
            // Its physical corner is identified by the shared outer boundary,
            // not by requiring both derived inner curves to end identically.
            var samples = new List<float3>(first);
            if (math.distance(first[first.Count-1].xz,second[0].xz) > .00001f) samples.Add(second[0]);
            for (int i = 1; i < second.Count; i++) samples.Add(second[i]);
            float length = 0; for (int i = 1; i < samples.Count; i++) length += math.distance(samples[i - 1].xz, samples[i].xz);
            float left = length * distanceFraction; int segment = 1;
            for (; segment < samples.Count - 1; segment++)
            { float span = math.distance(samples[segment - 1].xz, samples[segment].xz); if (left <= span) break; left -= span; }
            float fraction = math.saturate(left / math.max(.00001f, math.distance(samples[segment - 1].xz, samples[segment].xz)));
            var curb = math.lerp(samples[segment-1], samples[segment], fraction);
            var tangent = math.normalizesafe((samples[segment]-samples[segment-1]).xz);
            var outside = math.normalizesafe(a.Outside.xz) + math.normalizesafe(b.Outside.xz);
            var normal=math.normalizesafe(outside);
            float curveError=math.max(Error(a.Curb),Error(b.Curb));
            float distance=OffsetDistance(samples,curb.xz,normal,setback+curveError);
            result.Position=curb+new float3(normal.x,0,normal.y)*distance;
            result.CurbPoint=Nearest(samples,result.Position.xz);
            result.Normal=math.normalizesafe(new float3(result.Position.x-result.CurbPoint.x,0,result.Position.z-result.CurbPoint.z));
            result.Clearance = float.MaxValue;
            foreach (var slice in all) result.Clearance = math.min(result.Clearance, CurveGap(result.Position.xz, slice.Curb));
            result.Clearance = math.min(result.Clearance, Distance(result.Position.xz,a.Curb.d.xz,b.Curb.d.xz));
            if (math.lengthsq(tangent) < .9f || math.lengthsq(outside) < .1f || result.Clearance < envelope + .005f)
            { result.Reason = "road-side envelope does not fit"; return result; }
            // A pedestrian route centreline is not an occupied collider or the
            // sidewalk boundary. Native walkers share corner space with normal
            // roadside furniture. Physical base/attachment containment is the
            // placement rule; route refreshes must not invalidate this layout.
            if (!a.HasBack || !b.HasBack)
            { result.Reason = "road-side target measured; outer sidewalk not recorded"; return result; }
            if (!Finite(a.Back) || !Finite(b.Back) || math.distance(a.Back.d.xz, b.Back.d.xz) > JoinTolerance)
            { result.Reason = "outer corner connection is incomplete"; return result; }
            result.HasSidewalkBoundary = true;
            var backing=Points(b.Back,false);backing.AddRange(Points(a.Back,true));
            var backPoint=Nearest(backing,result.Position.xz);
            float roadDistance=math.distance(result.CurbPoint.xz,result.Position.xz),backDistance=math.distance(backPoint.xz,result.Position.xz);
            result.Position.y=math.lerp(result.CurbPoint.y,backPoint.y,roadDistance/math.max(.00001f,roadDistance+backDistance));
            var strip = samples; strip.AddRange(backing);
            float error = math.max(math.max(Error(a.Curb), Error(b.Curb)), math.max(Error(a.Back), Error(b.Back)));
            result.Fits = Contains(strip, result.Position.xz, envelope + .005f + error);
            result.Reason = result.Fits ? "fits the complete corner strip" : "complete fixture does not fit the sidewalk";
            return result;
        }
        // Exact outer driving-lane edges. No implicit curb/shoulder padding:
        // the same 1 m rule is applied once, into the supporting shoulder.
        public static bool DrivingEdges(IList<float2> lanes, float surfaceWidth, out float low, out float high)
        {
            low = float.MaxValue; high = float.MinValue;
            if (lanes == null || lanes.Count == 0 || !math.isfinite(surfaceWidth) || surfaceWidth <= 0) return false;
            foreach (var lane in lanes)
            {
                if (!math.all(math.isfinite(lane)) || lane.y <= 0) return false;
                low = math.min(low, lane.x - lane.y * .5f); high = math.max(high, lane.x + lane.y * .5f);
            }
            return high > low && low >= -surfaceWidth * .5f - .01f && high <= surfaceWidth * .5f + .01f;
        }
        private static bool StraightBack(CurbSlice a, CurbSlice b)
        {
            // If the existing T projection has no result, distinguish an
            // actual straight back from an unhandled curved bank. A Bezier
            // lies in its control hull; bound that hull against the curb chord.
            var start = a.Curb.a.xz;
            var chord = b.Curb.a.xz - start;
            float lengthSquared = math.lengthsq(chord);
            if (lengthSquared < JoinTolerance * JoinTolerance) return false;
            float limit = JoinTolerance * JoinTolerance * lengthSquared;
            bool OnChord(float3 point)
            {
                var delta = point.xz - start;
                float cross = chord.x * delta.y - chord.y * delta.x;
                float along = math.dot(delta, chord);
                return cross * cross <= limit && along >= 0 && along <= lengthSquared;
            }
            return OnChord(a.Curb.b) && OnChord(a.Curb.c) && OnChord(a.Curb.d) &&
                OnChord(b.Curb.b) && OnChord(b.Curb.c) && OnChord(b.Curb.d);
        }
        public static List<CornerProposal> Calculate(IList<CurbSlice> slices, float envelope,
            IList<Bezier4x3> walking = null, float setback = Setback, float cornerFraction = .5f)
        {
            var result = new List<CornerProposal>();
            if (slices == null || slices.Count > 12 || slices.Count < 4 || !math.isfinite(setback) || setback <= 0 ||
                !math.isfinite(envelope) || envelope < .4f) return result;
            var used = new bool[slices.Count];
            var straight = new List<int2>();
            for (int i = 0; i < slices.Count; i++)
            {
                if (used[i] || !Finite(slices[i].Curb)) continue;
                int partner = -1;
                for (int j = i + 1; j < slices.Count; j++)
                {
                    if (used[j] || slices[i].Road == slices[j].Road || !Finite(slices[j].Curb)) continue;
                    bool matched = slices[i].HasBack && slices[j].HasBack
                        ? Finite(slices[i].Back) && Finite(slices[j].Back) && math.distance(slices[i].Back.d.xz,slices[j].Back.d.xz) <= JoinTolerance
                        : math.distance(slices[i].Curb.d.xz,slices[j].Curb.d.xz) <= JoinTolerance;
                    if (!matched) continue;
                    if (partner >= 0) { partner = -1; break; } partner = j;
                }
                if (partner < 0) continue;
                used[i] = used[partner] = true;
                if (slices.Count == 6 &&
                    math.dot(math.normalizesafe(slices[i].Outside.xz), math.normalizesafe(slices[partner].Outside.xz)) > .995f)
                    straight.Add(new int2(i, partner));
                else result.Add(Propose(slices[i], slices[partner], slices, setback, envelope, walking, cornerFraction));
            }
            // A T has no curved back corner. Project its two stem-side corner
            // poles onto the actual back curb, preserving the same setback and
            // perpendicular spans without inventing a rectangular road surface.
            List<int2> curvedBackFallback = null;
            if (slices.Count == 6) foreach (var pair in straight)
            {
                var a = slices[pair.x]; var b = slices[pair.y];
                int proposalsBefore = result.Count;
                var direction = math.normalizesafe(a.Outside.xz + b.Outside.xz);
                var path = Points(a.Curb, false); var last = Points(b.Curb, true);
                for (int i = 1; i < last.Count; i++) path.Add(last[i]);
                float total = 0; for (int i = 1; i < path.Count; i++) total += math.distance(path[i - 1].xz, path[i].xz);
                var origins = new List<float3>();
                foreach (var stem in slices) if (stem.Road != a.Road && stem.Road != b.Road)
                {
                    foreach (var front in result)
                        if (front.RoadA == stem.Road && front.SideA == stem.Side || front.RoadB == stem.Road && front.SideB == stem.Side)
                        { origins.Add(front.Position); break; }
                }
                foreach (var origin in origins)
                {
                    float travelled = 0; bool found = false;
                    for (int i = 1; i < path.Count; i++)
                    {
                        var edge = path[i].xz - path[i - 1].xz; var delta = path[i - 1].xz - origin.xz;
                        float determinant = direction.x * edge.y - direction.y * edge.x;
                        float length = math.length(edge);
                        if (math.abs(determinant) > .000001f)
                        {
                            float along = (delta.x * edge.y - delta.y * edge.x) / determinant;
                            float t = (delta.x * direction.y - delta.y * direction.x) / determinant;
                            if (along >= 0 && t >= 0 && t <= 1)
                            { result.Add(Propose(a, b, slices, setback, envelope, walking, (travelled + t * length) / total)); found = true; break; }
                        }
                        travelled += length;
                    }
                    if (!found) break;
                }
                if (result.Count == proposalsBefore && !StraightBack(a, b))
                {
                    if (curvedBackFallback == null) curvedBackFallback = new List<int2>();
                    curvedBackFallback.Add(pair);
                }
            }
            // Keep successful T projections exactly as before. Add recovered
            // curved banks afterwards, so they cannot become synthetic stem
            // origins for another pair in the same native corner snapshot.
            if (curvedBackFallback != null) foreach (var pair in curvedBackFallback)
                result.Add(Propose(slices[pair.x], slices[pair.y], slices, setback, envelope, walking, cornerFraction));
            return result;
        }
    }
}
