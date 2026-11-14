using System;
using System.Collections.Generic;
using Game.Net;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // The native owner and both road ends matter. A changed node can also
    // rebuild its directly connected edges. Do not traverse the entire network.
    internal sealed class PreviewRefreshScope
    {
        internal readonly HashSet<Entity> Owners = new HashSet<Entity>();
        internal bool Complete { get; private set; } = true;
        private readonly HashSet<Entity> identityPath = new HashSet<Entity>();
        private readonly HashSet<Entity> edgePath = new HashSet<Entity>();
        private readonly HashSet<Entity> endpointPath = new HashSet<Entity>();
        private readonly HashSet<Entity> expanded = new HashSet<Entity>();
        private readonly HashSet<Entity> objectRequests = new HashSet<Entity>();
        private readonly HashSet<Entity> networkRequests = new HashSet<Entity>();
        private readonly HashSet<Entity> edgeRequests = new HashSet<Entity>();
        private readonly HashSet<Entity> endpointRequests = new HashSet<Entity>();
        private readonly List<PreviewOwnerIndex> interests = new List<PreviewOwnerIndex>(4);

        internal void Clear()
        {
            Owners.Clear(); identityPath.Clear(); edgePath.Clear(); endpointPath.Clear(); expanded.Clear();
            objectRequests.Clear(); networkRequests.Clear(); edgeRequests.Clear(); endpointRequests.Clear();
            interests.Clear(); Complete = true;
        }

        internal void Include(PreviewOwnerIndex index) { interests.Add(index); }
        private readonly PreviewNativeLists nativeLists = new PreviewNativeLists();
        private readonly List<PreviewOwnerIndex> previousInterests = new List<PreviewOwnerIndex>(4);
        private readonly List<ulong> previousRevisions = new List<ulong>(4);
        private ulong selectionEpoch;
        private readonly Action<Entity> includeChild;
        private readonly Func<Entity, bool> selectChild;

        internal PreviewRefreshScope() { includeChild = AddOwner; selectChild = Interested; }

        internal void BeginBatch(EntityManager manager, EntityQuery objects, EntityQuery lanes,
            bool supported, int frame)
        {
            bool changed = interests.Count != previousInterests.Count;
            for (int i = 0; !changed && i < interests.Count; i++)
                changed = !ReferenceEquals(interests[i], previousInterests[i]) || interests[i].Revision != previousRevisions[i];
            if (changed)
            {
                unchecked { selectionEpoch++; }
                previousInterests.Clear(); previousRevisions.Clear();
                foreach (var index in interests) { previousInterests.Add(index); previousRevisions.Add(index.Revision); }
            }
            nativeLists.BeginBatch(manager, objects, lanes, supported, frame, selectionEpoch, selectChild);
        }

        internal void EndBatch() { nativeLists.EndBatch(); }

        internal void ResetHistory()
        {
            EndBatch(); nativeLists.Reset(); previousInterests.Clear(); previousRevisions.Clear();
            unchecked { selectionEpoch++; }
        }

        private bool Interested(Entity entity)
        {
            if (interests.Count == 0) return true;
            foreach (var index in interests) if (index.MaySelect(entity)) return true;
            return false;
        }


        private void AddOwner(Entity entity)
        {
            if (Interested(entity)) Owners.Add(entity);
        }

        internal void AddObjectOwner(EntityManager manager, Entity actor)
        {
            if (!objectRequests.Add(actor)) return;
            identityPath.Clear();
            for (int depth = 0; depth < 8; depth++)
            {
                if (!manager.Exists(actor) || !identityPath.Add(actor)) { Complete = false; return; }
                AddOwner(actor);
                if (manager.HasComponent<Node>(actor) || manager.HasComponent<Edge>(actor)
                    || !manager.HasComponent<Game.Common.Owner>(actor)) { Add(manager, actor); return; }
                actor = manager.GetComponentData<Game.Common.Owner>(actor).m_Owner;
            }
            Complete = false;
        }

        internal void Add(EntityManager manager, Entity entity)
        {
            if (!networkRequests.Add(entity)) return;
            identityPath.Clear();
            for (int depth = 0; depth < 8; depth++)
            {
                if (entity == Entity.Null) return; // A newly drawn road has no original.
                if (!identityPath.Add(entity) || !manager.Exists(entity)) { Complete = false; return; }
                AddOwner(entity);
                AddChildren(manager, entity);
                if (manager.HasComponent<Edge>(entity)) AddEdgeEnds(manager, entity);
                if (manager.HasComponent<Node>(entity) && manager.HasBuffer<ConnectedEdge>(entity))
                {
                    var edges = manager.GetBuffer<ConnectedEdge>(entity, true);
                    foreach (var edge in edges) AddEdge(manager, edge.m_Edge);
                }
                if (!manager.HasComponent<Temp>(entity)) return;
                entity = manager.GetComponentData<Temp>(entity).m_Original;
            }
            Complete = false;
        }

        private void AddEdge(EntityManager manager, Entity edge)
        {
            if (!edgeRequests.Add(edge)) return;
            // This local path is independent of Add's path (an edge may refer
            // back to its changed endpoint). No connected-edge recursion.
            edgePath.Clear();
            for (int depth = 0; depth < 8; depth++)
            {
                if (edge == Entity.Null) return;
                if (!edgePath.Add(edge) || !manager.Exists(edge)) { Complete = false; return; }
                AddOwner(edge);
                AddChildren(manager, edge);
                if (manager.HasComponent<Edge>(edge)) AddEdgeEnds(manager, edge);
                if (!manager.HasComponent<Temp>(edge)) return;
                edge = manager.GetComponentData<Temp>(edge).m_Original;
            }
            Complete = false;
        }

        private void AddEdgeEnds(EntityManager manager, Entity entity)
        {
            var edge = manager.GetComponentData<Edge>(entity);
            AddIdentity(manager, edge.m_Start); AddIdentity(manager, edge.m_End);
        }

        private void AddIdentity(EntityManager manager, Entity entity)
        {
            if (!endpointRequests.Add(entity)) return;
            endpointPath.Clear();
            for (int depth = 0; depth < 8; depth++)
            {
                if (entity == Entity.Null) return;
                if (!endpointPath.Add(entity) || !manager.Exists(entity)) { Complete = false; return; }
                AddOwner(entity);
                AddChildren(manager, entity);
                if (!manager.HasComponent<Temp>(entity)) return;
                entity = manager.GetComponentData<Temp>(entity).m_Original;
            }
            Complete = false;
        }

        private void AddChildren(EntityManager manager, Entity owner)
        {
            if (!expanded.Add(owner)) return;
            if (manager.HasBuffer<Game.Objects.SubObject>(owner))
                if (!nativeLists.Objects(manager, owner, !manager.HasComponent<Temp>(owner), includeChild, out var objects))
                    foreach (var item in objects) AddOwner(item.m_SubObject);
            if (manager.HasBuffer<SubLane>(owner))
                if (!nativeLists.Lanes(manager, owner, !manager.HasComponent<Temp>(owner), includeChild, out var lanes))
                    foreach (var item in lanes) AddOwner(item.m_SubLane);
        }
    }
}
