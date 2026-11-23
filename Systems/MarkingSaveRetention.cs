using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Save-event scope only. No ECS views, new components, system or frame work.
    // The first family is committed road-section emitter paint with BUILT-IN
    // lane definitions. Registered custom styles deliberately retain old cleanup.
    internal sealed class MarkingSaveRetention
    {
        internal readonly HashSet<Entity> Roads = new HashSet<Entity>();
        internal readonly HashSet<Entity> Actors = new HashSet<Entity>();
        internal readonly HashSet<Entity> Sources = new HashSet<Entity>();
        internal readonly Dictionary<Entity, (Entity Owner, Entity Prefab, Bezier4x3 Curve)> SourceIdentities
            = new Dictionary<Entity, (Entity Owner, Entity Prefab, Bezier4x3 Curve)>();
        internal bool Active { get; private set; }
        internal bool NativePrefabSerialization;
        private readonly HashSet<Entity> validatedDefinitions = new HashSet<Entity>();
        private int definitionValidationDepth;
        internal readonly struct DefinitionValidation : IDisposable
        {
            private readonly MarkingSaveRetention owner;
            internal DefinitionValidation(MarkingSaveRetention owner)
            {
                this.owner = owner;
                if (owner.definitionValidationDepth++ == 0) owner.validatedDefinitions.Clear();
            }
            public void Dispose()
            {
                if (owner != null && --owner.definitionValidationDepth == 0) owner.validatedDefinitions.Clear();
            }
        }
        // Save-only, explicitly non-mutating validation phases. Never reused
        // across preparation, native serialization, cancellation or completion.
        internal DefinitionValidation DefinitionValidationScope() => new DefinitionValidation(this);
        private sealed class Definition
        {
            internal NetLaneGeometryPrefab Managed;
            internal int RuntimeIndex;
            internal NetLaneData Lane;
            internal NetLaneGeometryData Geometry;
            internal NetLaneArchetypeData Archetype;
        }
        private readonly Dictionary<Entity, Definition> definitions = new Dictionary<Entity, Definition>();

        internal void Begin() { Clear(); Active = true; }
        internal void Clear() { Roads.Clear(); Actors.Clear(); Sources.Clear(); SourceIdentities.Clear(); definitions.Clear(); validatedDefinitions.Clear(); definitionValidationDepth = 0; NativePrefabSerialization = false; Active = false; }
        internal void InheritDefinitionProofs(MarkingSaveRetention original)
        {
            NativePrefabSerialization = original.NativePrefabSerialization;
            foreach (var pair in original.definitions) definitions.Add(pair.Key, pair.Value);
        }
        internal bool StableDefinition(EntityManager manager, PrefabSystem prefabs, Entity entity)
        {
            if (!Active) return false;
            if (definitionValidationDepth != 0 && validatedDefinitions.Contains(entity)) return true;
            if (!NativePrefabSerialization)
            {
                if (!StablePaint(manager, prefabs, entity)) return false;
                if (!definitions.ContainsKey(entity))
                {
                    if (!prefabs.TryGetPrefab<NetLaneGeometryPrefab>(entity, out var managed)) return false;
                    definitions.Add(entity, new Definition { Managed = managed, RuntimeIndex = manager.GetComponentData<PrefabData>(entity).m_Index,
                        Lane = manager.GetComponentData<NetLaneData>(entity),
                        Geometry = manager.GetComponentData<NetLaneGeometryData>(entity),
                        Archetype = manager.GetComponentData<NetLaneArchetypeData>(entity) });
                }
            }
            if (!definitions.TryGetValue(entity, out var expected) || !manager.Exists(entity)
                || manager.HasComponent<Deleted>(entity) || !manager.HasComponent<PrefabData>(entity)
                || manager.HasComponent<MGRPrivateVisualPrefab>(entity) || manager.HasComponent<MGRPrivatePaintDefinition>(entity)
                || !manager.HasComponent<NetLaneData>(entity) || !manager.HasComponent<NetLaneGeometryData>(entity)
                || !manager.HasComponent<NetLaneArchetypeData>(entity)
                || !expected.Lane.Equals(manager.GetComponentData<NetLaneData>(entity))
                || !expected.Geometry.Equals(manager.GetComponentData<NetLaneGeometryData>(entity))
                || !expected.Archetype.Equals(manager.GetComponentData<NetLaneArchetypeData>(entity))) return false;
            if (NativePrefabSerialization)
            {
                // Referenced definitions carry the native old-index record;
                // unused definitions stay disabled with their runtime index.
                // Do not accept arbitrary index/enable changes as normalization.
                if (manager.IsComponentEnabled<PrefabData>(entity))
                {
                    if (!manager.HasBuffer<LoadedIndex>(entity)) return false;
                    var original = manager.GetBuffer<LoadedIndex>(entity, true);
                    if (original.Length != 1 || original[0].m_Index != expected.RuntimeIndex) return false;
                }
                else if (manager.GetComponentData<PrefabData>(entity).m_Index != expected.RuntimeIndex) return false;
            }
            // TryGetEntity uses the managed registration, not remapped PrefabData.
            bool same = prefabs != null && expected.Managed != null && expected.Managed.isBuiltin
                && prefabs.TryGetEntity(expected.Managed, out var registered) && registered == entity;
            if (same && definitionValidationDepth != 0) validatedDefinitions.Add(entity);
            return same;
        }
        internal bool Retains(Entity entity) => Active && Actors.Contains(entity);
        internal bool RetainsSource(Entity entity) => Active && Sources.Contains(entity);

        internal static bool StablePaint(EntityManager manager, PrefabSystem prefabs, Entity entity)
        {
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity)
                || !manager.HasComponent<PrefabData>(entity) || !manager.IsComponentEnabled<PrefabData>(entity)
                || manager.HasComponent<MGRPrivateVisualPrefab>(entity) || manager.HasComponent<MGRPrivatePaintDefinition>(entity)
                || !manager.HasComponent<NetLaneData>(entity) || !manager.HasComponent<NetLaneGeometryData>(entity)
                || !manager.HasComponent<NetLaneArchetypeData>(entity)) return false;
            if (!manager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype.Valid) return false;
            var flags = manager.GetComponentData<NetLaneData>(entity).m_Flags;
            if ((flags & (LaneFlags.Road | LaneFlags.Parking | LaneFlags.Pedestrian | LaneFlags.Track | LaneFlags.Master)) != 0) return false;
            return prefabs != null && prefabs.TryGetPrefab<NetLaneGeometryPrefab>(entity, out var definition)
                && definition != null && definition.isBuiltin
                && prefabs.TryGetEntity(definition, out var registered) && registered == entity;
        }
    }

    public partial class CurbLineEmissionSystem
    {
        private sealed class RetainedRoadMarking
        {
            internal RoadMarks Marks;
            internal List<Entity> Entities;
            internal List<MarkCurve> Curves;
            internal EdgeGeometry Geometry;
            internal Composition Composition;
        }
        private readonly Dictionary<Entity, RetainedRoadMarking> saveRoadMarkings = new Dictionary<Entity, RetainedRoadMarking>();

        internal void CaptureMarkingRetention(MarkingSaveRetention retention)
        {
            using (retention.DefinitionValidationScope())
            {

            saveRoadMarkings.Clear();
            foreach (var pair in _marks)
            {
                if (!RetainableRoadMarking(pair.Key, pair.Value, retention)) continue;
                saveRoadMarkings.Add(pair.Key, new RetainedRoadMarking
                {
                    Marks = pair.Value,
                    Entities = new List<Entity>(pair.Value.Entities),
                    Curves = new List<MarkCurve>(pair.Value.Curves),
                    Geometry = EntityManager.GetComponentData<EdgeGeometry>(pair.Key),
                    Composition = EntityManager.GetComponentData<Composition>(pair.Key)
                });
                retention.Roads.Add(pair.Key);
                foreach (var actor in pair.Value.Entities) retention.Actors.Add(actor);
            }
        
            }
        }

        private bool RetainableRoadMarking(Entity road, RoadMarks marks, MarkingSaveRetention retention)
        {
            if (!EntityManager.Exists(road) || EntityManager.HasComponent<Deleted>(road)
                || EntityManager.HasComponent<Game.Tools.Temp>(road) || !EntityManager.HasComponent<Road>(road)
                || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<PrefabRef>(road)
                || !EntityManager.HasBuffer<Game.Net.SubLane>(road)
                || !EntityManager.HasComponent<EdgeGeometry>(road) || !EntityManager.HasComponent<Composition>(road)
                || marks.Entities.Count == 0 || marks.Entities.Count != marks.Curves.Count) return false;
            var seen = new HashSet<Entity>();
            for (int i = 0; i < marks.Entities.Count; i++)
            {
                var actor = marks.Entities[i]; var mark = marks.Curves[i];
                if (!seen.Add(actor) || !EntityManager.Exists(actor) || EntityManager.HasComponent<Deleted>(actor)
                    || !EntityManager.HasComponent<Game.Rendering.CullingInfo>(actor)
                    || EntityManager.HasComponent<Game.Common.BatchesUpdated>(actor)
                    || (EntityManager.HasComponent<Disabled>(actor)
                        && World.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.OwnsMarkingSerializationExclusion(actor) != true)
                    || EntityManager.HasComponent<Created>(actor) || EntityManager.HasComponent<Updated>(actor)
                    || EntityManager.HasComponent<MGRPendingVisualRegistration>(actor) || EntityManager.HasComponent<MGRPendingSpatialUpdate>(actor)
                    || EntityManager.HasComponent<Game.Tools.Temp>(actor) || !EntityManager.HasComponent<MGRCurbMarking>(actor)
                    || EntityManager.HasComponent<Game.Net.SecondaryLane>(actor)
                    || EntityManager.HasComponent<Game.Net.MasterLane>(actor) || EntityManager.HasComponent<Game.Net.SlaveLane>(actor)
                    || EntityManager.HasComponent<Game.Net.ConnectionLane>(actor)
                    || EntityManager.HasComponent<MGRIntersectionPaint>(actor) || !EntityManager.HasComponent<Lane>(actor)
                    || EntityManager.HasComponent<Game.Net.CarLane>(actor) || EntityManager.HasComponent<Game.Net.ParkingLane>(actor)
                    || EntityManager.HasComponent<Game.Net.PedestrianLane>(actor) || EntityManager.HasComponent<Game.Net.TrackLane>(actor)
                    || !EntityManager.HasComponent<Owner>(actor) || EntityManager.GetComponentData<Owner>(actor).m_Owner != road
                    || !EntityManager.HasComponent<Curve>(actor) || !EntityManager.HasComponent<PrefabRef>(actor)
                    || EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab != mark.Prefab
                    || !SameRetainedCurve(EntityManager.GetComponentData<Curve>(actor).m_Bezier, mark.Curve)
                    || !retention.StableDefinition(EntityManager, _prefabs, mark.Prefab)) return false;
            }
            return true;
        }

        private static bool SameRetainedCurve(Bezier4x3 a, Bezier4x3 b)
            => a.a.Equals(b.a) && a.b.Equals(b.b) && a.c.Equals(b.c) && a.d.Equals(b.d);

        internal bool ValidateMarkingRetention()
        {
            var retention = World.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.MarkingRetention;
            if (retention == null || !retention.Active) return saveRoadMarkings.Count == 0;

            using (retention.DefinitionValidationScope())
            {
            foreach (var pair in saveRoadMarkings)
            {
                if (!_marks.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, pair.Value.Marks)
                    || !RetainableRoadMarking(pair.Key, current, retention)
                    || current.Entities.Count != pair.Value.Entities.Count || current.Curves.Count != pair.Value.Curves.Count
                    || !EntityManager.GetComponentData<EdgeGeometry>(pair.Key).Equals(pair.Value.Geometry)
                    || !EntityManager.GetComponentData<Composition>(pair.Key).Equals(pair.Value.Composition)) return false;
                for (int i = 0; i < current.Entities.Count; i++)
                    if (current.Entities[i] != pair.Value.Entities[i] || !current.Curves[i].Equals(pair.Value.Curves[i])) return false;
            }
            return true;
        
            }
        }

        internal void EndMarkingRetention() => saveRoadMarkings.Clear();
        internal void QueueRetainedMarkingReconcile(MarkingSaveRetention retention)
        {
            foreach (var road in retention.Roads)
                if (EntityManager.Exists(road) && EntityManager.HasComponent<Road>(road)
                    && !EntityManager.HasComponent<Deleted>(road) && !EntityManager.HasComponent<Game.Tools.Temp>(road)) QueueRoad(road);
        }
        internal bool RetainableHiddenOwner(Entity road, MarkingSaveRetention retention)
            => !_marks.ContainsKey(road) || retention.Roads.Contains(road);

        private void PrepareRetainedMarkingsForSerialization()
        {
            var save = World.GetExistingSystemManaged<GeneratedVisualSaveSystem>();
            var retention = save?.MarkingRetention;
            if (retention == null || !retention.Active) { ClearOwned(); _initial = true; return; }
            if (!ValidateMarkingRetention())
            {
                Mod.Log.Warn("Save retention deferred: recorded road/actor state changed before preparation.");
                save.DeferMarkingReconcileAfterSave("road/actor state changed during the save wait");
            }
            if (World.GetExistingSystemManaged<IntersectionPaintSystem>()?.ValidateRetainedHiddenSources(retention) == false)
            {
                Mod.Log.Warn("Save retention deferred: matching hidden native source is uncertain.");
                save.DeferMarkingReconcileAfterSave("hidden-source state changed during the save wait");
            }
            { CompleteDependency(); Dependency = default; }
            using (var entities = _owned.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    if (!retention.Retains(entity)) GeneratedVisualLifecycle.Retire(World, entity);
            foreach (var road in new List<Entity>(_marks.Keys))
                if (!retention.Roads.Contains(road)) { _marks.Remove(road); _caps.Remove(road); QueueRoad(road); }
            foreach (var node in _joins.Keys) _dirtyNodes.Add(node);
            _joins.Clear();
            // Existing pending work and retained plans survive. Unchanged roads
            // are not made initial here. Existing other refresh sources are kept.
            _ownedCount = retention.Actors.Count;
        }
    }

    public partial class IntersectionPaintSystem
    {
        internal string RetentionSourceFailure { get; private set; }
        private bool RejectRetainedSource(Entity source, Entity road, string reason)
        {
            RetentionSourceFailure = reason + "; source=" + source + "; road=" + road;
            return false;
        }

        internal bool CaptureRetainedHiddenSources(MarkingSaveRetention retention)
        {
            using (retention.DefinitionValidationScope())
            {

            RetentionSourceFailure = null;
            retention.Sources.Clear();
            retention.SourceIdentities.Clear();
            var lines = World.GetExistingSystemManaged<CurbLineEmissionSystem>();
            var indexed = new List<Entity>();
            foreach (var road in retention.Roads)
            {
                indexed.Clear(); _sourceIndex.CopyOwner(road, indexed);
                foreach (var source in indexed)
                    if (_replacements.TryGetValue(source, out var entry) && !entry.RoutedCrossing && entry.Paint.Count == 0
                        && (!EntityManager.HasComponent<Owner>(source) || EntityManager.GetComponentData<Owner>(source).m_Owner != road)) return RejectRetainedSource(source, road, "source owner does not match the owner index");
            }
            foreach (var pair in _replacements)
            {
                var source = pair.Key; var replacement = pair.Value;
                if (replacement.RoutedCrossing || replacement.Paint.Count != 0
                    || !EntityManager.HasComponent<Owner>(source)) continue;
                var road = EntityManager.GetComponentData<Owner>(source).m_Owner;
                if (!EntityManager.Exists(road) || !EntityManager.HasComponent<Road>(road)
                    || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Game.Tools.Temp>(road)
                    || lines == null || !lines.RetainableHiddenOwner(road, retention)) continue;
                if (!Alive(source)
                    || EntityManager.HasComponent<Game.Tools.Temp>(source) || !EntityManager.HasComponent<MGRReplacedPaint>(source)
                    || !EntityManager.HasComponent<Game.Tools.Hidden>(source) || !EntityManager.HasComponent<Owner>(source)
                    || !EntityManager.HasComponent<Curve>(source) || !EntityManager.HasComponent<PrefabRef>(source)
                    || _externalPaint.Contains(EntityManager, source))
                { if (retention.Roads.Contains(road)) return RejectRetainedSource(source, road, "source is missing, temporary, revealed, or externally owned"); continue; }
                var prefab = EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if (!retention.StableDefinition(EntityManager, _prefabs, prefab)
                    || !replacement.SourceKey.Equals(new MarkingKey(EntityManager.GetComponentData<Curve>(source).m_Bezier, prefab)))
                { if (retention.Roads.Contains(road)) return RejectRetainedSource(source, road, "native definition or recorded source curve/prefab no longer matches"); continue; }
                retention.Sources.Add(source);
                retention.SourceIdentities.Add(source, (road, prefab, EntityManager.GetComponentData<Curve>(source).m_Bezier));
            }
            return true;
        
            }
        }

        internal bool ValidateRetainedHiddenSources(MarkingSaveRetention retention)
        {
            var probe = new MarkingSaveRetention();
            probe.Begin();
            probe.InheritDefinitionProofs(retention);
            probe.Roads.UnionWith(retention.Roads);
            if (!CaptureRetainedHiddenSources(probe) || !retention.Sources.SetEquals(probe.Sources)) return false;
            foreach (var pair in retention.SourceIdentities)
                if (!probe.SourceIdentities.TryGetValue(pair.Key, out var current) || !current.Equals(pair.Value)) return false;
            return true;
        }

        private void PrepareRetainedSourcesForSerialization()
        {
            CaptureCrosswalkRestorePriority();
            var retention = World.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.MarkingRetention;
            if (retention == null || !retention.Active || retention.Sources.Count == 0)
            { Restore(); World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreAll(); return; }
            { CompleteDependency(); Dependency = default; } RestorePreviewPaint();
            using (var sources = _originals.ToEntityArray(Allocator.Temp))
                foreach (var source in sources) if (!retention.RetainsSource(source)) Remove(source);
            foreach (var source in new List<Entity>(_replacements.Keys))
                if (!retention.RetainsSource(source)) Remove(source);
            using (var paint = _paint.ToEntityArray(Allocator.Temp))
                foreach (var entity in paint) GeneratedVisualLifecycle.Retire(World, entity);
            World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreAll();
            _deferred.Clear(); _retryDue.Clear(); _retryOrder.Clear(); _clipRetries.Clear(); _backlog.Clear(); _sourceOrder.Clear();
            _clickedCrossings.Clear(); _processedClicks.Clear(); _requestedSources.Clear(); _clipFailures.Clear();
            _previousHiddenOwners.Clear(); _currentHiddenOwners.Clear(); _maintenanceSources.Clear(); _initial = true; _white = Entity.Null;
            // Keep only the existing zero-actor source claims and source index.
            // No crossing aliases, replacement paint or disposable copy survives.
        }
    }

    public partial class GeneratedVisualSaveSystem
    {
        internal MarkingSaveRetention MarkingRetention { get; } = new MarkingSaveRetention();

        private void BeginMarkingRetention()
        {
            // An overlapping notification must not replace a live versioned ticket.
            if (MarkingRetention.Active) return;
            if (!MarkingSerializationProfile.Supported)
            {
                Mod.Log.Warn("Marking retention preflight rejected before GPU wait: unreviewed native modules.");
                return;
            }
            if (!MarkingSerializationQuery.Supports(World))
            {
                Mod.Log.Warn("Marking retention preflight rejected before GPU wait: native serializer query no longer excludes held actors.");
                return;
            }
            NativeVisualInvalidation.CompleteReaders(World);
            World.GetExistingSystemManaged<VisualLifecycleSystem>()?.FlushPending();
            MarkingRetention.Begin();
            World.GetExistingSystemManaged<CurbLineEmissionSystem>()?.CaptureMarkingRetention(MarkingRetention);
            var paint = World.GetExistingSystemManaged<IntersectionPaintSystem>();
            if (paint != null && !paint.CaptureRetainedHiddenSources(MarkingRetention))
            {
                Mod.Log.Warn("Marking retention preflight rejected before GPU wait: " + paint.RetentionSourceFailure);
                CancelMarkingRetention();
                return;
            }
            if (!PrepareMarkingSerializationBeforeSaveWait()) return;
            markingRetentionSealed = true;
            Mod.Log.Info("Save retention preflight complete before GPU wait: road sections=" + MarkingRetention.Roads.Count
                + " actors=" + MarkingRetention.Actors.Count + " hiddenSources=" + MarkingRetention.Sources.Count + ".");
        }
        private void CopyRetirementCandidates(NativeArray<Entity> snapshot, NativeList<Entity> candidates)
        {
            foreach (var entity in snapshot) if (!MarkingRetention.Retains(entity)) candidates.Add(entity);
        }
        internal void CancelMarkingRetention()
        {
            if (markingRetentionSealed)
            {
                DeferMarkingReconcileAfterSave("retention cancellation requested after the early save boundary");
                return;
            }
            RestoreMarkingSerializationScope();
            MarkingRetention.Clear();
            World.GetExistingSystemManaged<CurbLineEmissionSystem>()?.EndMarkingRetention();
        }
        private bool markingRetentionSealed;
        private bool markingReconcileAfterSave;

        internal void DeferMarkingReconcileAfterSave(string reason)
        {
            if (!MarkingRetention.Active || markingReconcileAfterSave) return;
            markingReconcileAfterSave = true;
            Mod.Log.Warn("Marking retention changed after early save boundary; Curbside defers held-actor retirement to normal post-save reconciliation: " + reason);
        }

        private void EndMarkingRetention()
        {
            RestoreMarkingSerializationScope();
            if (markingReconcileAfterSave)
                World.GetExistingSystemManaged<CurbLineEmissionSystem>()?.QueueRetainedMarkingReconcile(MarkingRetention);
            markingRetentionSealed = false;
            markingReconcileAfterSave = false;
            MarkingRetention.Clear();
            World.GetExistingSystemManaged<CurbLineEmissionSystem>()?.EndMarkingRetention();
        }
        private void ValidateMarkingRetention()
        {
            if (!MarkingRetention.Active)
            { Mod.Log.Info("Save retention final: actors=0 hiddenSources=0; original early cleanup used."); return; }
            if (World.GetExistingSystemManaged<CurbLineEmissionSystem>()?.ValidateMarkingRetention() != false
                && World.GetExistingSystemManaged<IntersectionPaintSystem>()?.ValidateRetainedHiddenSources(MarkingRetention) != false)
                return;
            // The native pre-save frame wait has already passed. Preserve the
            // frozen exclusion/actor ticket, then use the normal post-save queue.
            DeferMarkingReconcileAfterSave("captured road, actor or hidden-source state changed");
        }
    }
}
