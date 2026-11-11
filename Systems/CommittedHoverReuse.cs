using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    internal static class CommittedHoverPolicy
    {
        internal static bool SameBinding(Entity source, Entity expectedSource, Entity owner, Entity expectedOwner,
            Entity native, Entity expectedNative, Bezier4x3 original, Bezier4x3 expectedOriginal,
            Bezier4x3 preview, Bezier4x3 expectedPreview)
            => source != Entity.Null && owner != Entity.Null && native != Entity.Null
                && source == expectedSource && owner == expectedOwner && native == expectedNative
                && PreviewPaintGeometry.Valid(original) && PreviewPaintGeometry.Valid(preview)
                && MarkingPreviewPolicy.SameCurve(original, expectedOriginal)
                && MarkingPreviewPolicy.SameCurve(preview, expectedPreview);
    }

    public partial class IntersectionPaintSystem
    {
        private static readonly object hiddenPreviewIntent = new object();

        // Managed identity only, scoped to the current replacement. Replacing
        // or removing paint changes this token without changing line planning.
        internal object PreviewIntentToken(Entity source)
        {
            if (!Alive(source) || EntityManager.HasComponent<Temp>(source)
                || _externalPaint.Contains(EntityManager, source)) return null;
            if (EntityManager.HasComponent<MGRHiddenJunctionLine>(source)) return hiddenPreviewIntent;
            return _replacements.TryGetValue(source, out var entry) ? entry : null;
        }
    }

    public partial class PreviewPaintInheritanceSystem
    {
        private bool TryKeepCommitted(Entity lane, Entity source, IntersectionPaintSystem replacements)
        {
            if (!entries.TryGetValue(lane, out var entry) || entry.Dirty || !entry.UsingOriginal
                || entry.Source != source || !identicalSources.TryGetValue(source, out var identical) || !identical
                || !object.ReferenceEquals(entry.Intent, replacements.PreviewIntentToken(source))
                || entry.Intent == null || !EntityManager.HasComponent<Owner>(source)
                || EntityManager.GetComponentData<Owner>(source).m_Owner != entry.SourceOwner
                || !Alive(entry.SourceOwner)
                || !EntityManager.HasComponent<PrefabRef>(source)
                || EntityManager.GetComponentData<PrefabRef>(source).m_Prefab != entry.SourcePrefab
                || !MarkingPreviewPolicy.SameCurve(EntityManager.GetComponentData<Curve>(source).m_Bezier, entry.Original)) return false;
            var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            var current = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
            var native = reverse.TryGetValue(current, out var restored) ? restored : current;
            if (owner != entry.Owner || !Alive(owner) || !EntityManager.HasComponent<Temp>(owner)
                || native != entry.Prefab || !EntityManager.HasComponent<NetLaneGeometryData>(native)
                || !CommittedHoverPolicy.SameBinding(source, entry.Source, owner, entry.Owner, native, entry.Prefab,
                    EntityManager.GetComponentData<Curve>(source).m_Bezier, entry.Original,
                    EntityManager.GetComponentData<Curve>(lane).m_Bezier, entry.Curve)) return false;
            // Other mods may edit our actors without sending SourceChanged.
            // Validate copied values at this native change event, never on a
            // timer or a display-frame sweep. No cached ECS views are held.
            foreach (var stroke in entry.Committed)
                if (!Alive(stroke.Actor) || !EntityManager.HasComponent<Curve>(stroke.Actor)
                    || !EntityManager.HasComponent<PrefabRef>(stroke.Actor)
                    || EntityManager.GetComponentData<PrefabRef>(stroke.Actor).m_Prefab != stroke.Prefab
                    || !MarkingPreviewPolicy.SameCurve(EntityManager.GetComponentData<Curve>(stroke.Actor).m_Bezier, stroke.Curve)
                    || !EntityManager.HasComponent<NetLaneArchetypeData>(stroke.Prefab)
                    || !EntityManager.GetComponentData<NetLaneArchetypeData>(stroke.Prefab).m_LaneArchetype.Valid) return false;
            aliases[lane] = native;
            TemporaryPreviewWrites.QueueLanePrefab(writes, lane, current, HiddenDefinition(native));
            return true;
        }

        private void RestoreAlias(Entity lane)
        {
            if (!aliases.TryGetValue(lane, out var native)) return;
            if (Alive(lane) && EntityManager.HasComponent<PrefabRef>(lane)
                && reverse.TryGetValue(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab, out var original) && original == native)
                writes[lane] = new TemporaryPreviewWrite { Prefab = native };
            aliases.Remove(lane);
        }
    }

    public partial class CrosswalkAppearancePreviewSystem
    {
        private bool TryKeepCommittedCrosswalk(Entity lane, CrosswalkUpgradeSystem choices)
        {
            if (!bindings.TryGetValue(lane, out var binding) || binding.Dirty || !binding.Unchanged
                || EntityManager.GetComponentData<Temp>(lane).m_Original != binding.Source
                || !Alive(binding.Source) || external.Contains(EntityManager, binding.Source)
                || EntityManager.HasComponent<Temp>(binding.Source)
                || !EntityManager.HasComponent<PedestrianLane>(binding.Source)
                || (EntityManager.GetComponentData<PedestrianLane>(binding.Source).m_Flags & PedestrianLaneFlags.Crosswalk) == 0
                || !Alive(binding.SourceOwner) || !Alive(binding.Road) || !Alive(binding.Node)
                || !EntityManager.HasComponent<Owner>(binding.Source)
                || EntityManager.GetComponentData<Owner>(binding.Source).m_Owner != binding.SourceOwner
                || !EntityManager.HasComponent<PrefabRef>(binding.Source)
                || World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Original(EntityManager.GetComponentData<PrefabRef>(binding.Source).m_Prefab) != binding.SourcePrefab
                || !EntityManager.HasComponent<Curve>(binding.Source)
                || !MarkingPreviewPolicy.SameCurve(EntityManager.GetComponentData<Curve>(binding.Source).m_Bezier, binding.SourceCurve)
                || EntityManager.GetComponentData<Owner>(lane).m_Owner != binding.PreviewOwner
                || !MarkingPreviewPolicy.SameCurve(EntityManager.GetComponentData<Curve>(lane).m_Bezier, binding.PreviewCurve)) return false;
            byte appearance = choices.CommittedAppearance(binding.Road, binding.Node);
            if (appearance == 0)
                appearance = choices.IsSourceInvisible(binding.Source) ? (byte)2 : choices.IsSourceSelected(binding.Source) ? (byte)1 : (byte)0;
            if (appearance == 0 || appearance != binding.Appearance
                || appearance == 1 && binding.PaintSources.Count == 0) return false;
            foreach (var source in binding.PaintSources) if (!Alive(source)) return false;
            var current = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
            var native = reverse.TryGetValue(current, out var restored) ? restored : current;
            if (!EntityManager.HasComponent<NetLaneGeometryData>(native)
                || !CommittedHoverPolicy.SameBinding(EntityManager.GetComponentData<Temp>(lane).m_Original, binding.Source,
                    EntityManager.GetComponentData<Owner>(lane).m_Owner, binding.PreviewOwner,
                    native, binding.Native, EntityManager.GetComponentData<Curve>(binding.Source).m_Bezier, binding.SourceCurve,
                    EntityManager.GetComponentData<Curve>(lane).m_Bezier, binding.PreviewCurve)) return false;
            aliases[lane] = native;
            TemporaryPreviewWrites.QueueLanePrefab(writes, lane, current, HiddenDefinition(native));
            return true;
        }

        private void RememberCommittedCrosswalk(Entity lane, Binding binding, Entity native, byte appearance)
        {
            binding.Unchanged = false;
            var source = binding.Source;
            if (!Alive(source) || EntityManager.HasComponent<Temp>(source)
                || !EntityManager.HasComponent<Owner>(source) || !EntityManager.HasComponent<PrefabRef>(source)
                || !EntityManager.HasComponent<Curve>(source)
                || EntityManager.GetComponentData<Temp>(lane).m_Original != source) return;
            var curve = EntityManager.GetComponentData<Curve>(source).m_Bezier;
            var preview = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
            if (!PreviewPaintGeometry.SamePath(curve, preview)) return;
            binding.Unchanged = true;
            binding.SourceOwner = EntityManager.GetComponentData<Owner>(source).m_Owner;
            binding.SourcePrefab = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab);
            binding.SourceCurve = curve;
            binding.PreviewCurve = preview;
            binding.PreviewOwner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            binding.Native = native;
            binding.Appearance = appearance;
        }
    }
}
