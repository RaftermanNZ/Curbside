using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Only the render layers differ. Routing data and the managed prefab identity
    // remain those of the original crossing. Never hide/delete a pedestrian lane.
    internal static class CrosswalkVisualPolicy
    {
        internal static NetLaneGeometryData WithoutPaint(NetLaneGeometryData data)
        { data.m_GameLayers=0; data.m_EditorLayers=0; return data; }
    }
    public partial class CrosswalkVisualSystem : GameSystemBase
    {
        private readonly Dictionary<Entity,Entity> _copies=new Dictionary<Entity,Entity>();
        private readonly Dictionary<Entity,Entity> _reverse=new Dictionary<Entity,Entity>();
        private readonly HashSet<Entity> _sources=new HashSet<Entity>();
        private readonly HashSet<Entity> _previewSources=new HashSet<Entity>();
        private EntityQuery _references, _networkChanges;
        protected override void OnCreate()
        {
            base.OnCreate();
            _networkChanges=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Updated>()},Any=new[]{ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<Game.Net.Node>()}});
            _references=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<PrefabRef>()},Options=EntityQueryOptions.IncludeDisabledEntities|EntityQueryOptions.IncludePrefab});
        }
        protected override void OnUpdate() { }
        internal Entity Original(Entity prefab) => _reverse.TryGetValue(prefab,out var original)?original:prefab;
        private void Change(Entity source,Entity prefab)
        {
            NativeVisualInvalidation.SetPrefab(World,source,prefab);
        }
        internal void Suppress(Entity source)
        {
            if(!EntityManager.Exists(source)||!EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||!EntityManager.HasComponent<PrefabRef>(source))return;
            if((EntityManager.GetComponentData<Game.Net.PedestrianLane>(source).m_Flags&PedestrianLaneFlags.Crosswalk)==0)return;
            var current=EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
            if(_reverse.ContainsKey(current)){TrackSource(source);return;}
            if(!EntityManager.HasComponent<NetLaneGeometryData>(current))return;
            TrackSource(source);
            Change(source,SuppressedDefinition(current));
        }
        private Entity SuppressedDefinition(Entity current)
        {
            if(!_copies.TryGetValue(current,out var copy))
            {
                copy=EntityManager.Instantiate(current);
                World.GetOrCreateSystemManaged<VisualLifecycleSystem>().TrackDefinition(copy,current,"committed-crosswalk");
                EntityManager.AddComponent<MGRPrivateVisualPrefab>(copy);
                if(EntityManager.HasComponent<Created>(copy))EntityManager.RemoveComponent<Created>(copy);
                if(EntityManager.HasComponent<Updated>(copy))EntityManager.RemoveComponent<Updated>(copy);
                if(EntityManager.HasComponent<PrefabData>(copy))EntityManager.SetComponentEnabled<PrefabData>(copy,false);
                EntityManager.SetComponentData(copy,CrosswalkVisualPolicy.WithoutPaint(EntityManager.GetComponentData<NetLaneGeometryData>(current)));
                _copies.Add(current,copy);_reverse.Add(copy,current);
            }
            return copy;
        }
        internal void RestoreSource(Entity source)
        {
            if(EntityManager.Exists(source)&&EntityManager.HasComponent<PrefabRef>(source))
            {
                var current=EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if(_reverse.TryGetValue(current,out var original)&&EntityManager.Exists(original))Change(source,original);
            }
            _sources.Remove(source);_sourceOwners.Remove(source);
        }
        internal void SuppressPreview(Entity source)
        {
            Suppress(source);if(_sources.Contains(source))_previewSources.Add(source);
        }
        internal void RestorePreviewReferences()
        {
            if(_previewSources.Count==0)return;Dependency.Complete();
            foreach(var source in _previewSources)RestoreSource(source);
            _previewSources.Clear();
        }
        internal void RestoreForNetworkUpdate()
        {
            BeginNetworkRefresh();
        }
        internal void RestoreAll()
        {
            if(_copies.Count==0)return;
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("crosswalk.restoreAll","definitions="+_copies.Count+" sources="+_sources.Count);
            Dependency.Complete();
            // Save/disposal only: also find references copied by native temp or
            // road regeneration jobs, not just the originally selected lanes.
            using(var refs=_references.ToEntityArray(Allocator.Temp))foreach(var entity in refs)RestoreSource(entity);
            foreach(var copy in _copies.Values)GeneratedVisualLifecycle.RetireDefinition(World, copy, _reverse[copy]);
            _copies.Clear();_reverse.Clear();_sources.Clear();_previewSources.Clear();_sourceOwners.Clear();_networkRestored.Clear();
        }
        protected override void OnDestroy(){RestoreAll();base.OnDestroy();}
    }
    public partial class CrosswalkVisualRestoreSystem : GameSystemBase
    {
        protected override void OnUpdate()
        { if(!Mod.Saving)World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreForNetworkUpdate(); }
    }
}
