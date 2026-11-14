using System.Collections.Generic;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class IntersectionPaintSystem
    {
        private readonly HashSet<Entity> _changedOwnerPaintSources = new HashSet<Entity>();

        private bool OwnerPaintCandidate(Entity source) => _replacements.ContainsKey(source)
            || (EntityManager.Exists(source)
                && !EntityManager.HasComponent<Game.Common.Deleted>(source)
                && !EntityManager.HasComponent<Game.Net.CarLane>(source)
                && !EntityManager.HasComponent<Game.Net.ParkingLane>(source)
                && !EntityManager.HasComponent<Game.Net.TrackLane>(source)
                && EntityManager.HasComponent<Game.Net.Curve>(source)
                && EntityManager.HasComponent<Game.Prefabs.PrefabRef>(source)
                && EntityManager.HasComponent<Game.Common.Owner>(source)
                && !KnownPathOnlySource(source));

        private bool KnownPathOnlySource(Entity source)
        {
            if (!OwnerInputEquality.Supported || _ownerNotifications.KnownNonPaintPrefab == null
                || EntityManager.HasComponent<Game.Net.LaneGeometry>(source)) return false;
            var prefab = EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(source).m_Prefab;
            return EntityManager.Exists(prefab)
                && !EntityManager.HasComponent<Game.Prefabs.NetLaneGeometryData>(prefab)
                && _ownerNotifications.KnownNonPaintPrefab(prefab);
        }

        private void CollectChangedOwnerPaint(Entity owner, HashSet<Entity> output)
        {
            foreach (var lane in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true))
            {
                var source = lane.m_SubLane;
                // Owners include buildings and their access/parking lanes. Only
                // candidates admitted by Reconcile need processing. Do not use
                // the narrower initial-scan query here: late/native producers
                // may not yet carry all of that query's component tags. Keep tracked
                // actors even if they ceased matching, so cleanup is preserved.
                if (OwnerPaintCandidate(source)) output.Add(source);
            }
        }
    }
}
