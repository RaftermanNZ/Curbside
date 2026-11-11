using System.Collections.Generic;
namespace MedianGrassReplacement.Systems
{
    // Derived values for one committed-node pass; never serialized or cached
    // across native changes. ReadSignalCoverage admits only owned, applied,
    // unhidden replacements at their expected pose.
    internal struct SignalCoverageEntry
    {
        internal int Approach;
        internal ushort Vehicle, Pedestrian;
    }
    internal static class SignalCoveragePolicy
    {
        internal static bool Ready(bool sameOwner,bool sameDefinition,bool samePose,
            bool ownedHidden,bool toolHidden,bool overridden)
            =>sameOwner&&sameDefinition&&samePose&&!ownedHidden&&!toolHidden&&!overridden;

        internal static bool SameApproach(int required,int replacement)
            =>required>=0&&required==replacement;

        internal static bool Covered(int approach,ushort requiredVehicle,ushort requiredPedestrian,
            bool needsVehicle,bool needsPedestrian,IList<SignalCoverageEntry> visible)
        {
            ushort vehicle=0,pedestrian=0;
            if(visible!=null)foreach(var replacement in visible)
            {
                if(SameApproach(approach,replacement.Approach))vehicle|=replacement.Vehicle;
                // Native crossing groups are independent of the vehicle road
                // mouth; retain their existing coverage contract separately.
                pedestrian|=replacement.Pedestrian;
            }
            return SignalBindingPolicy.Covered(requiredVehicle,requiredPedestrian,needsVehicle,needsPedestrian,vehicle,pedestrian);
        }
    }
}
