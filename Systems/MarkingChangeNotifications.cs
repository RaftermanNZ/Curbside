using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using CarLane = Game.Net.CarLane;
using TrackLane = Game.Net.TrackLane;
using ParkingLane = Game.Net.ParkingLane;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    // Runs only inside the existing Modification5 marking callback. No new
    // system, timer, polling loop, native tag or retained ECS view is introduced.
    internal sealed class MarkingChangeNotifications
    {
        internal static readonly bool Supported =
            typeof(Road).Module.ModuleVersionId == new Guid("46198354-d4f9-4d23-bf32-7e3c085d8b92")
            && typeof(EntityQuery).Module.ModuleVersionId == new Guid("bfb47e8c-9b6e-4acf-8c6d-c80a7bff1c41");
        private static readonly ComponentType[] GeometryFilter={ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>()};
        private static readonly ComponentType[] TopologyFilter={ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<ConnectedEdge>()};
        private readonly EntityQuery geometry, topology, owners, inputs, events;
        private readonly Dictionary<Entity,Entity> previousOwners = new Dictionary<Entity,Entity>();
        private readonly Dictionary<Entity,bool> ownerStatus = new Dictionary<Entity,bool>();
        private readonly List<Entity> unchangedRoads = new List<Entity>();
        private bool seeded;
        private readonly PaintInputReadScope ownerReads = new PaintInputReadScope();

        internal MarkingChangeNotifications(Func<EntityQueryDesc[],EntityQuery> query)
        {
            var none = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() };
            geometry=query(new[]{new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>()},None=none}});
            topology=query(new[]{new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<ConnectedEdge>()},None=none}});
            var lifecycle=new[]{ComponentType.ReadOnly<Created>(),ComponentType.ReadOnly<Updated>(),ComponentType.ReadOnly<Deleted>()};
            owners=query(new[]{
                new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>()},Any=lifecycle,None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}},
                new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Node>()},Any=lifecycle,None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}}});
            var inputDescriptions=InputDescriptions(null);
            inputs=query(inputDescriptions);
            events=query(InputDescriptions(lifecycle));
        }

        internal static EntityQueryDesc[] InputDescriptions(ComponentType[] lifecycle)
        {
            // EntityQueryDesc defaults are empty arrays. Passing null overrides
            // that valid default; the installed builder dereferences Any.Length.
            lifecycle = lifecycle ?? Array.Empty<ComponentType>();
            var descriptions=new List<EntityQueryDesc>();
            var generated=new[]{ComponentType.ReadOnly<Game.Tools.Temp>(),ComponentType.ReadOnly<MGRCurbMarking>(),ComponentType.ReadOnly<MGRIntersectionPaint>(),
                ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.ReadOnly<MGRSignalLuminaireRuntime>(),ComponentType.ReadOnly<MGRPendingVisualRegistration>(),ComponentType.ReadOnly<MGRTemporaryPreviewPaint>()};
            foreach(var role in new[]{ComponentType.ReadOnly<LaneGeometry>(),ComponentType.ReadOnly<CarLane>(),ComponentType.ReadOnly<TrackLane>(),ComponentType.ReadOnly<ParkingLane>(),ComponentType.ReadOnly<MGRReplacedPaint>()})
                descriptions.Add(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Owner>(),role},Any=lifecycle,None=generated});
            descriptions.Add(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<NodeLane>(),ComponentType.ReadOnly<PedestrianLane>()},Any=lifecycle,None=generated});
            // Untagged/late paint producers remain candidates. Ordinary path
            // roles are excluded before enumeration; geometry hybrids above are
            // still admitted. No Lane tag is required for this compatibility arm.
            var fallbackNone=new List<ComponentType>(generated){ComponentType.ReadOnly<CarLane>(),ComponentType.ReadOnly<TrackLane>(),ComponentType.ReadOnly<ParkingLane>(),ComponentType.ReadOnly<PedestrianLane>(),
                ComponentType.ReadOnly<Game.Net.ConnectionLane>(),ComponentType.ReadOnly<Game.Net.UtilityLane>()};
            descriptions.Add(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<Curve>(),ComponentType.ReadOnly<PrefabRef>()},Any=lifecycle,None=fallbackNone.ToArray()});
            return descriptions.ToArray();
        }

        private bool AddOwner(EntityManager em,Entity owner,HashSet<Entity> roads,List<Entity> nodes)
        {
            if(ownerStatus.TryGetValue(owner,out bool known))return known;
            bool relevant=false;
            if(em.Exists(owner)&&!em.HasComponent<Game.Tools.Temp>(owner))
            {
                if(em.HasComponent<Road>(owner)&&em.HasComponent<Edge>(owner)){roads.Add(owner);relevant=true;}
                else if(em.HasComponent<Node>(owner)&&!em.HasComponent<Deleted>(owner)
                    &&em.HasBuffer<ConnectedEdge>(owner)){nodes.Add(owner);relevant=true;}
            }
            ownerStatus[owner]=relevant;return relevant;
        }

        internal void Collect(EntityManager em,HashSet<Entity> roads,List<Entity> nodes)
        {
            bool phaseStarted = false;
            Collect(em, roads, nodes, null, null, ref phaseStarted);
        }

        internal void Collect(EntityManager em,HashSet<Entity> roads,List<Entity> nodes,
            PaintOwnerNotifications proof, ExternalPaintOwnership external, ref bool phaseStarted)
        {
            try { CollectOwners(em, roads, nodes, proof, external, ref phaseStarted); }
            finally { ownerReads.End(); }
        }

        private void CollectOwners(EntityManager em,HashSet<Entity> roads,List<Entity> nodes,
            PaintOwnerNotifications proof, ExternalPaintOwnership external, ref bool phaseStarted)
        {
            ownerStatus.Clear();
            // Structural lifecycle queries need no change-version synchronization
            // on Owner, Curve, parking state or the shared SubLane buffer.
            if (!owners.IsEmptyIgnoreFilter)
                using (var changed = owners.ToEntityArray(Allocator.Temp))
                    foreach (var owner in changed) AddOwner(em, owner, roads, nodes);
            var selected=seeded?events:inputs;
            if (!selected.IsEmptyIgnoreFilter)
            using(var changed=selected.ToEntityArray(Allocator.Temp))
            {
                if (changed.Length > 1 && PaintInputReadScope.Supported) ownerReads.Begin();
                foreach(var lane in changed)
                {
                    if(!em.Exists(lane))continue;
                    var owner=ownerReads.Component<Owner>(em,lane).m_Owner;
                    if(previousOwners.TryGetValue(lane,out var oldOwner)&&oldOwner!=owner)AddOwner(em,oldOwner,roads,nodes);
                    bool relevant=AddOwner(em,owner,roads,nodes);
                    if(em.HasComponent<Deleted>(lane))previousOwners.Remove(lane);
                    else if(relevant)previousOwners[lane]=owner;
                    else previousOwners.Remove(lane);
                }
            }
            seeded=true;
            // No relevant committed event means no geometry/topology filter wait.
            if(roads.Count==0&&nodes.Count==0)return;
            // These collections never filter SubLane or receive the system-wide
            // Dependency (which includes shared access-lane writers). Only
            // actual geometry/topology filters may join their own producer types.
            try
            {
                geometry.SetChangedVersionFilter(GeometryFilter);
                if (proof != null && proof.UsesTopologyChunks(em, external))
                {
                    using(var changed=geometry.ToArchetypeChunkArray(Allocator.Temp))
                    {
                        int count = 0; foreach(var chunk in changed) count += chunk.Count;
                        var entityType = em.GetEntityTypeHandle();
                        foreach(var chunk in changed)
                        {
                            if (proof.UnchangedGeometryChunk(em, external, chunk, count, true, ref phaseStarted)) continue;
                            foreach(var owner in chunk.GetNativeArray(entityType)) AddCommittedOwner(em, owner, true, roads, nodes);
                        }
                    }
                }
                else using(var changed=geometry.ToEntityArray(Allocator.Temp))
                    foreach(var owner in changed) AddCommittedOwner(em, owner, true, roads, nodes);
            }
            finally{geometry.ResetFilter();}
            try
            {
                topology.SetChangedVersionFilter(TopologyFilter);
                if (proof != null && proof.UsesTopologyChunks(em, external))
                {
                    using(var changed=topology.ToArchetypeChunkArray(Allocator.Temp))
                    {
                        int count = 0; foreach(var chunk in changed) count += chunk.Count;
                        var entityType = em.GetEntityTypeHandle();
                        foreach(var chunk in changed)
                        {
                            if (proof.UnchangedTopologyChunk(em, external, chunk, count, ref phaseStarted)) continue;
                            var entities = chunk.GetNativeArray(entityType);
                            foreach(var owner in entities) AddCommittedOwner(em, owner, false, roads, nodes);
                        }
                    }
                }
                else using(var changed=topology.ToEntityArray(Allocator.Temp))
                    foreach(var owner in changed) AddCommittedOwner(em, owner, false, roads, nodes);
            }
            finally{topology.ResetFilter();}

            // Native lifecycle tags are also candidate notifications: reused
            // lanes and their owners can receive Updated with equal paint inputs.
            // Keep the direct event as the discovery trigger above, including
            // old/new owner bookkeeping, before suppressing its proven no-ops.
            // Missing, changed, unsupported and busy inputs remain admitted.
            if (proof != null)
            {
                // Collect native candidates first, deduplicating lifecycle,
                // geometry and topology notifications before any paint proof.
                // This snapshot contains committed roads/nodes only. Temporary
                // previews remain owned by the existing preview systems.
                proof.PrepareInputCohort(roads.Count + nodes.Count, ref phaseStarted);
                RemoveUnchangedCandidates(em, roads, nodes, proof, external, ref phaseStarted);
            }
        }

        private void RemoveUnchangedCandidates(EntityManager em, HashSet<Entity> roads,
            List<Entity> nodes, PaintOwnerNotifications proof, ExternalPaintOwnership external,
            ref bool phaseStarted)
        {
            unchangedRoads.Clear();
            foreach (var road in roads)
                if (proof.UnchangedForCollectedInput(em, external, road, true, ref phaseStarted))
                    unchangedRoads.Add(road);
            foreach (var road in unchangedRoads) roads.Remove(road);
            unchangedRoads.Clear();
            int retained = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!proof.UnchangedForCollectedInput(em, external, node, false, ref phaseStarted))
                    nodes[retained++] = node;
            }
            if (retained != nodes.Count) nodes.RemoveRange(retained, nodes.Count - retained);
        }

        private void AddCommittedOwner(EntityManager em, Entity owner, bool roadContext,
            HashSet<Entity> roads, List<Entity> nodes)
        {
            // Both coarse queries exclude Deleted/Temp. No mutation occurs
            // while collecting their snapshots. The topology query can still
            // contain a foreign hybrid with both node and road roles; preserve
            // AddOwner's road precedence for that case.
            if (ownerStatus.ContainsKey(owner)) return;
            ownerStatus.Add(owner, true);
            if (!roadContext) roadContext = em.HasComponent<Road>(owner) && em.HasComponent<Edge>(owner);
            if (roadContext) roads.Add(owner);
            else nodes.Add(owner);
        }

        internal void Clear(){ownerReads.End();previousOwners.Clear();ownerStatus.Clear();unchangedRoads.Clear();seeded=false;}
    }

    public partial class IntersectionPaintSystem
    {
        private static readonly HashSet<Entity> markingRoadChanges=new HashSet<Entity>(),markingOwnerChanges=new HashSet<Entity>();
        internal static void RequestMarkingRoad(Entity road){if(road!=Entity.Null)markingRoadChanges.Add(road);}
        internal static void RequestMarkingOwner(Entity owner){if(owner!=Entity.Null)markingOwnerChanges.Add(owner);}
    }
}
