using System.Collections.Generic;
using Game.Objects;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Owned value snapshots only. Timing masks constrain crossing association;
    // they never substitute for the physical crossing endpoints.
    internal static class PlainCrossingPlacementPolicy
    {
        internal struct Crossing
        {
            internal Entity Lane;
            internal float3 A, B;
            internal float2 FacingA, FacingB;
            internal ushort Mask;
        }

        internal struct Support
        {
            internal Entity Actor;
            internal float3 Position;
            internal float Radius;
        }

        internal struct Plan
        {
            internal Transform Pose;
            internal Entity Crossing, Support;
        }

        private static bool SameCrossing(Crossing a, Crossing b)
            => math.distancesq(a.A, b.A) + math.distancesq(a.B, b.B) < .04f
            || math.distancesq(a.A, b.B) + math.distancesq(a.B, b.A) < .04f;

        internal static bool TryPlan(Transform original, ushort requiredMask,
            IList<Crossing> crossings, IList<Support> supports, float radius, out Plan plan)
        {
            plan = default;
            if (requiredMask == 0 || crossings == null || supports == null
                || !math.all(math.isfinite(original.m_Position))
                || !math.all(math.isfinite(original.m_Rotation.value))
                || math.lengthsq(original.m_Rotation.value) < .25f
                || !math.isfinite(radius) || radius < 0) return false;

            int selected = -1, rank = 2;
            bool sourceA = false, ambiguous = false;
            float best = 144;
            for (int i = 0; i < crossings.Count; i++)
            {
                var crossing = crossings[i];
                if ((crossing.Mask & requiredMask) == 0
                    || !math.all(math.isfinite(crossing.A)) || !math.all(math.isfinite(crossing.B))
                    || math.distancesq(crossing.A.xz, crossing.B.xz) < 4) continue;
                float da = math.distancesq(original.m_Position.xz, crossing.A.xz);
                float db = math.distancesq(original.m_Position.xz, crossing.B.xz);
                bool first = da < db;
                var end = first ? crossing.A : crossing.B;
                float distance = math.min(da, db);
                if (distance >= 144 || math.abs(original.m_Position.y - end.y) > 3) continue;
                int matchRank = crossing.Mask == requiredMask ? 0 : 1;
                if (matchRank < rank || matchRank == rank && distance < best - .04f)
                {
                    selected = i; sourceA = first; best = distance; rank = matchRank;
                    ambiguous = math.abs(da - db) < .04f;
                }
                else if (matchRank == rank && math.abs(distance - best) < .04f
                    && selected >= 0 && !SameCrossing(crossings[selected], crossing)) ambiguous = true;
            }
            if (selected < 0 || ambiguous) return false;

            var chosen = crossings[selected];
            var oldFacing = math.normalizesafe(sourceA ? chosen.FacingA : chosen.FacingB);
            if (math.lengthsq(oldFacing) < .5f) return false;
            int supportIndex = -1;
            bool targetA = false;
            best = float.MaxValue; ambiguous = false;
            // Prefer the matching original crossing end. If only the opposite
            // end has a far-side support, turn the original model with the crossing.
            for (int i = 0; i < supports.Count; i++)
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                var support = supports[i]; bool first = endpoint == 0;
                var end = first ? chosen.A : chosen.B;
                if (!math.all(math.isfinite(support.Position)) || !math.isfinite(support.Radius)
                    || support.Radius < 0 || math.abs(support.Position.y - end.y) > 3) continue;
                float distance = math.distancesq(support.Position.xz, end.xz);
                if (distance > 36) continue;
                float score = distance + (first == sourceA ? 0 : 256);
                if (score < best - .04f)
                { best = score; supportIndex = i; targetA = first; ambiguous = false; }
                else if (math.abs(score - best) < .04f && supportIndex >= 0)
                {
                    var previous = supports[supportIndex];
                    if (math.distancesq(previous.Position, support.Position) > .01f) ambiguous = true;
                    else if (support.Radius > previous.Radius
                        || support.Radius == previous.Radius && (support.Actor.Index < previous.Actor.Index
                            || support.Actor.Index == previous.Actor.Index && support.Actor.Version < previous.Actor.Version))
                    { supportIndex = i; targetA = first; }
                }
            }
            if (supportIndex < 0 || ambiguous) return false;

            var mount = supports[supportIndex];
            var facing = math.normalizesafe(targetA ? chosen.FacingA : chosen.FacingB);
            if (math.lengthsq(facing) < .5f) return false;
            float clearance = math.max(.6f, radius + mount.Radius + .15f);
            if (!math.isfinite(clearance) || clearance > 1.5f) return false;
            var side = new float3(-facing.y, 0, facing.x);
            float sign = math.dot(original.m_Position - mount.Position, side) < 0 ? -1 : 1;
            var pose = original;
            pose.m_Position = mount.Position + side * (clearance * sign);
            float turn = math.atan2(oldFacing.y * facing.x - oldFacing.x * facing.y,
                math.dot(oldFacing, facing));
            pose.m_Rotation = math.mul(quaternion.RotateY(turn), original.m_Rotation);
            plan = new Plan { Pose = pose, Crossing = chosen.Lane, Support = mount.Actor };
            return true;
        }
    }
}
