using Game.Common;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    // Call after an owned live prefab/pose restoration, including save preparation.
    // Native systems retain responsibility for rebuilding their buffers and effects.
    internal static class NativeVisualInvalidation
    {
        internal static void Mark(EntityManager manager,Entity entity)
        {
            if(!manager.Exists(entity)||manager.HasComponent<Deleted>(entity))return;
            if(!manager.HasComponent<Updated>(entity))manager.AddComponent<Updated>(entity);
            if(!manager.HasComponent<BatchesUpdated>(entity))manager.AddComponent<BatchesUpdated>(entity);
            if(!manager.HasComponent<EffectsUpdated>(entity))manager.AddComponent<EffectsUpdated>(entity);
        }
    }
}
