using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class GeneratedPaintOwnerPolicy
    {
        internal static bool Retire(bool exists, bool deleted, bool hasPrefab)
            => !exists || deleted || !hasPrefab;
    }

    // GroundHeightSystem's lane iterator reads the owner's PrefabRef directly.
    // Generated markings and decorative cables are not in native SubLane buffers, so native road deletion
    // cannot be relied on to retire it with its road/node. Evict only our paint
    // before the terrain job, rather than waiting for marking emission in phase 5.
    public partial class GeneratedPaintOwnerCleanupSystem : GameSystemBase
    {
        private EntityQuery _paint, _networkChanges;
        private readonly Dictionary<Entity, bool> _ownerStatus = new Dictionary<Entity, bool>();
        private readonly ManagedWorkIndex<Entity,Entity> _index=new ManagedWorkIndex<Entity,Entity>();
        private readonly List<Entity> _work=new List<Entity>(), _audit=new List<Entity>(64);
        private bool _initialized;
        internal void Track(Entity entity,Entity owner)=>_index.Add(owner,entity);
        internal void Untrack(Entity entity)=>_index.Remove(entity);
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode)
        {_index.Clear();_work.Clear();_audit.Clear();_initialized=false;base.OnGamePreload(purpose,mode);}
        protected override void OnDestroy(){_index.Clear();base.OnDestroy();}

        protected override void OnCreate()
        {
            base.OnCreate();
            _paint = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Lane>() },
                Any = new[] { ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            _networkChanges = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Edge>() },
                    Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
                },
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Node>() },
                    Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
                });

            RequireForUpdate(_networkChanges);
        }

        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdatePerformanceCore();}finally{ModDiagnostics.RuntimeCosts.End("GeneratedPaintOwnerCleanupSystem",started);}
        }
        private void UpdatePerformanceCore()
        {
            if (Mod.Saving) return;
            if(_paint.IsEmpty){_index.Clear();_initialized=false;return;}
            Dependency.Complete();
            _ownerStatus.Clear();
            int removed = 0;
            // One bootstrap covers pre-existing entities. New visuals register
            // their managed IDs through GeneratedVisualLifecycle.Track.
            if(!_initialized)
            {
                using(var entities=_paint.ToEntityArray(Allocator.Temp))
                using(var owners=_paint.ToComponentDataArray<Owner>(Allocator.Temp))
                    for(int i=0;i<entities.Length;i++)_index.Add(owners[i].m_Owner,entities[i]);
                _initialized=true;
            }
            _work.Clear();_audit.Clear();
            using(var owners=_networkChanges.ToEntityArray(Allocator.Temp))foreach(var owner in owners)_index.CopyOwner(owner,_work);
            _index.Rotate(_audit,64);_work.AddRange(_audit);
            foreach(var entity in _work)
            {
                if(!EntityManager.Exists(entity)||EntityManager.HasComponent<Deleted>(entity)){_index.Remove(entity);continue;}
                if(!EntityManager.HasComponent<Owner>(entity)){_index.Remove(entity);continue;}
                Entity owner=EntityManager.GetComponentData<Owner>(entity).m_Owner;
                _index.Add(owner,entity); // Refresh if another system changed the owner.
                if(!_ownerStatus.TryGetValue(owner,out bool retire))
                {
                    bool exists=EntityManager.Exists(owner);
                    retire=GeneratedPaintOwnerPolicy.Retire(exists,exists&&EntityManager.HasComponent<Deleted>(owner),exists&&EntityManager.HasComponent<PrefabRef>(owner));
                    _ownerStatus[owner]=retire;
                }
                if(!retire)continue;
                GeneratedVisualLifecycle.Retire(World,entity);
                if(removed<8&&ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("paint.ownerRemoved","paint="+entity+" owner="+owner);
                removed++;
            }
            _ownerStatus.Clear();
            if (removed > 0) Mod.Log.Info("Retired " + removed + " generated paint lanes before ground-height update: road/node owner removed or unavailable.");
        }
    }
}
