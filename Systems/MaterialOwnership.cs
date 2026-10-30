using System.Collections.Generic;
namespace MedianGrassReplacement.Systems
{
    internal static class MaterialOwnership
    {
        internal static bool CanRestore<T>(bool owned, T current, T applied) => owned && EqualityComparer<T>.Default.Equals(current, applied);
    }
}
