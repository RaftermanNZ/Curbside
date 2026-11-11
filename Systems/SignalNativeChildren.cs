using System.Collections.Generic;
using Game.Common;
using Unity.Entities;
using ObjectChild=Game.Objects.SubObject;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private readonly SignalChildEventIndex<Entity> nativeSignalChildren = new SignalChildEventIndex<Entity>();
        private readonly HashSet<Entity> signalChildSeen = new HashSet<Entity>();
        private readonly List<Entity> signalTicketChildren = new List<Entity>();
        private readonly List<Entity> signalCoverageChildren = new List<Entity>();
        private readonly List<Entity> signalDuplicateChildren = new List<Entity>();
        private readonly List<Entity> signalEdgeChildren = new List<Entity>();
        private readonly List<Entity> signalRecoveryChildren = new List<Entity>();
        private void CopySignalChildren(Entity owner,List<Entity> output)
        {
            output.Clear();signalChildSeen.Clear();
            if(EntityManager.HasBuffer<ObjectChild>(owner))foreach(var child in EntityManager.GetBuffer<ObjectChild>(owner,true))
                AddSignalChild(owner,child.m_SubObject,output);
            foreach(var actor in nativeSignalChildren.Pending(owner))AddSignalChild(owner,actor,output);
        }
        private void AddSignalChild(Entity owner,Entity actor,List<Entity> output)
        {
            if(!Live(actor)||!EntityManager.HasComponent<Owner>(actor)||EntityManager.GetComponentData<Owner>(actor).m_Owner!=owner||!signalChildSeen.Add(actor))return;
            output.Add(actor);
        }
        private bool HasSignalChildren(Entity owner)=>EntityManager.HasBuffer<ObjectChild>(owner)||System.Linq.Enumerable.Any(nativeSignalChildren.Pending(owner));
    }
}
