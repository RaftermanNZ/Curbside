namespace MedianGrassReplacement.Systems
{
    internal static class DecalPolicy

    {

        internal static byte Apply(byte existing, byte selection, bool remove) => (byte)((remove ? existing & ~selection : existing | selection) & 3);

        internal static bool Changes(byte existing, byte selection, bool remove) => Apply(existing, selection, remove) != existing;

        internal static byte Classify(bool decal, bool laneDirection, bool speedSign, string name)

        {

            if (!decal) return 0;

            if (laneDirection) return 1;

            if (speedSign || (!string.IsNullOrEmpty(name) && name.Replace(" ", "").Replace("_", "").Replace("-", "").IndexOf("speedlimit", System.StringComparison.OrdinalIgnoreCase) >= 0)) return 2;

            return 0;

        }

    }

}
