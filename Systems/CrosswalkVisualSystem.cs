using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Only the render layers differ. Routing data and the managed prefab identity
    // remain those of the original crossing. Never hide/delete a pedestrian lane.
    internal static class CrosswalkVisualPolicy
    {
        internal static NetLaneGeometryData WithoutPaint(NetLaneGeometryData data)
        { data.m_GameLayers=0; data.m_EditorLayers=0; return data; }
    }
    internal static class PaintPreviewContinuityPolicy
    {
        internal static bool SamePath(Colossal.Mathematics.Bezier4x3 a,Colossal.Mathematics.Bezier4x3 b)
            =>Near(a.a,b.a)&&Near(a.b,b.b)&&Near(a.c,b.c)&&Near(a.d,b.d);
        private static bool Near(float3 a,float3 b)=>math.all(math.isfinite(a))&&math.all(math.isfinite(b))&&math.distancesq(a,b)<=.000001f;
    }
    public partial class CrosswalkVisualSystem : GameSystemBase
    {
        private readonly Dictionary<Entity,Entity> _copies=new Dictionary<Entity,Entity>();
        private readonly Dictionary<Entity,Entity> _reverse=new Dictionary<Entity,Entity>();
        private readonly HashSet<Entity> _sources=new HashSet<Entity>();
        private readonly HashSet<Entity> _previewSources=new HashSet<Entity>();
        private EntityQuery _references, _networkChanges, _temporaryCrossings, _temporaryReferences;
        private readonly ExternalPaintOwnership _previewExternal=new ExternalPaintOwnership();
        private readonly HashSet<Entity> _inheritedPreviews=new HashSet<Entity>();
        private readonly HashSet<Entity> _plainPreviews=new HashSet<Entity>();
        private readonly List<Entity> _stalePreviews=new List<Entity>();
        private EntityQuery _temporaryPaint;
        private readonly HashSet<Entity> _unchangedPreviewSources=new HashSet<Entity>(),_changedPreviewSources=new HashSet<Entity>();
        private int _continuityFrame=-1;
        protected override void OnCreate()
        {
            base.OnCreate();
            _temporaryPaint=GetEntityQuery(ComponentType.ReadOnly<Game.Tools.Temp>(),ComponentType.ReadOnly<Lane>(),ComponentType.ReadOnly<Curve>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Net.CarLane>(),ComponentType.Exclude<Game.Net.ParkingLane>(),ComponentType.Exclude<Game.Net.TrackLane>());
            _temporaryCrossings=GetEntityQuery(ComponentType.ReadOnly<Game.Tools.Temp>(),ComponentType.ReadOnly<Game.Net.PedestrianLane>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Deleted>());
            _temporaryReferences=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Game.Tools.Temp>(),ComponentType.ReadOnly<PrefabRef>()},Options=EntityQueryOptions.IncludeDisabledEntities});
            _networkChanges=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Updated>()},Any=new[]{ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<Game.Net.Node>()},None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}});
            _references=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<PrefabRef>()},Options=EntityQueryOptions.IncludeDisabledEntities|EntityQueryOptions.IncludePrefab});
        }
        protected override void OnUpdate() { }
        internal bool UnchangedPreview(Entity source)=>_unchangedPreviewSources.Contains(source);
        internal void RefreshPreviewContinuity()
        {
            // One read-only pass over active paint previews, not all city roads.
            if(_continuityFrame==UnityEngine.Time.frameCount)return;
            _continuityFrame=UnityEngine.Time.frameCount;
            _unchangedPreviewSources.Clear();_changedPreviewSources.Clear();
            if(_temporaryPaint.IsEmptyIgnoreFilter)return;
            using(var items=_temporaryPaint.ToEntityArray(Allocator.Temp))foreach(var item in items)
            {
                var temp=EntityManager.GetComponentData<Game.Tools.Temp>(item);var original=temp.m_Original;
                if((temp.m_Flags&(Game.Tools.TempFlags.Delete|Game.Tools.TempFlags.Cancel|Game.Tools.TempFlags.Hidden))!=0
                    ||!EntityManager.Exists(original)||EntityManager.HasComponent<Deleted>(original)||!EntityManager.HasComponent<Curve>(original)
                    ||!EntityManager.HasComponent<MGRReplacedPaint>(original)||_previewExternal.Contains(EntityManager,item)||_previewExternal.Contains(EntityManager,original))continue;
                if(PaintPreviewContinuityPolicy.SamePath(EntityManager.GetComponentData<Curve>(item).m_Bezier,EntityManager.GetComponentData<Curve>(original).m_Bezier))_unchangedPreviewSources.Add(original);
                else _changedPreviewSources.Add(original);
            }
            // Ambiguous duplicate previews fail closed: never retain old paint
            // when any active copy proposes a different curve for that source.
            _unchangedPreviewSources.ExceptWith(_changedPreviewSources);
        }
        internal Entity Original(Entity prefab) => _reverse.TryGetValue(prefab,out var original)?original:prefab;
        private void Change(Entity source,Entity prefab)
        {
            EntityManager.SetComponentData(source,new PrefabRef(prefab));
            if(!EntityManager.HasComponent<Updated>(source))EntityManager.AddComponent<Updated>(source);
            if(!EntityManager.HasComponent<BatchesUpdated>(source))EntityManager.AddComponent<BatchesUpdated>(source);
        }
        internal void Suppress(Entity source)
        {
            if(!EntityManager.Exists(source)||!EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||!EntityManager.HasComponent<PrefabRef>(source))return;
            if((EntityManager.GetComponentData<Game.Net.PedestrianLane>(source).m_Flags&PedestrianLaneFlags.Crosswalk)==0)return;
            SuppressGeometry(source);
        }
        // Plain paint previews use the same render-only aliases as crossings.
        // No routing lane, original owner, shared asset or Hidden flag is edited.
        internal bool SuppressPreviewLine(Entity source)
        {
            if(!EntityManager.Exists(source)||!EntityManager.HasComponent<Game.Tools.Temp>(source)
                ||!EntityManager.HasComponent<Lane>(source)||EntityManager.HasComponent<Deleted>(source)
                ||EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)
                ||EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||EntityManager.HasComponent<Game.Net.TrackLane>(source)
                ||!EntityManager.HasComponent<PrefabRef>(source))return false;
            if(!SuppressGeometry(source))return false;
            _plainPreviews.Add(source);return true;
        }
        internal void RestorePreviewLine(Entity source)
        {
            if(_plainPreviews.Remove(source))RestoreSource(source);
        }
        internal void RestorePlainPreviews()
        {
            if(_copies.Count==0&&_plainPreviews.Count==0)return;Dependency.Complete();
            // Native tools can copy a temporary reference after the last render
            // update. Restore those exact aliases too, before any temp is committed.
            using(var refs=_temporaryReferences.ToEntityArray(Allocator.Temp))foreach(var source in refs)RestoreSource(source);
            foreach(var source in _plainPreviews)RestoreSource(source);
            _plainPreviews.Clear();
        }
        private bool SuppressGeometry(Entity source)
        {
            var current=EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
            if(_reverse.ContainsKey(current)){_sources.Add(source);return true;}
            if(!EntityManager.HasComponent<NetLaneGeometryData>(current))return false;
            if(!_copies.TryGetValue(current,out var copy))
            {
                copy=EntityManager.Instantiate(current);
                // Copies are never registered as new game definitions, and are
                // destroyed before native prefab serialization starts.
                try
                {
                    if(EntityManager.HasComponent<Created>(copy))EntityManager.RemoveComponent<Created>(copy);
                    if(EntityManager.HasComponent<Updated>(copy))EntityManager.RemoveComponent<Updated>(copy);
                    if(EntityManager.HasComponent<PrefabData>(copy))EntityManager.SetComponentEnabled<PrefabData>(copy,false);
                    EntityManager.SetComponentData(copy,CrosswalkVisualPolicy.WithoutPaint(EntityManager.GetComponentData<NetLaneGeometryData>(current)));
                    _copies.Add(current,copy);_reverse.Add(copy,current);
                }
                catch
                {
                    _copies.Remove(current);_reverse.Remove(copy);
                    if(EntityManager.Exists(copy))EntityManager.DestroyEntity(copy);
                    throw;
                }
                if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("crosswalk.visualCopy",current+" -> "+copy);
            }
            _sources.Add(source);Change(source,copy);return true;
        }
        internal void RestoreSource(Entity source)
        {
            if(EntityManager.Exists(source)&&EntityManager.HasComponent<PrefabRef>(source))
            {
                var current=EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                if(_reverse.TryGetValue(current,out var original)&&EntityManager.Exists(original))Change(source,original);
            }
            _sources.Remove(source);
        }
        internal void SuppressPreview(Entity source)
        {
            Suppress(source);if(_sources.Contains(source))_previewSources.Add(source);
        }
        internal void InheritPreviewPaint()
        {
            if(Mod.Saving||_temporaryCrossings.IsEmptyIgnoreFilter&&_inheritedPreviews.Count==0)return;
            Dependency.Complete();_stalePreviews.Clear();
            foreach(var source in _inheritedPreviews)
                if(!EntityManager.Exists(source)||!EntityManager.HasComponent<Game.Tools.Temp>(source)||EntityManager.HasComponent<Deleted>(source))_stalePreviews.Add(source);
            foreach(var source in _stalePreviews){RestoreSource(source);_inheritedPreviews.Remove(source);}
            using(var items=_temporaryCrossings.ToEntityArray(Allocator.Temp))foreach(var source in items)
            {
                var temp=EntityManager.GetComponentData<Game.Tools.Temp>(source);
                var original=temp.m_Original;
                // Exact original only; no spatial guesses and no pedestrian routing edits.
                bool inherit=(temp.m_Flags&(Game.Tools.TempFlags.Delete|Game.Tools.TempFlags.Cancel|Game.Tools.TempFlags.Hidden))==0&&EntityManager.Exists(original)&&!EntityManager.HasComponent<Deleted>(original)
                    &&EntityManager.HasComponent<MGRReplacedPaint>(original)&&EntityManager.HasComponent<PrefabRef>(original)
                    &&_reverse.ContainsKey(EntityManager.GetComponentData<PrefabRef>(original).m_Prefab);
                inherit=inherit&&!EntityManager.HasComponent<Game.Net.CarLane>(source)&&!EntityManager.HasComponent<Game.Net.ParkingLane>(source)&&!EntityManager.HasComponent<Game.Net.TrackLane>(source)
                    &&!_previewExternal.Contains(EntityManager,source)&&!_previewExternal.Contains(EntityManager,original);
                if(!inherit){if(_inheritedPreviews.Remove(source))RestoreSource(source);continue;}
                Suppress(source);if(_sources.Contains(source))_inheritedPreviews.Add(source);
            }
        }
        internal void RestoreInheritedPreviews()
        {
            if(_inheritedPreviews.Count==0)return;Dependency.Complete();
            foreach(var source in _inheritedPreviews)RestoreSource(source);
            _inheritedPreviews.Clear();
        }
        internal void RestorePreviewReferences()
        {
            if(_previewSources.Count==0)return;Dependency.Complete();
            foreach(var source in _previewSources)RestoreSource(source);
            _previewSources.Clear();
        }
        internal void RestoreForNetworkUpdate()
        {
            if(_sources.Count==0 || _networkChanges.IsEmptyIgnoreFilter)return;
            Dependency.Complete();
            foreach(var source in new List<Entity>(_sources))
                if(!EntityManager.HasComponent<Game.Tools.Temp>(source))RestoreSource(source);
        }
        internal void RestoreAll()
        {
            if(_copies.Count==0)return;
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("crosswalk.restoreAll","definitions="+_copies.Count+" sources="+_sources.Count);
            Dependency.Complete();
            // Save/disposal only: also find references copied by native temp or
            // road regeneration jobs, not just the originally selected lanes.
            using(var refs=_references.ToEntityArray(Allocator.Temp))foreach(var entity in refs)RestoreSource(entity);
            foreach(var copy in _copies.Values)if(EntityManager.Exists(copy))EntityManager.DestroyEntity(copy);
            _copies.Clear();_reverse.Clear();_sources.Clear();_previewSources.Clear();_inheritedPreviews.Clear();_plainPreviews.Clear();_stalePreviews.Clear();
            _unchangedPreviewSources.Clear();_changedPreviewSources.Clear();_continuityFrame=-1;
        }
        protected override void OnDestroy(){RestoreAll();base.OnDestroy();}
    }
    public partial class CrosswalkVisualRestoreSystem : GameSystemBase
    {
        protected override void OnUpdate()
        { if(!Mod.Saving)World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreForNetworkUpdate(); }
    }
}
