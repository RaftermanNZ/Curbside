using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class DecalVisibilitySystem
    {
        private readonly ManagedWorkIndex<Entity, Entity> hiddenDecalOwners = new ManagedWorkIndex<Entity, Entity>();
        private readonly List<Entity> hiddenDecalScratch = new List<Entity>();
        private EntityQuery hiddenDecalMarkerChanges;
        private bool hiddenDecalIndexReady;

        internal static bool SameHiddenDecal(RDCHiddenDecal a, RDCHiddenDecal b)
            => a.Road == b.Road && a.Prefab == b.Prefab && a.Owner == b.Owner && a.Kind == b.Kind;

        private void InitializeHiddenDecalIndex()
        {
            // Distinct query shape: do not share the mutable filter with _hidden
            // or the PrefabRef/Owner change query. Includes private marker edits.
            hiddenDecalMarkerChanges = GetEntityQuery(ComponentType.ReadOnly<RDCHiddenDecal>(), ComponentType.ReadOnly<Game.Prefabs.PrefabRef>());
            hiddenDecalMarkerChanges.SetChangedVersionFilter(ComponentType.ReadOnly<RDCHiddenDecal>());
        }

        private void ClearHiddenDecalIndex()
        {
            hiddenDecalOwners.Clear(); hiddenDecalScratch.Clear(); hiddenDecalIndexReady = false;
        }

        private void TrackHiddenDecal(Entity actor)
        {
            if (EntityManager.Exists(actor) && EntityManager.HasComponent<RDCHiddenDecal>(actor))
                hiddenDecalOwners.Add(EntityManager.GetComponentData<RDCHiddenDecal>(actor).Road, actor);
        }

        private void CollectHiddenDecalWork(HashSet<Entity> pending, bool initial, HashSet<Entity> work)
        {
            if (!hiddenDecalIndexReady || initial)
            {
                hiddenDecalOwners.Clear();
                using (var actors = _hidden.ToEntityArray(Allocator.Temp))
                    foreach (var actor in actors)
                    {
                        TrackHiddenDecal(actor);
                        if (initial) work.Add(actor);
                    }
                hiddenDecalIndexReady = true;
            }
            hiddenDecalScratch.Clear();
            foreach (var road in pending) hiddenDecalOwners.CopyOwner(road, hiddenDecalScratch);
            foreach (var actor in hiddenDecalScratch)
            {
                if (!EntityManager.Exists(actor) || !EntityManager.HasComponent<RDCHiddenDecal>(actor))
                { hiddenDecalOwners.Remove(actor); continue; }
                var marker = EntityManager.GetComponentData<RDCHiddenDecal>(actor);
                hiddenDecalOwners.Add(marker.Road, actor);
                if (initial || pending.Contains(marker.Road)) work.Add(actor);
            }
            // Current changed/revealed markers are handled immediately by their
            // original queries. This bounded recovery only repairs missed index
            // changes; it never delays or rate-limits a native reveal.
            hiddenDecalScratch.Clear(); hiddenDecalOwners.Rotate(hiddenDecalScratch, 64);
            foreach (var actor in hiddenDecalScratch)
            {
                if (!EntityManager.Exists(actor) || !EntityManager.HasComponent<RDCHiddenDecal>(actor))
                { hiddenDecalOwners.Remove(actor); continue; }
                var marker = EntityManager.GetComponentData<RDCHiddenDecal>(actor);
                hiddenDecalOwners.Add(marker.Road, actor);
                if (pending.Contains(marker.Road)) work.Add(actor);
            }
            hiddenDecalScratch.Clear();
        }
    }
}
