using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class SignalPreviewSystem
    {
        private readonly PreviewOwnerIndex savedOwners = new PreviewOwnerIndex();
        internal PreviewOwnerIndex RefreshOwners => savedOwners;
        private readonly PreviewOwnerIndex bindingOwners = new PreviewOwnerIndex();
        private readonly List<Entity> callbackActors = new List<Entity>();
        private readonly Dictionary<Entity, int> nodeRevisions = new Dictionary<Entity, int>();
        private int revisionSerial, revisionBase;

        private int NodeRevision(Entity node) => nodeRevisions.TryGetValue(node, out var value) ? value : revisionBase;

        private void InvalidateNode(Entity node)
        {
            if (node == Entity.Null)
            {
                // Missing ownership cannot justify a local invalidation.
                revisionBase = unchecked(++revisionSerial);
                nodeRevisions.Clear(); nodes.Clear();
            }
            else nodeRevisions[node] = unchecked(++revisionSerial);
        }

        private void SetBinding(Entity actor, Binding value)
        {
            bindings[actor] = value;
            bindingOwners.Track(actor, value.Node, value.Source);
        }

        private void ForgetBinding(Entity actor) { bindings.Remove(actor); bindingOwners.Remove(actor); }

        private void SetSaved(Entity actor, Saved value, Entity node)
        {
            saved[actor] = value;
            savedOwners.Track(actor, value.Owner, node);
        }

        private void ForgetSaved(Entity actor) { saved.Remove(actor); savedOwners.Remove(actor); }

        internal void RestoreForOwners(PreviewRefreshScope scope)
        {
            if (!scope.Complete) { RestoreForRefresh(); return; }
            savedOwners.Select(scope.Owners, stale);
            RestoreRetiredIdentities(stale);
            foreach (var actor in stale)
            {
                if (saved.TryGetValue(actor, out var value)) QueueRestore(actor, value);
                ForgetSaved(actor);
            }
            visualWrites += TemporaryPreviewWrites.Apply(World, writes);
        }
    }
}
