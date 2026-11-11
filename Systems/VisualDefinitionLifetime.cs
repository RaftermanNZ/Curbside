using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class VisualLifecycleSystem
    {
        private readonly VisualDefinitionOrigins definitionOrigins = new VisualDefinitionOrigins();
        private readonly HashSet<Entity> readyDefinitions = new HashSet<Entity>();
        private bool definitionRepairPending;

        internal void TrackDefinition(Entity copy, Entity original, string producer)
        {
            definitionOrigins.Track(copy, original, producer);
            if (definitionOrigins.HasDependents(original)) readyDefinitions.Remove(original);
        }

        private void DefinitionDependencyReleased(Entity definition)
        {
            if (!definitions.ContainsKey(definition)) return;
            readyDefinitions.Add(definition);
            definitionRepairPending = true;
        }

        private bool DefinitionExists(Entity entity) => EntityManager.Exists(entity);

        private bool OriginalDefinition(Entity copy, out Entity original)
        {
            if (!definitionOrigins.Resolve(copy, DefinitionExists, out original)) return false;
            // A Curbside disposable copy without creation ancestry is not proof
            // of a stable original. Never guess a replacement by name or distance.
            return !EntityManager.HasComponent<MGRPrivateVisualPrefab>(original)
                && !definitions.ContainsKey(original);
        }

        private InvalidOperationException DefinitionError(Entity copy, Entity actor, Entity original)
        {
            return new InvalidOperationException("Missing original visual definition before serialization: " + original
                + "; copy=" + copy + " producer=" + definitionOrigins.Producer(copy)
                + " actor=" + actor + " actorDeleted=" + EntityManager.HasComponent<Deleted>(actor)
                + ". Referenced definition ancestry could not be resolved; no fallback was substituted.");
        }

        private void PrepareDefinitionReferences()
        {
            // Validate EVERY targeted reference before making the first change.
            // Unreferenced stale records do not require an original to exist.
            var originals = new Dictionary<Entity, Entity>();
            var restore = new Dictionary<Entity, Entity>();
            using (var entities = references.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var copy = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (!definitionSources.ContainsKey(copy)) continue;
                    if (!originals.TryGetValue(copy, out var original))
                    {
                        if (!OriginalDefinition(copy, out original)) throw DefinitionError(copy, entity, original);
                        originals.Add(copy, original);
                    }
                    restore[entity] = original;
                }
            foreach (var pair in restore)
            {
                EntityManager.SetComponentData(pair.Key, new PrefabRef(pair.Value));
                if (!EntityManager.HasComponent<Deleted>(pair.Key)) NativeVisualInvalidation.ApplyMark(EntityManager, pair.Key);
            }
        }
    }
}
