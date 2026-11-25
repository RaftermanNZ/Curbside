namespace MedianGrassReplacement.Systems
{
    internal enum JunctionPaintOrigin : byte
    {
        Other,
        NativeCurb,
        RoadSectionLead,
        SidewalkLaneFallback,
        TangentFallback,
        ClearanceAdjustedFallback,
        TwoRoadJoin,
        HighwayJoin,
        NativeLaneReuse
    }
}
