using System.Collections.Generic;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FixedParkingSystem
    {
        // Exactly the lanes in our existing definition/restriction records. No
        // native views, timer, extra recovery sweep or independent city scan.
        private readonly ManagedWorkIndex<Entity, Entity> _parkingOwners = new ManagedWorkIndex<Entity, Entity>();
        private readonly List<Entity> _restoreLanes = new List<Entity>();
        private readonly HashSet<Entity> _restoreMembers = new HashSet<Entity>();
        private readonly HashSet<Entity> _restoreOwners = new HashSet<Entity>();

        private bool HasParkingChoice(Entity road)
            => EntityManager.HasComponent<MGRParkingSides>(road)
                || EntityManager.HasComponent<MGRParkingUpgrade>(road)
                || EntityManager.HasComponent<MGRParkingBanSides>(road);

        private void ForgetParkingIfUnused(Entity lane)
        {
            if (!_originals.ContainsKey(lane) && !_restrictions.ContainsKey(lane)) _parkingOwners.Remove(lane);
        }

        private static bool ContainsOwnedOverlap(DynamicBuffer<LaneOverlap> buffer, List<LaneOverlap> owned)
        {
            foreach (var entry in owned)
                for (int i = 0; i < buffer.Length; i++)
                    if (buffer[i].Equals(entry)) return true;
            return false;
        }
    }
}
