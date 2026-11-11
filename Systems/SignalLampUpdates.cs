using System.Collections.Generic;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private uint lampRoadVersion;
        private readonly Dictionary<Entity,bool> lampRoadStates = new Dictionary<Entity,bool>();
        private readonly Dictionary<Entity,HashSet<Entity>> lampRoadOwners = new Dictionary<Entity,HashSet<Entity>>();
        private readonly Dictionary<Entity,SignalLayoutEntry> lampNetworks = new Dictionary<Entity,SignalLayoutEntry>();
        private readonly HashSet<Entity> lampStateNodes = new HashSet<Entity>();
        private readonly List<Entity> staleLampRoads = new List<Entity>();
        private long lampRoadChecks, lampStateChanges;
        private void WatchLampRoads(Entity node)
        {
            if(!signalLayouts.TryGetValue(node,out var network))return;
            if(lampNetworks.TryGetValue(node,out var before)&&ReferenceEquals(before,network))return;
            UnwatchLampRoads(node);lampNetworks[node]=network;
            foreach(var input in network.Inputs)
            {
                if(!lampRoadOwners.TryGetValue(input.Road,out var owners))lampRoadOwners.Add(input.Road,owners=new HashSet<Entity>());
                owners.Add(node);
            }
        }
        private void UnwatchLampRoads(Entity node)
        {
            if(!lampNetworks.TryGetValue(node,out var before))return;
            foreach(var input in before.Inputs)if(lampRoadOwners.TryGetValue(input.Road,out var owners))
            {owners.Remove(node);if(owners.Count==0){lampRoadOwners.Remove(input.Road);lampRoadStates.Remove(input.Road);}}
            lampNetworks.Remove(node);
        }
        private void SynchronizeChangedLampStates()
        {
            if(UnityEngine.Time.realtimeSinceStartup<_nextLampState)return;
            _nextLampState=UnityEngine.Time.realtimeSinceStartup+1f;
            uint version=EntityManager.GlobalSystemVersion;
            var roadType=GetComponentTypeHandle<Road>(true);
            lampStateNodes.Clear();staleLampRoads.Clear();
            // Only roads serving our lamps, never all city roads or lamps.
            // Version notifications are coarse; actual LightsOff values
            // decide whether any node's lamp state needs an update.
            foreach(var watched in lampRoadOwners)
            {
                var road=watched.Key;lampRoadChecks++;
                if(!Live(road)||!EntityManager.HasComponent<Road>(road)){staleLampRoads.Add(road);continue;}
                if(lampRoadStates.ContainsKey(road)&&!EntityManager.GetChunk(road).DidChange(ref roadType,lampRoadVersion))continue;
                bool off=(EntityManager.GetComponentData<Road>(road).m_Flags&RoadFlags.LightsOff)!=0;
                if(lampRoadStates.TryGetValue(road,out bool before)&&before==off)continue;
                lampRoadStates[road]=off;lampStateChanges++;
                foreach(var node in watched.Value)if(EnabledNode(node))lampStateNodes.Add(node);
            }
            foreach(var road in staleLampRoads){lampRoadOwners.Remove(road);lampRoadStates.Remove(road);}
            lampRoadVersion=version;
            foreach(var node in lampStateNodes)
            {
                maintenance.CopyNode(node,maintenanceWork);
                foreach(var entry in maintenanceWork)
                {
                    if((entry.Kinds&SignalMaintenanceIndex<Entity>.Lamp)!=0)SynchronizeLampState(entry.Actor,node,true);
                    if((entry.Kinds&SignalMaintenanceIndex<Entity>.GeneratedLamp)!=0)SynchronizeLampState(entry.Actor,node,false);
                }
            }
        }
        private void ClearLampTracking(){lampRoadStates.Clear();lampRoadOwners.Clear();lampNetworks.Clear();lampStateNodes.Clear();lampRoadVersion=0;_nextLampState=0;}
    }
}
