using System.Collections.Generic;
using Game.Common;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class PreviewPaintInheritanceSystem
    {
        private readonly PreviewOwnerIndex entryOwners = new PreviewOwnerIndex();
        internal PreviewOwnerIndex RefreshOwners => entryOwners;

        internal void RestoreForOwners(PreviewRefreshScope scope)
        {
            if (!scope.Complete) { RestoreForRefresh(); return; }
            entryOwners.Select(scope.Owners, stale);
            foreach (var lane in stale)
                if (entries.TryGetValue(lane, out var entry) && entry.UsingOriginal && entry.Actors.Count == 0)
                    RestoreAlias(lane); // Native matching key restored; committed binding retained.
                else Remove(lane);
            // Retaining unaffected actors must also retain their original-paint
            // visibility claims. Affected actors still retire before generation.
            retainedOriginals.Clear();
            foreach (var entry in entries.Values)
                if (entry.UsingOriginal && Alive(entry.Source)) retainedOriginals.Add(entry.Source);
            FlushRetired();
            TemporaryPreviewWrites.Apply(World, writes);
        }
    }

    public partial class CrosswalkAppearancePreviewSystem
    {
        private readonly PreviewOwnerIndex aliasOwners = new PreviewOwnerIndex();
        internal PreviewOwnerIndex RefreshOwners => aliasOwners;

        internal void RestoreForOwners(PreviewRefreshScope scope)
        {
            if (!scope.Complete) { RestoreForRefresh(); return; }
            aliasOwners.Select(scope.Owners, stale);
            foreach (var lane in stale) RestoreAlias(lane);
            // Binding/paint retention deliberately survives the refresh, as it
            // does in the existing full restore. Only native keys are returned.
            TemporaryPreviewWrites.Apply(World, writes);
        }
    }
}
