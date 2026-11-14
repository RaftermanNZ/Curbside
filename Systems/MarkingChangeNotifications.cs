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
        private bool seeded;

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
            ownerStatus.Clear();
            // Structural lifecycle queries need no change-version synchronization
            // on Owner, Curve, parking state or the shared SubLane buffer.
            using(var changed=owners.ToEntityArray(Allocator.Temp))foreach(var owner in changed)AddOwner(em,owner,roads,nodes);
            var selected=seeded?events:inputs;
            using(var changed=selected.ToEntityArray(Allocator.Temp))foreach(var lane in changed)
            {
                if(!em.Exists(lane))continue;
                var owner=em.GetComponentData<Owner>(lane).m_Owner;
                if(previousOwners.TryGetValue(lane,out var oldOwner)&&oldOwner!=owner)AddOwner(em,oldOwner,roads,nodes);
                bool relevant=AddOwner(em,owner,roads,nodes);
                if(em.HasComponent<Deleted>(lane))previousOwners.Remove(lane);
                else if(relevant)previousOwners[lane]=owner;
                else previousOwners.Remove(lane);
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
                using(var changed=geometry.ToEntityArray(Allocator.Temp))foreach(var owner in changed)AddOwner(em,owner,roads,nodes);
            }
            finally{geometry.ResetFilter();}
            try
            {
                topology.SetChangedVersionFilter(TopologyFilter);
                using(var changed=topology.ToEntityArray(Allocator.Temp))foreach(var owner in changed)AddOwner(em,owner,roads,nodes);
            }
            finally{topology.ResetFilter();}

        }

        internal void Clear(){previousOwners.Clear();seeded=false;}
    }

    public partial class IntersectionPaintSystem
    {
        private static readonly HashSet<Entity> markingRoadChanges=new HashSet<Entity>(),markingOwnerChanges=new HashSet<Entity>();
        internal static void RequestMarkingRoad(Entity road){if(road!=Entity.Null)markingRoadChanges.Add(road);}
        internal static void RequestMarkingOwner(Entity owner){if(owner!=Entity.Null)markingOwnerChanges.Add(owner);}
    }
}
