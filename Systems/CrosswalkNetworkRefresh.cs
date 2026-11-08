using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public struct MGRCrosswalkRestoreRequest : IComponentData { }

    public partial class CrosswalkVisualSystem
    {
        private readonly ManagedWorkIndex<Entity, Entity> _sourceOwners = new ManagedWorkIndex<Entity, Entity>();
        private readonly Dictionary<Entity, Entity> _networkRestored = new Dictionary<Entity, Entity>();
        private readonly HashSet<Entity> _refreshOwners = new HashSet<Entity>();
        private readonly List<Entity> _refreshSources = new List<Entity>();
        private readonly Dictionary<Entity, Entity> _refreshWrites = new Dictionary<Entity, Entity>();

        private void TrackSource(Entity source)
        {
            _sources.Add(source);
            if (EntityManager.HasComponent<Owner>(source))
                _sourceOwners.Add(EntityManager.GetComponentData<Owner>(source).m_Owner, source);
        }

        private Entity CommittedOwner(Entity owner)
        {
            for (int depth = 0; depth < 8 && EntityManager.Exists(owner); depth++)
            {
                if (!EntityManager.HasComponent<Temp>(owner)) return owner;
                var next = EntityManager.GetComponentData<Temp>(owner).m_Original;
                if (next == owner) return Entity.Null;
                owner = next;
            }
            return Entity.Null;
        }

        private void BeginNetworkRefresh()
        {
            if (_sources.Count == 0 || _networkChanges.IsEmptyIgnoreFilter) return;
            _refreshOwners.Clear(); _refreshSources.Clear(); _refreshWrites.Clear();
            Entity request = Entity.Null;
            using (var owners = _networkChanges.ToEntityArray(Allocator.Temp))
                foreach (var owner in owners)
                {
                    var original = CommittedOwner(owner);
                    if (original == Entity.Null) continue;
                    if (request == Entity.Null) request = owner;
                    _refreshOwners.Add(original);
                    if (EntityManager.HasComponent<Edge>(original))
                    {
                        var edge = EntityManager.GetComponentData<Edge>(original);
                        _refreshOwners.Add(edge.m_Start); _refreshOwners.Add(edge.m_End);
                    }
                }
            foreach (var owner in _refreshOwners) _sourceOwners.CopyOwner(owner, _refreshSources);
            foreach (var source in _refreshSources)
            {
                if (!EntityManager.Exists(source) || EntityManager.HasComponent<Deleted>(source)
                    || EntityManager.HasComponent<Temp>(source) || !EntityManager.HasComponent<PrefabRef>(source)) continue;
                var current = EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if (!_reverse.TryGetValue(current, out var native)) continue;
                _refreshWrites[source] = native;
                _networkRestored[source] = native;
            }
            if (_refreshWrites.Count == 0) return;
            // One event marker for the whole native batch, including a rebuild
            // which produces no changed temporary crossing lane to wake the pass.
            if (request != Entity.Null && !EntityManager.HasComponent<MGRCrosswalkRestoreRequest>(request))
                EntityManager.AddComponent<MGRCrosswalkRestoreRequest>(request);
            ApplyNetworkDefinitions();
        }

        internal void FinishNetworkRefresh()
        {
            if (_networkRestored.Count == 0) return;
            _refreshWrites.Clear();
            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            foreach (var pair in _networkRestored)
            {
                var source = pair.Key;
                if (!EntityManager.Exists(source) || EntityManager.HasComponent<Deleted>(source)
                    || EntityManager.HasComponent<Temp>(source) || !EntityManager.HasComponent<PrefabRef>(source)) continue;
                var current = EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if (_reverse.ContainsKey(current)) continue;
                if (current != pair.Value) { _sources.Remove(source); _sourceOwners.Remove(source); continue; }
                if (!EntityManager.HasComponent<NetLaneGeometryData>(current)) continue;
                if (!choices.IsSourceSelected(source) && !choices.IsSourceInvisible(source)
                    && !EntityManager.HasComponent<MGRHiddenJunctionLine>(source))
                { _sources.Remove(source); _sourceOwners.Remove(source); continue; }
                TrackSource(source);
                _refreshWrites[source] = SuppressedDefinition(current);
            }
            _networkRestored.Clear();
            ApplyNetworkDefinitions();
        }

        private void ApplyNetworkDefinitions()
        {
            if (_refreshWrites.Count == 0) return;
            using (var batch = new EntityCommandBuffer(Allocator.Temp))
            {
                foreach (var pair in _refreshWrites)
                {
                    batch.SetComponent(pair.Key, new PrefabRef(pair.Value));
                    if (!EntityManager.HasComponent<Updated>(pair.Key)) batch.AddComponent<Updated>(pair.Key);
                    if (!EntityManager.HasComponent<BatchesUpdated>(pair.Key)) batch.AddComponent<BatchesUpdated>(pair.Key);
                }
                NativeVisualInvalidation.CompleteReaders(World);
                batch.Playback(EntityManager);
            }
            _refreshWrites.Clear();
        }
    }
}
