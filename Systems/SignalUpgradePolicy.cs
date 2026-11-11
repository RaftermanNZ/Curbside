namespace MedianGrassReplacement.Systems
{
    internal static class SignalUpgradePolicy
    {
        // Appearance changes use the existing controller/actors. Only enabling
        // absent lights or removing them requires native network generation.
        internal static bool NeedsNativeRebuild(bool remove,bool hasController)
            =>remove||!hasController;

        internal static bool NeedsVisualRefresh(bool nativeRebuild,bool customStyle,bool plainFromCustom,bool hasUpgrade)
            =>nativeRebuild||customStyle||plainFromCustom||!hasUpgrade;
    }
}
