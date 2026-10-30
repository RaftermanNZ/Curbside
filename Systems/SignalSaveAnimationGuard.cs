using Game.Rendering;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Save-only guard. Do not change native buffers, culling records or allocators.
    // The native interpolation jobs cache ProceduralLight/Emissive buffer lookups.
    // Stop new jobs before save-time prefab restoration and join existing readers.
    internal static class SignalSaveAnimationGuard
    {
        private static ObjectInterpolateSystem animation;
        private static World owner;
        private static bool previousEnabled;

        internal static void Begin(World world)
        {
            if (animation != null || world == null || !world.IsCreated) return;
            var system = world.GetExistingSystemManaged<ObjectInterpolateSystem>();
            if (system == null) return;
            owner = world;
            animation = system;
            previousEnabled = system.Enabled;
            system.Enabled = false;
            try
            {
            var culling = world.GetExistingSystemManaged<PreCullingSystem>();
            if (culling != null) { culling.GetCullingData(false, out var readers); readers.Complete(); }
            var effects = world.GetExistingSystemManaged<Game.Effects.EffectControlSystem>();
            if (effects != null) { effects.GetEnabledData(false, out var readers); readers.Complete(); }
            world.EntityManager.CompleteAllTrackedJobs();
            Mod.Log.Info("Save animation guard: native interpolation suspended; culling/effect readers joined. Previous enabled=" + previousEnabled);
            }
            catch
            {
                End(); // A managed fence failure must not leave animation disabled.
                throw;
            }
        }

        internal static void End()
        {
            var system = animation;
            var world = owner;
            animation = null;
            owner = null;
            if (system != null && world != null && world.IsCreated)
            {
                system.Enabled = previousEnabled;
                Mod.Log.Info("Save animation guard: native interpolation restored to enabled=" + previousEnabled);
            }
        }
    }
}
