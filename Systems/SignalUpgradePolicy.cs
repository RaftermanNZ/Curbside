using Game.Prefabs;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalUpgradePolicy
    {
        // Appearance changes use the existing controller/actors. Only enabling
        // absent lights, replacing conflicting controls or removing lights needs a rebuild.
        internal static bool NeedsNativeRebuild(bool remove,bool hasController,CompositionFlags.General current)
            =>remove||!hasController||(current&(CompositionFlags.General.AllWayStop|CompositionFlags.General.RemoveTrafficLights))!=0;

        internal static CompositionFlags.General SetSignalControl(CompositionFlags.General current,bool remove)
            => remove
                ? (current & ~CompositionFlags.General.TrafficLights) | CompositionFlags.General.RemoveTrafficLights
                : (current & ~(CompositionFlags.General.RemoveTrafficLights | CompositionFlags.General.AllWayStop)) | CompositionFlags.General.TrafficLights;

        internal static bool NeedsVisualRefresh(bool nativeRebuild,bool customStyle,bool plainFromCustom,bool hasUpgrade)
            =>nativeRebuild||customStyle||plainFromCustom||!hasUpgrade;
    }
}
