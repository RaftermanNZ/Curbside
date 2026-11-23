using System;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace MedianGrassReplacement.Systems
{
    internal enum NativeCrosswalkAppearance : byte { Zebra, Lines, Invisible, Restore }

    internal readonly struct NativeCrosswalkApproach : IEquatable<NativeCrosswalkApproach>
    {
        internal readonly Entity Road, Node;
        internal NativeCrosswalkApproach(Entity road, Entity node) { Road = road; Node = node; }
        public bool Equals(NativeCrosswalkApproach other) => Road == other.Road && Node == other.Node;
        public override bool Equals(object value) => value is NativeCrosswalkApproach other && Equals(other);
        public override int GetHashCode() => unchecked(Road.GetHashCode() * 397 ^ Node.GetHashCode());
    }

    internal readonly struct NativeCrosswalkPair : IEquatable<NativeCrosswalkPair>
    {
        internal readonly Entity A, B;
        internal NativeCrosswalkPair(Entity a, Entity b)
        {
            bool swap = a.Index > b.Index || a.Index == b.Index && a.Version > b.Version;
            A = swap ? b : a; B = swap ? a : b;
        }
        public bool Equals(NativeCrosswalkPair other) => A == other.A && B == other.B;
        public override bool Equals(object value) => value is NativeCrosswalkPair other && Equals(other);
        public override int GetHashCode() => unchecked(A.GetHashCode() * 397 ^ B.GetHashCode());
    }

    internal struct NativeCrosswalkSelection
    {
        internal Entity A, B;
        internal CompositionFlags Add, Remove;
    }

    internal static class NativeCrosswalkDragPolicy
    {
        internal const CompositionFlags.Side Crossing = CompositionFlags.Side.AddCrosswalk | CompositionFlags.Side.RemoveCrosswalk;
        internal static bool Selected(CompositionFlags.General endpointFlags, NativeCrosswalkSelection selection)
        {
            bool left = (endpointFlags & CompositionFlags.General.Invert) != 0;
            return ((left ? selection.Add.m_Left | selection.Remove.m_Left : selection.Add.m_Right | selection.Remove.m_Right) & Crossing) != 0;
        }
        internal static NetToolSystem.UpgradeState RestoreRouting(NetToolSystem.UpgradeState state)
        {
            // The native snapshot already identifies the selected approaches.
            // Preserve an existing crossing; never create one on an empty end.
            var left = Normalize(state.m_AddFlags.m_Left, state.m_RemoveFlags.m_Left, state.m_OldFlags.m_Left);
            var right = Normalize(state.m_AddFlags.m_Right, state.m_RemoveFlags.m_Right, state.m_OldFlags.m_Right);
            state.m_AddFlags.m_Left = left.Add; state.m_RemoveFlags.m_Left = left.Remove;
            state.m_AddFlags.m_Right = right.Add; state.m_RemoveFlags.m_Right = right.Remove;
            return state;
        }
        private static (CompositionFlags.Side Add, CompositionFlags.Side Remove) Normalize(
            CompositionFlags.Side add, CompositionFlags.Side remove, CompositionFlags.Side old)
        {
            if (((add | remove) & Crossing) == 0) return (add, remove);
            bool exists = (old & CompositionFlags.Side.AddCrosswalk) != 0;
            return ((add & ~Crossing) | (exists ? CompositionFlags.Side.AddCrosswalk : 0),
                (remove & ~Crossing) | (exists ? CompositionFlags.Side.RemoveCrosswalk : 0));
        }
    }

    [BurstCompile]
    internal struct CaptureNativeCrosswalkSelectionJob : IJob
    {
        [ReadOnly] internal NativeList<ControlPoint> Controls;
        [ReadOnly] internal NativeList<NetToolSystem.UpgradeState> States;
        internal NativeList<NativeCrosswalkSelection> Output;
        public void Execute()
        {
            Output.Clear();
            if (Controls.Length < 2 || States.Length > (Controls.Length - 2) / 2) return;
            for (int i = 0; i < States.Length; i++)
            {
                var state = States[i];
                if (!state.m_IsUpgrading) continue;
                var a = Controls[2 * i + 1].m_OriginalEntity;
                var b = Controls[2 * i + 2].m_OriginalEntity;
                if (a == Entity.Null || b == Entity.Null) continue;
                Output.Add(new NativeCrosswalkSelection { A = a, B = b, Add = state.m_AddFlags, Remove = state.m_RemoveFlags });
            }
        }
    }

    [BurstCompile]
    internal struct RestoreNativeCrosswalkRoutingJob : IJob
    {
        internal NativeList<NetToolSystem.UpgradeState> States;
        public void Execute()
        {
            for (int i = 0; i < States.Length; i++)
                if (States[i].m_IsUpgrading) States[i] = NativeCrosswalkDragPolicy.RestoreRouting(States[i]);
        }
    }
}
