using System;
using Game.Prefabs;
namespace MedianGrassReplacement.Systems
{
    internal static class CrosswalkPaintPolicy
    {
        // Paint is a secondary visual lane; pedestrian routing lives elsewhere.
        internal static bool IsPaint(bool geometry,string name)=>geometry&&!string.IsNullOrEmpty(name)&&(name.IndexOf("Crosswalk",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Zebra",StringComparison.OrdinalIgnoreCase)>=0);
    }

    internal static class WhiteEdgePolicy
    {
        internal static bool Enabled(bool paved,bool alley,bool ordinaryEnabled,bool alleyEnabled)=>paved&&(alley?alleyEnabled:ordinaryEnabled);
    }
}
