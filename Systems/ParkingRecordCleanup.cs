using System.Collections.Generic;
using Game.Common;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FixedParkingSystem
    {
        private readonly List<Entity> parkingCleanupWork = new List<Entity>();
        private readonly HashSet<Entity> parkingCleanupSeen = new HashSet<Entity>();

        private void CleanupParkingRecords(HashSet<Entity> pending)
        {
            if (_originals.Count == 0) return;
            parkingCleanupWork.Clear(); parkingCleanupSeen.Clear();
            // Native deleted events are in pending. Unexpected direct destruction
            // has the same fallback as before, now bounded by the existing index.
            foreach (var lane in pending) parkingCleanupSeen.Add(lane);
            _parkingOwners.Rotate(parkingCleanupWork, 64);
            foreach (var lane in parkingCleanupWork) parkingCleanupSeen.Add(lane);
            foreach (var lane in parkingCleanupSeen)
                if (_originals.ContainsKey(lane)
                    && (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)))
                {
                    _originals.Remove(lane);
                    ForgetParkingReadiness(lane);
                    ForgetParkingIfUnused(lane);
                }
            parkingCleanupWork.Clear(); parkingCleanupSeen.Clear();
        }
    }
}
