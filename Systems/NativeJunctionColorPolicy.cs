namespace MedianGrassReplacement.Systems
{
    internal static class NativeJunctionColorPolicy
    {
        internal static byte ThemedTarget(bool opposing,bool edge,bool european,bool themeEdges,bool followCity)
            =>followCity?(european||!opposing?(byte)1:(byte)2):Target(opposing,edge,european,themeEdges);
        // 0: retain source, 1: exact white variant, 2: exact yellow variant.
        internal static byte Target(bool opposing,bool edge,bool european,bool themeEdges)
        {
            // This policy is only for opt-in native recoloring. A legacy shape
            // selection of "Vanilla" must not silently disable its color theme.
            if(opposing)return european?(byte)1:(byte)2;
            return edge&&european&&themeEdges?(byte)1:(byte)0;
        }
    }
}
