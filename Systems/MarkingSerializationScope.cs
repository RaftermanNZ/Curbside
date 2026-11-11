using System;
using System.Collections.Generic;
using Game.Common;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class GeneratedVisualSaveSystem
    {
        // This world owns exact versioned IDs, never an index-only claim.
        private readonly HashSet<Entity> serializationExcludedMarkings = new HashSet<Entity>();

        internal bool OwnsMarkingSerializationExclusion(Entity actor)
            => serializationExcludedMarkings.Contains(actor);

        // Called synchronously by the save-start callback, before the game's
        // existing GPU-frame wait. Every retirement fallback stays on this side.
        private bool PrepareMarkingSerializationBeforeSaveWait()
        {
            if (!MarkingRetention.Active) return false;
            using (var actors = new NativeList<Entity>(MarkingRetention.Actors.Count, Allocator.Temp))
            {
                foreach (var actor in MarkingRetention.Actors)
                {
                    if (!EntityManager.Exists(actor) || EntityManager.HasComponent<Deleted>(actor)
                        || EntityManager.HasComponent<Disabled>(actor))
                    {
                        Mod.Log.Warn("Marking retention preflight rejected before GPU wait: actor changed: " + actor);
                        CancelMarkingRetention();
                        return false;
                    }
                    actors.Add(actor);
                }
                // Record before mutation, so partial failure is recoverable.
                // Actors already disabled before capture are never claimed.
                foreach (var actor in actors) serializationExcludedMarkings.Add(actor);
                try
                {
                    if (actors.Length != 0) EntityManager.AddComponent(actors.AsArray(), ComponentType.ReadWrite<Disabled>());
                }
                catch (Exception error)
                {
                    // This fallback still precedes the native frame wait.
                    foreach (var actor in MarkingRetention.Actors) GeneratedVisualLifecycle.RetireNow(World, actor);
                    CancelMarkingRetention();
                    Mod.Log.Warn("Marking exclusion preflight failed before GPU wait; original early cleanup used: " + error.Message);
                    return false;
                }
            }
            return true;
        }

        // Existing Serialize entry: validate the frozen ticket. Do not open a
        // new exclusion or delete a previously held actor at this late boundary.
        internal void BeginMarkingSerializationScope()
        {
            if (!MarkingRetention.Active) return;
            NativeVisualInvalidation.CompleteReaders(World);
            ValidateMarkingRetention();
            foreach (var actor in serializationExcludedMarkings)
            {
                if (!EntityManager.Exists(actor) || EntityManager.HasComponent<Deleted>(actor))
                {
                    DeferMarkingReconcileAfterSave("a held actor was destroyed or deleted by another producer");
                    continue;
                }
                if (!EntityManager.HasComponent<Disabled>(actor))
                {
                    // Another mod removing the same native tag violates this
                    // exclusion contract. Do not erase or overwrite its edits.
                    DeferMarkingReconcileAfterSave("another producer removed a held actor's exclusion");
                    Mod.Log.Warn("Marking save exclusion was removed by another producer: " + actor
                        + ". Snapshot exclusion is not guaranteed for this actor.");
                }
            }
        }

        internal void RestoreMarkingSerializationScope()
        {
            if (serializationExcludedMarkings.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            using (var actors = new NativeList<Entity>(serializationExcludedMarkings.Count, Allocator.Temp))
            {
                foreach (var actor in serializationExcludedMarkings)
                    if (EntityManager.Exists(actor) && EntityManager.HasComponent<Disabled>(actor)) actors.Add(actor);
                if (actors.Length != 0) EntityManager.RemoveComponent(actors.AsArray(), ComponentType.ReadWrite<Disabled>());
            }
            // Retry at actual completion if the native batch throws. No stale
            // prefab, transform, curve, ownership or owner-buffer snapshot writes.
            serializationExcludedMarkings.Clear();
        }
    }
}
