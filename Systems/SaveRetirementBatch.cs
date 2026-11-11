using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static partial class GeneratedVisualLifecycle
    {
        // Synchronous save-boundary work only. Finish readers before borrowing
        // buffers; no structural change occurs while any buffer is in use.
        internal static void DetachSaveLanes(World world, NativeArray<Entity> entities,
            IDictionary<Entity, Entity> previousOwners = null)
        {
            if (entities.Length == 0) return;
            var manager = world.EntityManager;
            manager.CompleteAllTrackedJobs();
            var owners = new Dictionary<Entity, HashSet<Entity>>();
            foreach (var entity in entities)
            {
                if (!manager.Exists(entity)) continue;
                if (manager.HasComponent<Lane>(entity) && manager.HasComponent<Owner>(entity))
                    AddSaveLane(owners, manager.GetComponentData<Owner>(entity).m_Owner, entity);
                if (previousOwners != null && previousOwners.TryGetValue(entity, out var previous))
                    AddSaveLane(owners, previous, entity);
            }
            foreach (var pair in owners)
            {
                if (!manager.Exists(pair.Key) || !manager.HasBuffer<SubLane>(pair.Key)) continue;
                var lanes = manager.GetBuffer<SubLane>(pair.Key);
                for (int i = lanes.Length - 1; i >= 0; i--)
                    if (pair.Value.Contains(lanes[i].m_SubLane)) lanes.RemoveAt(i);
            }
        }

        private static void AddSaveLane(Dictionary<Entity, HashSet<Entity>> owners, Entity owner, Entity lane)
        {
            if (owner == Entity.Null) return;
            if (!owners.TryGetValue(owner, out var lanes)) owners.Add(owner, lanes = new HashSet<Entity>());
            lanes.Add(lane);
        }
    }

    public partial class GeneratedVisualSaveSystem
    {
        private struct RetirementStamp
        {
            internal Entity Owner;
            internal bool HadOwner, WasLane, HadTransform;
        }

        private readonly Dictionary<Entity, RetirementStamp> earlyCompleted = new Dictionary<Entity, RetirementStamp>();
        private readonly Dictionary<Entity, Entity> earlyLaneOwners = new Dictionary<Entity, Entity>();

        private void RecordEarlyCompletion(NativeArray<Entity> entities)
        {
            foreach (var entity in entities)
            {
                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Deleted>(entity)) continue;
                var stamp = new RetirementStamp
                {
                    HadOwner = EntityManager.HasComponent<Owner>(entity),
                    WasLane = EntityManager.HasComponent<Lane>(entity),
                    HadTransform = EntityManager.HasComponent<Game.Objects.Transform>(entity)
                };
                if (stamp.HadOwner) stamp.Owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                earlyCompleted[entity] = stamp;
                if (stamp.WasLane && stamp.HadOwner) earlyLaneOwners[entity] = stamp.Owner;
            }
        }

        internal bool CompletedEarlyRetirement(Entity entity)
        {
            // Deleted alone is insufficient. A receipt exists only after both
            // grouped membership removal and spatial retirement succeeded.
            return Mod.Saving && earlyCompleted.TryGetValue(entity, out var stamp)
                && EntityManager.Exists(entity) && EntityManager.HasComponent<Deleted>(entity)
                && EntityManager.HasComponent<Lane>(entity) == stamp.WasLane
                && EntityManager.HasComponent<Game.Objects.Transform>(entity) == stamp.HadTransform
                && EntityManager.HasComponent<Owner>(entity) == stamp.HadOwner
                && (!stamp.HadOwner || EntityManager.GetComponentData<Owner>(entity).m_Owner == stamp.Owner);
        }

        private void ClearEarlyCompletion()
        {
            earlyCompleted.Clear();
            earlyLaneOwners.Clear();
        }
    }
}
