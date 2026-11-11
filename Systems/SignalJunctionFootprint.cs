using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Copied native geometry values only. Never retains ECS buffers or lookups.
    internal sealed class SignalJunctionFootprint
    {
        internal readonly List<float2> Outline;
        internal Bezier4x3[] NativeCurves;
        private readonly float2 minimum, maximum;
        private readonly float winding;

        private SignalJunctionFootprint(List<float2> outline, float area)
        {
            Outline = outline;
            minimum = new float2(float.MaxValue); maximum = new float2(float.MinValue);
            foreach (var p in outline) { minimum = math.min(minimum, p); maximum = math.max(maximum, p); }
            winding = area > 0 ? 1 : -1;
        }

        // Native secondary-object placement remaps the lateral composition
        // coordinate separately at the mouth and at the junction end.
        private static float Remap(float t, float4 from, float4 to)
        {
            float a = 0, b = from.x, c = 0, d = to.x;
            if (t >= from.x) { a = from.x; b = from.y; c = to.x; d = to.y; }
            if (t >= from.y) { a = from.y; b = from.z; c = to.y; d = to.z; }
            if (t >= from.z) { a = from.z; b = from.w; c = to.z; d = to.w; }
            if (t >= from.w) { a = from.w; b = 1; c = to.w; d = 1; }
            if (b <= a) return d;
            return math.lerp(c, d, math.saturate((t - a) / (b - a)));
        }

        internal static Bezier4x3 Curve(Bezier4x3 left, Bezier4x3 right, float t, float4 offsets, float4 targets)
        {
            float end = Remap(t, offsets, targets);
            float startWidth = math.distance(left.a.xz, right.a.xz);
            float endWidth = math.distance(left.d.xz, right.d.xz);
            var middleTargets = (offsets * startWidth + targets * endWidth) * .5f / math.max(.000001f, (startWidth + endWidth) * .5f);
            float middle = Remap(t, offsets, middleTargets);
            return new Bezier4x3(math.lerp(left.a, right.a, t), math.lerp(left.b, right.b, t),
                math.lerp(left.c, right.c, middle), math.lerp(left.d, right.d, end));
        }

        private static Bezier4x3 Curb(EdgeNodeGeometry geometry, NetCompositionData data, float x)
        {
            float width = math.max(1, data.m_Width);
            float t = x / width + .5f, middle = data.m_MiddleOffset / width + .5f;
            return t < middle
                ? Curve(geometry.m_Left.m_Left, geometry.m_Left.m_Right, t / math.max(.00001f, middle), data.m_SyncVertexOffsetsLeft, geometry.m_SyncVertexTargetsLeft)
                : Curve(geometry.m_Right.m_Left, geometry.m_Right.m_Right, (t - middle) / math.max(.00001f, 1 - middle), data.m_SyncVertexOffsetsRight, geometry.m_SyncVertexTargetsRight);
        }

        private static float2 Closest(float2 p, float2 a, float2 b)
        {
            var d = b - a; float length = math.lengthsq(d);
            return length < .00000001f ? a : a + d * math.saturate(math.dot(p - a, d) / length);
        }

        private static bool Flatten(float2 a, float2 b, float2 c, float2 d, int depth, List<float2> points)
        {
            if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || !math.all(math.isfinite(c)) || !math.all(math.isfinite(d))) return false;
            float error = math.max(math.distancesq(b, Closest(b, a, d)), math.distancesq(c, Closest(c, a, d)));
            if (error <= .000025f) { points.Add(d); return true; }
            if (depth == 8 || points.Count >= 512) return false;
            var ab = (a + b) * .5f; var bc = (b + c) * .5f; var cd = (c + d) * .5f;
            var abc = (ab + bc) * .5f; var bcd = (bc + cd) * .5f; var centre = (abc + bcd) * .5f;
            return Flatten(a, ab, abc, centre, depth + 1, points) && Flatten(centre, bcd, cd, d, depth + 1, points);
        }

        internal static SignalJunctionFootprint Create(EdgeNodeGeometry geometry, NetCompositionData data, float left, float right)
        {
            // Roundabouts and degenerate/no-junction pieces keep their existing
            // path until their native split/middle-radius representation is read.
            if (geometry.m_MiddleRadius > 0 || data.m_Width < 1 || !math.isfinite(left) || !math.isfinite(right)) return null;
            var a = Curb(geometry, data, left); var b = Curb(geometry, data, right);
            var first = new List<float2> { a.a.xz }; var second = new List<float2> { b.a.xz };
            if (!Flatten(a.a.xz, a.b.xz, a.c.xz, a.d.xz, 0, first) || !Flatten(b.a.xz, b.b.xz, b.c.xz, b.d.xz, 0, second)) return null;
            second.Reverse(); first.AddRange(second);
            var result=FromOutline(first);if(result!=null)result.NativeCurves=new[]{a,b};return result;
        }

        internal static SignalJunctionFootprint FromOutline(List<float2> points)
        {
            if (points == null || points.Count < 3 || points.Count > 1024) return null;
            // Translate the area calculation to avoid cancellation in large cities.
            var origin = points[0]; double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (!math.all(math.isfinite(points[i]))) return null;
                var a = points[i] - origin; var b = points[(i + 1) % points.Count] - origin;
                area += (double)a.x * b.y - (double)a.y * b.x;
            }
            return Math.Abs(area) < .05 ? null : new SignalJunctionFootprint(new List<float2>(points), (float)area);
        }

        internal bool Clear(float2 point, float radius, out float2 outside)
        {
            outside = point;
            if (!math.all(math.isfinite(point)) || !math.isfinite(radius) || radius < 0) return false;
            var boxGap = math.max(float2.zero, math.max(minimum - point, point - maximum));
            if (math.lengthsq(boxGap) > radius * radius) return true;
            bool inside = false; float nearest = float.MaxValue; float2 edgePoint = default, normal = default;
            for (int i = 0; i < Outline.Count; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count]; var delta = b - a;
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) * delta.x / delta.y) inside = !inside;
                var p = Closest(point, a, b); float d = math.distancesq(point, p);
                if (d < nearest) { nearest = d; edgePoint = p; normal = math.normalizesafe(new float2(delta.y, -delta.x)) * winding; }
            }
            if (!inside && nearest >= radius * radius) return true;
            if (!inside) normal = math.normalizesafe(point - edgePoint, normal);
            outside = edgePoint + normal * (radius + .01f);
            return false;
        }

        internal bool HasClearance(float2 point, float radius)
        {
            if (!math.all(math.isfinite(point)) || !math.isfinite(radius) || radius < 0) return false;
            var gap = math.max(float2.zero, math.max(minimum - point, point - maximum));
            float squared = radius * radius;
            if (math.lengthsq(gap) > squared) return true;
            bool inside = false;
            for (int i = 0; i < Outline.Count; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
            }
            if (inside) return false;
            for (int i = 0; i < Outline.Count; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
                var edgeGap = math.max(float2.zero, math.max(math.min(a, b) - point, point - math.max(a, b)));
                if (math.lengthsq(edgeGap) >= squared) continue;
                if (math.distancesq(point, Closest(point, a, b)) < squared) return false;
            }
            return true;
        }

        // Conservative containment: the complete base must fit inside this
        // copied native patch. A missing patch never implies sidewalk clearance.
        internal bool ContainsBase(float2 point, float radius)
        {
            if (!math.all(math.isfinite(point)) || !math.isfinite(radius) || radius < 0) return false;
            if (math.any(point - radius < minimum) || math.any(point + radius > maximum)) return false;
            bool inside = false;
            for (int i = 0; i < Outline.Count; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
                if (math.distancesq(point, Closest(point, a, b)) < radius * radius) return false;
            }
            return inside;
        }

        internal bool TryContainBase(float2 point, float radius, out float2 result)
        {
            result = point;
            if (!math.all(math.isfinite(point)) || radius < 0 || !math.isfinite(radius)) return false;
            for (int step = 0; step < 16; step++)
            {
                if (ContainsBase(result, radius)) return true;
                float nearest = float.MaxValue; float2 site = default, inward = default;
                for (int i = 0; i < Outline.Count; i++)
                {
                    var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
                    var edge = b - a; var p = Closest(result, a, b); float distance = math.distancesq(result, p);
                    if (distance < nearest && math.lengthsq(edge) > .000001f)
                    { nearest = distance; site = p; inward = math.normalizesafe(new float2(-edge.y, edge.x)) * winding; }
                }
                if (nearest == float.MaxValue) return false;
                result = site + inward * (radius + .01f);
            }
            return ContainsBase(result, radius);
        }

        internal static bool Available(IList<FarSideLightPolicy.Approach> roads)
        {
            if (roads == null || roads.Count < 2) return false;
            foreach (var road in roads) if (road.Pavement == null) return false;
            return true;
        }

        internal static bool ClearAll(float3 point, IList<FarSideLightPolicy.Approach> roads)
        {
            for(int i=0;i<roads.Count;i++)
            {
                var pavement=roads[i].Pavement;if(pavement==null)continue;
                bool duplicate=false;for(int j=0;j<i;j++)if(ReferenceEquals(roads[j].Pavement,pavement)){duplicate=true;break;}
                if(!duplicate&&!pavement.HasClearance(point.xz,.41f))return false;
            }
            return true;
        }
    }
}
