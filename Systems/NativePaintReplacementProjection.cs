using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class NativePaintProjectionSystem
    {
        private sealed class ReplacementPreview
        {
            internal Entity Source, Parent, Prefab;
            internal Bezier4x3 Original, Preview;
            internal float2 Range;
            internal bool Retry;
            internal readonly List<PreviewPaintStroke> Committed = new List<PreviewPaintStroke>();
            internal readonly Dictionary<Entity, PreviewPathPair> Paths = new Dictionary<Entity, PreviewPathPair>();
            internal readonly List<Entity> Actors = new List<Entity>();
        }

        private readonly Dictionary<Entity, ReplacementPreview> replacementPreviews = new Dictionary<Entity, ReplacementPreview>();
        private readonly List<PreviewPaintStroke> replacementStrokes = new List<PreviewPaintStroke>();
        private readonly List<Entity> replacementRetirements = new List<Entity>();
        internal int ReplacementPreviewCount => replacementPreviews.Count;
        internal long ReplacementBuilds { get; private set; }
        internal long ReplacementCacheHits { get; private set; }

        // Called only from the native changed-actor/source-callback batch. The
        // committed replacement is the authority, including deliberately empty
        // output. This path does not choose a paint style or inspect nearby roads.
        private bool ProjectReplacement(Entity actor, Entity source, Entity parent)
        {
            // A saved empty result needs no geometry or split-range mapping.
            // Unavailable replacement output remains distinct from empty output.
            if (SavedEmpty(source)) { RemoveReplacement(actor); return true; }
            if (!EntityManager.HasComponent<MGRReplacedPaint>(source)
                || !EntityManager.HasComponent<Curve>(source) || !EntityManager.HasComponent<Curve>(actor))
            { RemoveReplacement(actor); return true; }

            var original = EntityManager.GetComponentData<Curve>(source).m_Bezier;
            var preview = EntityManager.GetComponentData<Curve>(actor).m_Bezier;
            if (MarkingPreviewPolicy.SameCurve(original, preview))
            { RemoveReplacement(actor); return true; } // Existing retention owns this case.

            var prefab = EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
            var originalParent = EntityManager.GetComponentData<Owner>(source).m_Owner;
            if (!NativePreviewSection.TryRange(EntityManager, parent, originalParent, out var range))
            { RemoveReplacement(actor); return false; }
            return PublishReplacement(actor, source, parent, prefab, original, preview, false, range);
        }

        private bool PublishReplacement(Entity actor, Entity source, Entity parent, Entity prefab,
            Bezier4x3 original, Bezier4x3 preview, bool ownedOutput, float2 range)
        {
            ReadOwnerPaths(ownedOutput ? parent : Entity.Null, source);
            if (replacementPreviews.TryGetValue(actor, out var cached)
                && cached.Source == source && cached.Parent == parent && cached.Prefab == prefab
                && cached.Range.Equals(range) && !cached.Retry
                && SameOwnerPaths(cached.Paths)
                && MarkingPreviewPolicy.SameCurve(cached.Original, original)
                && MarkingPreviewPolicy.SameCurve(cached.Preview, preview)
                && !pendingSources.Contains(source) && !pendingOwners.Contains(source))
            { ReplacementCacheHits++; return true; }

            bool read = ownedOutput
                ? World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().ReadOwnedPreviewPaint(source, replacementStrokes)
                : World.GetExistingSystemManaged<IntersectionPaintSystem>()?.ReadPreviewPaint(source, replacementStrokes) == true;
            if (!read)
            {
                if (ownedOutput || World.GetExistingSystemManaged<IntersectionPaintSystem>()?.OwnsPreviewPaint(source) != true)
                { RemoveReplacement(actor); return false; }
                // Preserve the last authoritative presentation while committed
                // actors are being regenerated in this native modification pass.
                replacementStrokes.Clear();
                if (cached == null || cached.Source != source || cached.Prefab != prefab
                    || !MarkingPreviewPolicy.SameCurve(cached.Original, original))
                { RemoveReplacement(actor); return false; }
                replacementStrokes.AddRange(cached.Committed);
            }

            var next = new ReplacementPreview { Source = source, Parent = parent, Prefab = prefab,
                Original = original, Preview = preview, Range = range, Retry = !read };
            next.Committed.AddRange(replacementStrokes);
            foreach (var path in ownerPaths) next.Paths.Add(path.Key, path.Value);

            int kept = 0;
            for (int i = 0; i < replacementStrokes.Count; i++)
            {
                var stroke = replacementStrokes[i];
                var before = original; var after = preview; var section = range;
                if (stroke.NativeSource != Entity.Null && ownerPaths.TryGetValue(stroke.NativeSource, out var path))
                { before = path.Original; after = path.Preview; section = new float2(0, 1); }
                if (!PreviewPaintSectionGeometry.Map(stroke.Curve, before, after, section, out stroke.Curve, out bool included)
                    || !EntityManager.Exists(stroke.Prefab)
                    || !EntityManager.HasComponent<NetLaneArchetypeData>(stroke.Prefab)
                    || !EntityManager.GetComponentData<NetLaneArchetypeData>(stroke.Prefab).m_LaneArchetype.Valid)
                { RemoveReplacement(actor); replacementStrokes.Clear(); return false; }
                if (included) replacementStrokes[kept++] = stroke;
            }
            if (kept < replacementStrokes.Count) replacementStrokes.RemoveRange(kept, replacementStrokes.Count - kept);

            try
            {
                var emission = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
                foreach (var stroke in replacementStrokes)
                {
                    // Shared committed emission helper preserves the actual
                    // prefab/material and native rendering registration contract.
                    var generated = emission.Spawn(parent, stroke.Curve, stroke.Prefab, false);
                    next.Actors.Add(generated);
                    if (EntityManager.HasComponent<MGRSignalWire>(stroke.Actor))
                    {
                        if (EntityManager.HasComponent<EdgeMapping>(generated)) EntityManager.RemoveComponent<EdgeMapping>(generated);
                        if (EntityManager.HasBuffer<SubFlow>(generated)) EntityManager.RemoveComponent<SubFlow>(generated);
                        if (EntityManager.HasComponent<NodeLane>(stroke.Actor))
                            EntityManager.AddComponentData(generated, EntityManager.GetComponentData<NodeLane>(stroke.Actor));
                    }
                    EntityManager.AddComponentData(generated, new Temp { m_Flags = TempFlags.Select });
                    EntityManager.AddComponent<MGRTemporaryPreviewPaint>(generated);
                    GeneratedVisualLifecycle.RememberLaneOwner(World, generated, parent);
                }
            }
            catch
            {
                replacementRetirements.AddRange(next.Actors);
                throw;
            }
            finally { replacementStrokes.Clear(); }
            RemoveReplacement(actor);
            replacementPreviews[actor] = next;
            ReplacementBuilds++;
            return true;
        }

        private void RemoveReplacement(Entity actor)
        {
            if (!replacementPreviews.TryGetValue(actor, out var entry)) return;
            replacementRetirements.AddRange(entry.Actors);
            replacementPreviews.Remove(actor);
        }

        private void ReleaseReplacements()
        {
            foreach (var entry in replacementPreviews.Values) replacementRetirements.AddRange(entry.Actors);
            replacementPreviews.Clear();
            FlushReplacementRetirements();
        }

        private void FlushReplacementRetirements()
        {
            if (replacementRetirements.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            using (var actors = new NativeList<Entity>(replacementRetirements.Count, Allocator.Temp))
            {
                foreach (var actor in replacementRetirements) if (EntityManager.Exists(actor)) actors.Add(actor);
                if (actors.Length != 0)
                {
                    var snapshot = actors.AsArray();
                    // ApplyNetSystem also observes Deleted actors: removing Temp
                    // is essential before the native apply boundary consumes them.
                    EntityManager.RemoveComponent(snapshot, ComponentType.ReadWrite<Temp>());
                    GeneratedVisualLifecycle.DetachSaveLanes(World, snapshot);
                    GeneratedVisualLifecycle.RetireSaveBatch(World, snapshot);
                }
            }
            replacementRetirements.Clear();
        }
    }
}
