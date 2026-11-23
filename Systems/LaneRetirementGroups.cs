using System.Collections.Generic;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Reusable IDs only, never ECS buffers or native tree views. Unique-owner
    // groups store an actor inline. Shared groups borrow cleared owner-local
    // sets by workspace slot, retaining the original cheap membership lookup.
    internal sealed class LaneRetirementGroups
    {
        internal struct Group
        {
            internal Entity First;
            internal HashSet<Entity> Shared;
        }

        private readonly Dictionary<Entity, Group> owners = new Dictionary<Entity, Group>();
        private readonly List<HashSet<Entity>> sharedSets = new List<HashSet<Entity>>();
        private int usedShared;
        internal Dictionary<Entity, Group> Entries => owners;
        internal bool InUse { get; private set; }

        internal bool TryBegin()
        {
            if (InUse) return false;
            InUse = true;
            return true;
        }

        internal void Add(Entity owner, Entity actor)
        {
            if (owner == Entity.Null) return;
            if (!owners.TryGetValue(owner, out var group))
            {
                owners.Add(owner, new Group { First = actor });
                return;
            }
            if (group.First == actor) return;
            if (group.Shared == null)
            {
                if (usedShared == sharedSets.Count) sharedSets.Add(new HashSet<Entity>());
                group.Shared = sharedSets[usedShared++];
                group.Shared.Add(group.First);
                owners[owner] = group;
            }
            group.Shared.Add(actor);
        }

        internal void End()
        {
            ClearStorage(); InUse = false;
        }

        internal void ClearIfIdle()
        {
            if (!InUse) ClearStorage();
        }

        private void ClearStorage()
        {
            owners.Clear();
            for (int i = 0; i < usedShared; i++) sharedSets[i].Clear();
            usedShared = 0;
        }
    }
}
