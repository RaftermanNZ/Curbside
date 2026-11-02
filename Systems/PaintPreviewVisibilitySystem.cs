using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Serialization;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class PaintPreviewVisibilityPolicy
    {
        internal static bool Suppress(bool originalExists,bool owned,bool simulationLane,bool external)
            => originalExists&&owned&&!simulationLane&&!external;
    }

    // Routed/simulation lanes are excluded. Plain paint uses render-only preview
    // aliases; object decals retain their existing owned-flag fallback.
    public partial class PaintPreviewVisibilitySystem : GameSystemBase, IPreSerialize
    {
        private EntityQuery _previews;
        private readonly HashSet<Entity> _owned=new HashSet<Entity>();
        private readonly HashSet<Entity> _renderOnly=new HashSet<Entity>();
        private readonly List<Entity> _release=new List<Entity>();
        private readonly ExternalPaintOwnership _external=new ExternalPaintOwnership();
        protected override void OnCreate()
        {
            base.OnCreate();
            _previews=GetEntityQuery(new EntityQueryDesc {
                All=new[]{ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<PrefabRef>()},
                Any=new[]{ComponentType.ReadOnly<Game.Net.Lane>(),ComponentType.ReadOnly<Game.Objects.ObjectGeometry>()},
                None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Game.Net.CarLane>(),ComponentType.ReadOnly<Game.Net.ParkingLane>(),ComponentType.ReadOnly<Game.Net.PedestrianLane>(),ComponentType.ReadOnly<Game.Net.TrackLane>()}});
        }
        private bool ShouldHide(Entity entity)
        {
            if(!EntityManager.Exists(entity)||EntityManager.HasComponent<Deleted>(entity)||!EntityManager.HasComponent<Temp>(entity))return false;
            var original=EntityManager.GetComponentData<Temp>(entity).m_Original;
            bool exists=EntityManager.Exists(original)&&!EntityManager.HasComponent<Deleted>(original);
            bool owned=exists&&(EntityManager.HasComponent<MGRHiddenJunctionLine>(original)||EntityManager.HasComponent<MGRReplacedPaint>(original)||EntityManager.HasComponent<RDCHiddenDecal>(original));
            if(!owned)return false;
            bool simulation=EntityManager.HasComponent<Game.Net.CarLane>(entity)||EntityManager.HasComponent<Game.Net.ParkingLane>(entity)||EntityManager.HasComponent<Game.Net.PedestrianLane>(entity)||EntityManager.HasComponent<Game.Net.TrackLane>(entity);
            return PaintPreviewVisibilityPolicy.Suppress(exists,owned,simulation,_external.Contains(EntityManager,entity)||exists&&_external.Contains(EntityManager,original));
        }
        private void Release(Entity entity)
        {
            if(EntityManager.Exists(entity)&&EntityManager.HasComponent<Hidden>(entity))
            {
                bool parentHidden=EntityManager.HasComponent<Owner>(entity)&&EntityManager.HasComponent<Hidden>(EntityManager.GetComponentData<Owner>(entity).m_Owner);
                if(!parentHidden){EntityManager.RemoveComponent<Hidden>(entity);Changed(entity);}
            }
            _owned.Remove(entity);
        }
        private void Changed(Entity entity){if(!EntityManager.HasComponent<BatchesUpdated>(entity))EntityManager.AddComponent<BatchesUpdated>(entity);}
        internal void Restore()
        {
            World.GetExistingSystemManaged<CrosswalkVisualSystem>()?.RestorePlainPreviews();
            _renderOnly.Clear();
            if(_owned.Count==0)return;
            Dependency.Complete();_release.Clear();_release.AddRange(_owned);
            foreach(var entity in _release)Release(entity);
            _release.Clear();
        }
        public void PreSerialize(Context context)=>Restore();
        protected override void OnGamePreload(Purpose purpose,GameMode mode){Restore();_external.Clear();base.OnGamePreload(purpose,mode);}
        protected override void OnUpdate()
        {
            if(Mod.Saving||_previews.IsEmptyIgnoreFilter&&_owned.Count==0&&_renderOnly.Count==0)return;
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try {
                Dependency.Complete();_release.Clear();
                var visual=World.GetOrCreateSystemManaged<CrosswalkVisualSystem>();
                foreach(var entity in _renderOnly)if(!ShouldHide(entity))_release.Add(entity);
                foreach(var entity in _release){visual.RestorePreviewLine(entity);_renderOnly.Remove(entity);}
                _release.Clear();
                foreach(var entity in _owned)if(!ShouldHide(entity))_release.Add(entity);
                foreach(var entity in _release)Release(entity);
                using(var entities=_previews.ToEntityArray(Allocator.Temp))foreach(var entity in entities)
                {
                    if(!ShouldHide(entity))continue;
                    if(visual.SuppressPreviewLine(entity))
                    {
                        _renderOnly.Add(entity);
                        // Release only flags owned by this guard, never native ones.
                        if(_owned.Contains(entity))Release(entity);
                        continue;
                    }
                    // Never fall back to the native Hidden fight for lanes whose
                    // render definition is unavailable. Object decals alone use it.
                    if(EntityManager.HasComponent<Game.Net.Lane>(entity))continue;
                    if(EntityManager.HasComponent<Hidden>(entity))continue; // Don't adopt native/external hidden flags.
                    EntityManager.AddComponent<Hidden>(entity);_owned.Add(entity);Changed(entity);
                }
            } catch(System.Exception error) {Restore();Enabled=false;Mod.Log.Error("Preview paint visibility paused: "+error);}
            finally {ModDiagnostics.RuntimeCosts.End("PaintPreviewVisibilitySystem",started);}
        }
        protected override void OnDestroy(){Restore();base.OnDestroy();}
    }
}
