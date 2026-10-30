using System;

namespace MedianGrassReplacement.Systems

{

    internal static class JunctionMarkingPolicy

    {

        internal static bool Suppress(string name, int roadCount, bool curbLines)

        {

            if (!curbLines || roadCount < 2 || string.IsNullOrEmpty(name)) return false;

            // Only longitudinal edge paint, never stop bars, pedestrian

            // crossings, arrows, or intersection guidance/center lines.

            return name.IndexOf("Edge Line", StringComparison.OrdinalIgnoreCase) >= 0

                && name.IndexOf("Yellow", StringComparison.OrdinalIgnoreCase) < 0;

        }

    }

}

