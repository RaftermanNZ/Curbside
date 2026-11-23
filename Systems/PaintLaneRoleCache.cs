using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Only archetype membership is shared. Values and buffer contents are still
    // read for each lane. Archetypes are immutable; an entity that moves to a
    // different archetype obtains a different key even inside this phase.
    internal sealed class PaintLaneRoleCache
    {
        private const int Capacity = 64;
        private readonly Dictionary<EntityArchetype, PaintLaneRoles> entries = new Dictionary<EntityArchetype, PaintLaneRoles>();
        internal void Clear() => entries.Clear();
        internal PaintLaneRoles Read(EntityManager em, Entity lane)
        {
            var archetype = em.GetStorageInfo(lane).Chunk.Archetype;
            if (entries.TryGetValue(archetype, out var roles)) return roles;
            roles = new PaintLaneRoles {
                Deleted=em.HasComponent<Deleted>(lane), Temporary=em.HasComponent<Game.Tools.Temp>(lane),
                Generated=em.HasComponent<MGRCurbMarking>(lane), Lane=em.HasComponent<Lane>(lane),
                Car=em.HasComponent<Game.Net.CarLane>(lane), Track=em.HasComponent<Game.Net.TrackLane>(lane),
                Parking=em.HasComponent<Game.Net.ParkingLane>(lane), Pedestrian=em.HasComponent<Game.Net.PedestrianLane>(lane),
                PaintGeometry=em.HasComponent<Game.Net.LaneGeometry>(lane), HiddenJunction=em.HasComponent<MGRHiddenJunctionLine>(lane),
                Prefab=em.HasComponent<PrefabRef>(lane), Owner=em.HasComponent<Owner>(lane),
                Curve=em.HasComponent<Curve>(lane), EdgeLane=em.HasComponent<EdgeLane>(lane), Master=em.HasComponent<MasterLane>(lane)
            };
            if (entries.Count < Capacity) entries.Add(archetype, roles);
            return roles;
        }
    }
    internal struct PaintLaneRoles
    {
        internal bool Deleted, Temporary, Generated, Lane, Car, Track, Parking,
            Pedestrian, PaintGeometry, HiddenJunction, Prefab, Owner, Curve, EdgeLane, Master;
    }
}
