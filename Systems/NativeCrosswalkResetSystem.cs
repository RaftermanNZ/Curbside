using Game;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Native input/selection/commit owns the transaction. No cursor paint bridge.
    public partial class NativeCrosswalkResetSystem : GameSystemBase
    {
        internal void CaptureCrosswalkInput()
        {
            if (!Mod.Saving) World.GetExistingSystemManaged<CrosswalkUpgradeSystem>()?.ApplyPendingNativeMode();
        }
        internal void PrepareInvisibleRestore() { }
        internal void ReleaseInput() { Enabled = false; }
        // Only disabled legacy cosmetic consumers retain this signature.
        internal bool TryPreviewTarget(out Entity road, out Entity node, out Entity source, out float3 point, out float2 axis)
        { road = node = source = Entity.Null; point = default; axis = default; return false; }
        protected override void OnUpdate() { }
    }
}
