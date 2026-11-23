using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Managed entity/version IDs only. Observe creation and existing maintenance
    // batches; never query roads or retain ECS buffer views here.
    internal sealed class GeneratedLaneOwnerHistory
    {
        private readonly Dictionary<Entity, Entity> latest = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, HashSet<Entity>> previous = new Dictionary<Entity, HashSet<Entity>>();
        internal readonly HashSet<Entity> RetirementOwners = new HashSet<Entity>();
        internal readonly LaneRetirementGroups RetirementGroups = new LaneRetirementGroups();
        internal bool IsActive { get; private set; } = true;

        internal void Remember(Entity actor, Entity owner)
        {
            if (actor == Entity.Null || owner == Entity.Null) return;
            if (latest.TryGetValue(actor, out var before))
            {
                if (before == owner) return;
                if (!previous.TryGetValue(actor, out var owners))
                    previous.Add(actor, owners = new HashSet<Entity>());
                owners.Add(before);
            }
            latest[actor] = owner;
        }

        internal void CopyOwners(Entity actor, HashSet<Entity> output, Entity accountedFor = default)
        {
            latest.TryGetValue(actor, out var owner);
            if (owner != Entity.Null && owner != accountedFor) output.Add(owner);
            if (previous.Count != 0 && previous.TryGetValue(actor, out var owners))
                foreach (var prior in owners)
                    if (prior != owner && prior != accountedFor) output.Add(prior);
        }

        internal void AddToBatch(Entity actor, LaneRetirementGroups batch,
            Entity accountedFor = default, Entity otherAccountedFor = default)
        {
            latest.TryGetValue(actor, out var owner);
            if (owner != Entity.Null && owner != accountedFor && owner != otherAccountedFor) batch.Add(owner, actor);
            if (previous.Count != 0 && previous.TryGetValue(actor, out var owners))
                foreach (var prior in owners)
                    if (prior != owner && prior != accountedFor && prior != otherAccountedFor) batch.Add(prior, actor);
        }

        internal void Forget(Entity actor) { latest.Remove(actor); previous.Remove(actor); }
        internal void Clear() { latest.Clear(); previous.Clear(); RetirementOwners.Clear(); RetirementGroups.ClearIfIdle(); }
        internal void Deactivate() { Clear(); IsActive = false; }
    }

    public partial class VisualLifecycleSystem
    {
        internal readonly GeneratedLaneOwnerHistory LaneOwners = new GeneratedLaneOwnerHistory();
    }

    internal static partial class GeneratedVisualLifecycle
    {
        internal static void RememberLaneOwner(World world, Entity actor, Entity owner)
            => world.GetOrCreateSystemManaged<VisualLifecycleSystem>().LaneOwners.Remember(actor, owner);

        private static void DetachGeneratedLaneMembership(World world, Entity actor, GeneratedLaneOwnerHistory history)
        {
            var manager = world.EntityManager;
            Entity current = Entity.Null;
            if (manager.Exists(actor) && manager.HasComponent<Lane>(actor) && manager.HasComponent<Owner>(actor))
                current = manager.GetComponentData<Owner>(actor).m_Owner;
            if (manager.Exists(current) && manager.HasBuffer<SubLane>(current))
                DetachLaneMembership(manager, current, actor);

            if (history == null) return;
            var owners = history.RetirementOwners;
            owners.Clear();
            try
            {
                history.CopyOwners(actor, owners, current);
                foreach (var owner in owners)
                    if (owner != current && manager.Exists(owner) && manager.HasBuffer<SubLane>(owner))
                        DetachLaneMembership(manager, owner, actor);
            }
            finally { owners.Clear(); }
        }
    }
}
