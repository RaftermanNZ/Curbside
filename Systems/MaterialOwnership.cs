using System.Collections.Generic;
namespace MedianGrassReplacement.Systems
{
    internal static class MaterialOwnership
    {
        internal static bool CanRestore<T>(bool owned, T current, T applied) => owned && EqualityComparer<T>.Default.Equals(current, applied);
        // An explicit reapplication may take over a property edited by another mod.
        // Remember that newer value, rather than restoring an obsolete baseline later.
        internal static T Baseline<T>(bool owned, T current, T applied, T before) => CanRestore(owned, current, applied) ? before : current;
    }
}
