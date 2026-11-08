namespace MedianGrassReplacement.Systems
{
    internal static class PaintPreviewVisibilityPolicy
    {
        internal static bool Suppress(bool originalExists,bool owned,bool simulationLane,bool external)
            => originalExists&&owned&&!simulationLane&&!external;
    }

    // Routed/simulation lanes are excluded. Plain paint uses render-only preview
    // aliases; object decals retain their existing owned-flag fallback.
}
