using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FixedParkingSystem
    {
        private void RequestRestoredParkingPaint(Entity lane, Entity original)
        {
            if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)
                || EntityManager.HasComponent<Game.Tools.Temp>(lane)) return;

            if (EntityManager.HasComponent<ParkingLane>(lane)
                && !EntityManager.HasComponent<Game.Objects.Transform>(lane)
                && EntityManager.HasComponent<Owner>(lane)
                && EntityManager.HasComponent<Game.Prefabs.ParkingLaneData>(original)
                && EntityManager.HasComponent<Game.Prefabs.NetLaneData>(original))
            {
                var road = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                if (EntityManager.Exists(road) && !EntityManager.HasComponent<Deleted>(road)
                    && !EntityManager.HasComponent<Game.Tools.Temp>(road)
                    && EntityManager.HasComponent<Road>(road) && EntityManager.HasComponent<Edge>(road))
                {
                    // Required road work preserves native-source fanout even
                    // when replacement curves themselves compare unchanged.
                    CurbLineEmissionSystem.QueueRoad(road);
                    return;
                }
            }

            // Foreign copies/reparented objects cannot be safely localized.
            CurbLineEmissionSystem.RequestRoadRefresh();
        }
    }
}
