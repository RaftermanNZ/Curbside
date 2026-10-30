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

        protected override void OnCreate()
        {
            base.OnCreate();
            _paint = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Lane>() },
                Any = new[] { ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>() }
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
            RequireForUpdate(_paint);
            RequireForUpdate(_networkChanges);
        }

        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            Dependency.Complete();
            _ownerStatus.Clear();
            int removed = 0;
            // Capture both arrays before any Retire call changes archetypes.
            using (var entities = _paint.ToEntityArray(Allocator.Temp))
            using (var owners = _paint.ToComponentDataArray<Owner>(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity owner = owners[i].m_Owner;
                    if (!_ownerStatus.TryGetValue(owner, out bool retire))
                    {
                        bool exists = EntityManager.Exists(owner);
                        retire = GeneratedPaintOwnerPolicy.Retire(exists,
                            exists && EntityManager.HasComponent<Deleted>(owner),
                            exists && EntityManager.HasComponent<PrefabRef>(owner));
                        _ownerStatus[owner] = retire;
                    }
                    if (!retire) continue;
                    // Retire synchronizes native search readers, removes this
                    // entity from their tree and marks it Deleted for rendering.
                    GeneratedVisualLifecycle.Retire(World, entities[i]);
                    if (removed < 8 && ModDiagnostics.TraceBuffer.Enabled)
                        ModDiagnostics.TraceBuffer.Record("paint.ownerRemoved", "paint=" + entities[i] + " owner=" + owner);
                    removed++;
                }
            _ownerStatus.Clear();
            if (removed > 0) Mod.Log.Info("Retired " + removed + " generated paint lanes before ground-height update: road/node owner removed or unavailable.");
        }
    }
}
