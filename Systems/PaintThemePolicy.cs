using System;
namespace MedianGrassReplacement.Systems
{
    internal static class PaintThemePolicy
    {
        internal static byte WhiteShape(string key)
        {
            if(string.IsNullOrEmpty(key)||key.IndexOf("Yellow",StringComparison.OrdinalIgnoreCase)<0)return 0;
            if(key.IndexOf("Double",StringComparison.OrdinalIgnoreCase)>=0)return 2;
            if(key.IndexOf("Dash",StringComparison.OrdinalIgnoreCase)>=0)return 3;
            return 1;
        }
    }
}
