using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class DecalVisibilitySystem
    {
        private EntityQuery _readyDecalDefinitions;
        private readonly HashSet<Entity> unreadyDecalKinds = new HashSet<Entity>();
        private readonly ManagedWorkIndex<Entity, Entity> pendingDecalKinds = new ManagedWorkIndex<Entity, Entity>();
        private readonly List<Entity> readyDecalScratch = new List<Entity>();
        private readonly HashSet<Entity> readyDecalSources = new HashSet<Entity>();

        private bool TryReadDecalDefinition(Entity prefab, out string name)
        {
            name = null;
            // Registration is required for the managed lookup. Repeated
            // requests for an unregistered definition need only type presence.
            if (unreadyDecalKinds.Contains(prefab) && !EntityManager.HasComponent<PrefabData>(prefab)) return false;
            if (!_prefabs.TryGetPrefab<PrefabBase>(prefab, out var definition) || definition == null)
            { unreadyDecalKinds.Add(prefab); return false; }
            if (definition is ObjectGeometryPrefab geometry
                && (geometry.m_Meshes == null || geometry.m_Meshes.Length != 0
                    && (!EntityManager.HasComponent<ObjectGeometryData>(prefab) || !EntityManager.HasBuffer<SubMesh>(prefab))))
            { unreadyDecalKinds.Add(prefab); return false; }
            // A valid empty geometry array (e.g. an invisible helper prop) is a
            // completed unsupported result, not a reason to retry forever.
            unreadyDecalKinds.Remove(prefab);
            name = definition.name;
            return true;
        }

        private void CollectReadyDecalKinds()
        {
            readyDecalSources.Clear();
            if (pendingDecalKinds.Count == 0) return;
            // Structural availability only; no payload-change filter, extra
            // system, timer, actor scan or retained native view is required.
            using (var definitions = _readyDecalDefinitions.ToEntityArray(Allocator.Temp))
                foreach (var definition in definitions)
                {
                    readyDecalScratch.Clear();
                    pendingDecalKinds.CopyOwner(definition, readyDecalScratch);
                    foreach (var source in readyDecalScratch) readyDecalSources.Add(source);
                }
            readyDecalScratch.Clear();
        }

        private void ClearDecalReadiness()
        {
            unreadyDecalKinds.Clear();
            pendingDecalKinds.Clear();
            readyDecalScratch.Clear();
            readyDecalSources.Clear();
        }
    }
}
