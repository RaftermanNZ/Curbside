using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class VanillaDividerPolicy
    {
        internal const string Asset = "MGR Vanilla Lane Divider";
        internal const byte Placed = 13; // Existing save value for a composed vanilla divider.
        internal const byte Style = 14;
        internal const byte CenterMode = 16;
        internal const byte EdgeMode = 14;
        internal const byte DividerMode = 14;
        private const byte GeneratedOffset = 32;

        internal static bool IsGenerated(byte intent) => intent == Placed || intent > GeneratedOffset && intent <= GeneratedOffset + 12;
        internal static byte EncodeGenerated(byte style) => style == Style ? Placed : (byte)(GeneratedOffset + style);
        internal static byte DecodeGenerated(byte intent) => intent == Placed ? Style : (byte)(intent - GeneratedOffset);

        internal static bool Eligible(UpgradeDragTarget target)
            => target.Individual && target.Role == 4 && (target.Part != Entity.Null || target.Slot > 0);

        internal static bool Matches(UpgradeDragTarget anchor, bool node, UpgradeDragTarget candidate, bool candidateNode, bool exact)
            => node == candidateNode && Eligible(candidate)
                && (!exact || (anchor.Part == candidate.Part && anchor.Slot == candidate.Slot));

        internal readonly struct Lane
        {
            internal readonly float X, Width, Height;
            internal readonly bool Reverse;
            internal Lane(float x, float width, float height, bool reverse)
            { X = x; Width = width; Height = height; Reverse = reverse; }
        }

        internal readonly struct LanePosition
        {
            internal readonly int FromTrafficRight;
            internal readonly bool Reverse;
            internal bool Known => FromTrafficRight > 0;

            internal LanePosition(int fromTrafficRight, bool reverse)
            { FromTrafficRight = fromTrafficRight; Reverse = reverse; }

            internal bool Matches(LanePosition candidate, bool reversedRoad)
                => Known && candidate.Known && FromTrafficRight == candidate.FromTrafficRight
                    && candidate.Reverse == (Reverse != reversedRoad);
        }

        internal static List<(float X, float Height, LanePosition Position)> Boundaries(List<Lane> lanes)
        {
            lanes.Sort((a, b) => a.X.CompareTo(b.X));
            var result = new List<(float X, float Height, LanePosition Position)>();
            int forwardRemaining = 0;
            foreach (var lane in lanes)
                if (!lane.Reverse) forwardRemaining++;
            int reverseSeen = 0;
            for (int i = 1; i < lanes.Count; i++)
            {
                var a = lanes[i - 1];
                var b = lanes[i];
                if (a.Reverse) reverseSeen++;
                else forwardRemaining--;
                if (a.Reverse != b.Reverse || !math.isfinite(a.Width) || !math.isfinite(b.Width)
                    || a.Width <= 0 || b.Width <= 0 || !math.isfinite(a.Height) || !math.isfinite(b.Height)
                    || math.abs(a.Height - b.Height) > .2f) continue;
                float first = a.X + a.Width * .5f;
                float last = b.X - b.Width * .5f;
                float gap = last - first;
                // Authored widths can overlap slightly on some roads. Keep
                // distinct lane centers and leave actual medians out.
                if (!math.isfinite(gap) || gap < -.45f || gap > .5f
                    || b.X - a.X < math.min(a.Width, b.Width) * .7f) continue;
                int rank = a.Reverse ? reverseSeen : forwardRemaining;
                result.Add(((first + last) * .5f, (a.Height + b.Height) * .5f, new LanePosition(rank, a.Reverse)));
            }
            return result;
        }

        internal static bool TrimPaint(Bezier4x3 path, float lower, float upper, out Bezier4x3 paint)
            => StopLinePlacement.TrimRange(path, lower, upper, .55f, out paint)
                && PreviewPaintGeometry.Valid(paint) && MathUtils.Length(paint) > .1f;

        internal static bool UsesHalf(Bezier4x3 half, Bezier4x3 source)
        {
            for (int i = 1; i <= 3; i++)
            {
                float t = i * .25f;
                if (Follows(half, MathUtils.Position(source, t), math.normalizesafe(MathUtils.Tangent(source, t))))
                    return true;
            }
            return false;
        }

        private static bool Follows(Bezier4x3 boundary, float3 point, float3 tangent)
            => PreviewPaintGeometry.Valid(boundary) && MathUtils.Distance(boundary, point, out float at) <= .35f
                && math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(boundary, at)), tangent)) >= .95f;

        internal static bool HasAuthoredMark(Bezier4x3 first, Bezier4x3 last, Bezier4x3 source)
        {
            // Sample inside the source so a perpendicular marking sharing a seam
            // or a neighboring lane whose endpoints converge cannot claim the slot.
            for (int i = 1; i <= 3; i++)
            {
                float t = i * .25f;
                var point = MathUtils.Position(source, t);
                var tangent = math.normalizesafe(MathUtils.Tangent(source, t));
                if (!Follows(first, point, tangent) && !Follows(last, point, tangent)) return false;
            }
            return true;

        }
    }
}
