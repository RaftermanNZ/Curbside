using System;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // A native role may occur more than once without Aligned slot metadata.
    // Exact recorded native pose disambiguates it without a nearest-road search.
    internal readonly struct NativeSignalPoseRole : IEquatable<NativeSignalPoseRole>
    {
        internal readonly NativeSignalRole Role;
        internal readonly Game.Objects.Transform Pose;
        internal NativeSignalPoseRole(NativeSignalRole role, Game.Objects.Transform pose) { Role = role; Pose = pose; }
        public bool Equals(NativeSignalPoseRole other) => Role.Equals(other.Role) && Pose.Equals(other.Pose);
        public override bool Equals(object value) => value is NativeSignalPoseRole other && Equals(other);
        public override int GetHashCode()
        { unchecked { return (Role.GetHashCode() * 397 ^ Pose.m_Position.GetHashCode()) * 397 ^ Pose.m_Rotation.value.GetHashCode(); } }
    }

    internal readonly struct NativeSignalRole : IEquatable<NativeSignalRole>
    {
        internal readonly Entity Owner, Prefab;
        internal readonly int Slot;
        internal readonly ushort Vehicle, Pedestrian;
        internal readonly bool Secondary;
        internal NativeSignalRole(Entity owner, Entity prefab, int slot, ushort vehicle, ushort pedestrian, bool secondary)
        { Owner = owner; Prefab = prefab; Slot = slot; Vehicle = vehicle; Pedestrian = pedestrian; Secondary = secondary; }
        public bool Equals(NativeSignalRole other) => Owner == other.Owner && Prefab == other.Prefab
            && Slot == other.Slot && Vehicle == other.Vehicle && Pedestrian == other.Pedestrian && Secondary == other.Secondary;
        public override bool Equals(object other) => other is NativeSignalRole role && Equals(role);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Owner.GetHashCode(); hash = hash * 397 ^ Prefab.GetHashCode();
                hash = hash * 397 ^ Slot; hash = hash * 397 ^ Vehicle;
                hash = hash * 397 ^ Pedestrian; return hash * 397 ^ (Secondary ? 1 : 0);
            }
        }
    }

    internal static class NativeSignalProjectionPolicy
    {
        internal static bool Eligible(TempFlags flags)
            => (flags & (TempFlags.Delete | TempFlags.Cancel | TempFlags.Hidden)) == 0;

        // Only the native rendering bit changes. Original, cost, value, curve
        // position and native operation flags remain the caller's current values.
        internal static Temp Suppress(Temp current)
        { current.m_Flags |= TempFlags.Hidden; return current; }

        internal static bool CanRelease(Temp current, Entity owner, Entity expectedOriginal,
            Entity expectedOwner, TempFlags appliedFlags, bool parentHidden)
            => !parentHidden && current.m_Original == expectedOriginal && owner == expectedOwner
                && current.m_Flags == appliedFlags
                && (current.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) == 0;

        internal static Temp Release(Temp current)
        { current.m_Flags &= ~TempFlags.Hidden; return current; }

        internal static bool AllowsTool(string toolID)
            => toolID == "Net Tool" || toolID == "Object Tool";
    }

    public partial class FarSideLightSystem
    {
        // Read the authoritative suppression decision used by committed lights.
        // No geometry, controller or approach rule is duplicated in a preview.
        internal bool IsSuppressedForNativeProjection(Entity source)
            => _hiddenSignals.ContainsKey(source) && _suppressedPrefabs.TryGetValue(source, out var value)
                && Live(source) && EntityManager.HasComponent<Game.Prefabs.PrefabRef>(source)
                && EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(source).m_Prefab == value.Applied;
    }
}
