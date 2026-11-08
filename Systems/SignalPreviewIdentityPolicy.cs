using Game.Objects;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalPreviewIdentityPolicy
    {
        internal static bool Customized(Entity original, Entity applied, Transform native, Transform visual)
            => original != applied || !native.Equals(visual);

        internal static bool SameSlot(int preview, int source) => preview >= 0 && preview == source;

        internal static bool CompatibleMask(ushort requested, ushort source)
            => requested == 0 || source == 0 || (requested & source) != 0;

        internal static bool TrySecondaryScore(Transform generated, Transform native, out float score)
        {
            score = math.distancesq(generated.m_Position, native.m_Position);
            // The native secondary producer compares positions inside its owner
            // and prefab bucket. Use the recorded native pose, never a far-side
            // applied pose, with a bounded distance and explicit tie rejection.
            return math.isfinite(score) && score < 144f;
        }

        internal static bool Better(float score, float best) => score < best - .0001f;
        internal static bool Tied(float score, float best) => math.abs(score - best) <= .0001f;

        internal static bool CopiesOriginalPose(bool isStatic, bool hasOriginal, TempFlags flags)
            => hasOriginal && (flags & TempFlags.Dragging) == 0
                && (!isStatic || (flags & (TempFlags.Create | TempFlags.Modify)) == 0);

        internal static Temp WithOriginal(Temp current, Entity original)
        {
            // Identity only: tool cost, curve position, value and every native
            // creation/modification/cancellation flag keep their current values.
            current.m_Original = original;
            return current;
        }
    }
}
