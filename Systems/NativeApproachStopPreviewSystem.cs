using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Serialization;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using CarLane = Game.Net.CarLane;
using SubLane = Game.Net.SubLane;

namespace MedianGrassReplacement.Systems
{
    // Native secondary generation reads CarLane priority to create stop/yield
    // signs and their markings. Per-approach choices are saved on the original
    // node, unlike vanilla AllWayStop composition flags copied by native tools.
    // Lend only that priority to temporary lanes during the two native readers.
    public partial class NativeApproachStopPreviewSystem : GameSystemBase, IPreSerialize
    {
        private struct Claim
        {
            internal Entity Actor, Owner, Original;
            internal CarLaneFlags Before, Applied;
        }
        private readonly List<Claim> claims = new List<Claim>();
        private EntityQuery changedNodes;
        private ToolSystem tools;

        protected override void OnCreate()
        {
            base.OnCreate();
            tools = World.GetOrCreateSystemManaged<ToolSystem>();
            changedNodes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Node>(),
                    ComponentType.ReadOnly<SubLane>(), ComponentType.ReadOnly<ConnectedEdge>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate() => Prepare();

        internal void Prepare()
        {
            Restore();
            if (Mod.Saving || !NativeSignalProjectionSystem.Supported || Mod.Settings?.CurbsidePreviews != true
                || !NativeSignalProjectionPolicy.AllowsTool(tools.activeTool?.toolID)
                || changedNodes.IsEmptyIgnoreFilter) return;
            try
            {
                using (var nodes = changedNodes.ToEntityArray(Allocator.Temp))
                    foreach (var node in nodes) PrepareNode(node);
            }
            catch { Restore(); throw; }
        }

        private void PrepareNode(Entity node)
        {
            var temp = EntityManager.GetComponentData<Temp>(node);
            if (!NativeSignalProjectionPolicy.Eligible(temp.m_Flags)
                || EntityManager.HasComponent<TrafficLights>(node)
                || EntityManager.HasComponent<Upgraded>(node)
                    && (EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General & CompositionFlags.General.AllWayStop) != 0) return;
            var original = NativePreviewOrigin.Resolve(EntityManager, node, true);
            if (!EntityManager.HasComponent<Node>(original) || !EntityManager.HasBuffer<MGRStopApproach>(original)) return;
            foreach (var child in EntityManager.GetBuffer<SubLane>(node, true))
            {
                var actor = child.m_SubLane;
                if (!EntityManager.HasComponent<Temp>(actor) || EntityManager.HasComponent<Deleted>(actor)
                    || !EntityManager.HasComponent<CarLane>(actor) || !EntityManager.HasComponent<Lane>(actor)
                    || !EntityManager.HasComponent<Owner>(actor)
                    || EntityManager.GetComponentData<Owner>(actor).m_Owner != node) continue;
                var laneTemp = EntityManager.GetComponentData<Temp>(actor);
                if (!NativeSignalProjectionPolicy.Eligible(laneTemp.m_Flags)) continue;
                int incoming = EntityManager.GetComponentData<Lane>(actor).m_StartNode.GetOwnerIndex();
                bool matched = false; byte mode = 0;
                Entity sourceRoad = Entity.Null;
                foreach (var road in EntityManager.GetBuffer<ConnectedEdge>(node, true))
                {
                    if (road.m_Edge.Index != incoming) continue;
                    sourceRoad = NativePreviewOrigin.Resolve(EntityManager, road.m_Edge, true);
                    break;
                }
                // Native EdgeIterator also uses unchanged roads on the original
                // node. These need not appear in the preview ConnectedEdge list.
                if (sourceRoad == Entity.Null && EntityManager.HasBuffer<ConnectedEdge>(original))
                    foreach (var road in EntityManager.GetBuffer<ConnectedEdge>(original, true))
                    {
                        if (road.m_Edge.Index != incoming) continue;
                        sourceRoad = NativePreviewOrigin.Resolve(EntityManager, road.m_Edge, true);
                        break;
                    }
                if (!EntityManager.HasComponent<Edge>(sourceRoad)) continue;
                var edge = EntityManager.GetComponentData<Edge>(sourceRoad);
                if ((edge.m_Start == original) == (edge.m_End == original)) continue;
                // A split preview may move/rotate while retaining this saved
                // approach. Match intent in the original road/node frame.
                var direction = ApproachStopSystem.Away(EntityManager, sourceRoad, original);
                foreach (var choice in EntityManager.GetBuffer<MGRStopApproach>(original, true))
                {
                    if (!(Unity.Mathematics.math.dot(Unity.Mathematics.math.normalizesafe(choice.Outward.xz),
                        Unity.Mathematics.math.normalizesafe(direction.xz)) > .85f)) continue;
                    if (matched && mode != choice.Stop) { matched = false; break; }
                    matched = true; mode = choice.Stop;
                }
                if (!matched || mode > 2) continue;
                var lane = EntityManager.GetComponentData<CarLane>(actor);
                var next = ApproachStopPolicy.ApplyMode(lane.m_Flags, mode);
                if (next == lane.m_Flags) continue;
                claims.Add(new Claim { Actor = actor, Owner = node, Original = laneTemp.m_Original,
                    Before = lane.m_Flags & ApproachStopPolicy.Mask, Applied = next & ApproachStopPolicy.Mask });
                lane.m_Flags = next;
                EntityManager.SetComponentData(actor, lane);
            }
        }

        internal void Restore()
        {
            foreach (var claim in claims)
            {
                if (!EntityManager.Exists(claim.Actor) || !EntityManager.HasComponent<Temp>(claim.Actor)
                    || !EntityManager.HasComponent<CarLane>(claim.Actor) || !EntityManager.HasComponent<Owner>(claim.Actor)
                    || EntityManager.GetComponentData<Owner>(claim.Actor).m_Owner != claim.Owner
                    || EntityManager.GetComponentData<Temp>(claim.Actor).m_Original != claim.Original) continue;
                var lane = EntityManager.GetComponentData<CarLane>(claim.Actor);
                if (!ApproachStopPolicy.OwnsPriority(lane.m_Flags, claim.Applied)) continue;
                lane.m_Flags = (lane.m_Flags & ~ApproachStopPolicy.Mask) | claim.Before;
                // EntityManager's write dependency completes the native readers
                // before restoring; their command buffers retain generated signs.
                EntityManager.SetComponentData(claim.Actor, lane);
            }
            claims.Clear();
        }

        public void PreSerialize(Context context) => Restore();
        protected override void OnDestroy() { Restore(); base.OnDestroy(); }
    }

    public partial class NativeApproachStopPreviewRestoreSystem : GameSystemBase
    {
        protected override void OnUpdate()
            => World.GetExistingSystemManaged<NativeApproachStopPreviewSystem>()?.Restore();
    }
}
