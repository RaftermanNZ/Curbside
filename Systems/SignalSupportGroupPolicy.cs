using System.Collections.Generic;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalSupportGroupPolicy
    {
        internal static bool FullyMapped(ushort phase, IList<ushort> lanes)
        {
            if (phase == 0 || lanes == null) return false;
            ushort mapped = 0;
            foreach (ushort lane in lanes) mapped |= lane;
            return (mapped & phase) == phase;
        }
        // Phase controllers remain separate entities. Only their visual support
        // may be shared, after native lane assignments prove their heads distinct.
        internal static bool CanShare(FarSideLightPolicy.Approach road, IList<ushort> phases)
        {
            if (phases == null || phases.Count < 2 || road.Incoming == null ||
                road.LaneMasks == null || road.Incoming.Count != road.LaneMasks.Count)
                return false;

            ushort used = 0;
            foreach (ushort phase in phases)
            {
                if (phase == 0 || (phase & used) != 0) return false;
                // Every movement in a combined phase must be accounted for.
                // A partial overlap cannot prove that two heads may share a
                // support. Preserve their separate native actors if uncertain.
                if (!FullyMapped(phase, road.LaneMasks)) return false;
                used |= phase;
            }

            // A shared lane with multiple independently controlled movements
            // needs a different head arrangement. Do not overlap its heads.
            foreach (ushort lane in road.LaneMasks)
            {
                int owners = 0;
                foreach (ushort phase in phases)
                    if ((lane & phase) != 0) owners++;
                if (owners > 1) return false;
            }
            return true;
        }
    }
}
