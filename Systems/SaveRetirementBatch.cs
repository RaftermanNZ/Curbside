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
            var history = world.GetExistingSystemManaged<VisualLifecycleSystem>()?.LaneOwners;
            var owners = history?.RetirementGroups;
            // Nested cleanup owns a separate workspace, so it cannot mutate
            // an outer owner's enumeration. The usual path reuses capacity.
            if (owners == null || !owners.TryBegin())
            {
                owners = new LaneRetirementGroups();
                owners.TryBegin();
            }
            try
            {
                foreach (var entity in entities)
                {
                    Entity current = Entity.Null, previous = Entity.Null;
                    if (manager.Exists(entity) && manager.HasComponent<Lane>(entity) && manager.HasComponent<Owner>(entity))
                    {
                        current = manager.GetComponentData<Owner>(entity).m_Owner;
                        AddSaveLane(owners, current, entity);
                    }
                    if (previousOwners != null && previousOwners.TryGetValue(entity, out previous) && previous != current)
                        AddSaveLane(owners, previous, entity);
                    history?.AddToBatch(entity, owners, current, previous);
                }
                foreach (var pair in owners.Entries)
                {
                    if (!manager.Exists(pair.Key) || !manager.HasBuffer<SubLane>(pair.Key)) continue;
                    // Unique-owner groups need direct entity equality, not a set
                    // lookup for every lane element. Keep the same RO probe and
                    // conditional writable access as single-actor retirement.
                    if (pair.Value.Shared == null)
                    {
                        DetachLaneMembership(manager, pair.Key, pair.Value.First);
                        continue;
                    }
                    // Read-only probing avoids announcing a buffer change when
                    // these generated actors were never members of native SubLane.
                    bool found = false;
                    {
                        var existing = manager.GetBuffer<SubLane>(pair.Key, true);
                        foreach (var lane in existing)
                            if (pair.Value.Shared.Contains(lane.m_SubLane)) { found = true; break; }
                    }
                    if (!found) continue;

                    var lanes = manager.GetBuffer<SubLane>(pair.Key);
                    for (int i = lanes.Length - 1; i >= 0; i--)
                        if (pair.Value.Shared.Contains(lanes[i].m_SubLane)) lanes.RemoveAt(i);
                }
            }
            finally { owners.End(); }
        }

        private static void AddSaveLane(LaneRetirementGroups owners, Entity owner, Entity lane)
        {
            owners.Add(owner, lane);
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
