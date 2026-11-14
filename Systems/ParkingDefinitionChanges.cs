using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FixedParkingSystem
    {
        private void SetParkingDefinition(Entity lane, Entity prefab)
        {
            // These copies change ParkingLaneData only. A parking lane is
            // network simulation data, not a signal/prop presentation actor.
            // Keep the visual lifecycle path for any foreign object reference
            // found by save cleanup; its rendering safety contract still applies.
            if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)
                || !EntityManager.HasComponent<PrefabRef>(lane)) return;
            if (!EntityManager.HasComponent<Game.Net.ParkingLane>(lane)
                || EntityManager.HasComponent<Game.Objects.Transform>(lane)
                || !EntityManager.Exists(prefab) || !EntityManager.HasComponent<ParkingLaneData>(prefab)
                || !EntityManager.HasComponent<NetLaneData>(prefab))
            {
                NativeVisualInvalidation.SetPrefab(World, lane, prefab);
                return;
            }

            if (EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab == prefab) return;

            // EntityManager completes the PrefabRef read/write dependencies.
            // Preserve Updated for native parking/pathfinding consumers, without
            // culling/effect resets, spatial flags or a CompleteAllTrackedJobs.
            // Restore the native key before LaneSystem and the private slot
            // definition afterward; native lane identity/reuse is unchanged.
            EntityManager.SetComponentData(lane, new PrefabRef(prefab));
            if (!EntityManager.HasComponent<Updated>(lane)) EntityManager.AddComponent<Updated>(lane);
        }
    }
}
