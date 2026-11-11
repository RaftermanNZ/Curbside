using System;
using System.Collections.Generic;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private sealed class LayoutRoadInput
        {
            internal Entity Road; internal Edge Ends; internal EdgeGeometry EdgeGeometry;
            internal EdgeNodeGeometry NodeGeometry; internal Composition Composition;
            internal NetCompositionData EdgeData,NodeData;
            internal NetCompositionLane[] Lanes,NodeLanes;
            internal NetCompositionPiece[] Pieces;
        }
        private sealed class SignalLayoutEntry
        {
            internal float3 Centre; internal LayoutRoadInput[] Inputs;
            internal List<FarSideLightPolicy.Approach> Roads; internal List<float3> Surface;
            internal WireCornerPlan Wire,Arms;
            internal int WireAttemptStyle=-1;
            internal int ArmAttemptStyle=-1;
            internal bool ArmFailure;
            internal string WireFailure;
            internal bool HasCrosswalk;
        }
        private sealed class WireCornerPlan
        {
            internal byte Style; internal float3[] Supports; internal int2[] Pairs;
            internal Dictionary<Entity,int> RoadSlots=new Dictionary<Entity,int>();
        }
        private readonly Dictionary<Entity,SignalLayoutEntry> signalLayouts=new Dictionary<Entity,SignalLayoutEntry>();
        private bool deferSignalApplication;
        private long networkGeometryReads,networkGeometryHits,wirePlansBuilt,wirePlansReused;
        private string lastLayoutResult="not requested";
        private static T[] CopyBuffer<T>(EntityManager manager,Entity owner) where T:unmanaged,IBufferElementData
        {
            if(!manager.HasBuffer<T>(owner))return null;var source=manager.GetBuffer<T>(owner,true);var result=new T[source.Length];
            for(int i=0;i<source.Length;i++)result[i]=source[i];return result;
        }
        private bool SameBuffer<T>(Entity owner,T[] previous) where T:unmanaged,IBufferElementData
        {
            if(!EntityManager.HasBuffer<T>(owner))return previous==null;
            var current=EntityManager.GetBuffer<T>(owner,true);if(previous==null||current.Length!=previous.Length)return false;
            for(int i=0;i<current.Length;i++)if(!EqualityComparer<T>.Default.Equals(current[i],previous[i]))return false;return true;
        }
        // Change filters are notifications, not proof of changed geometry.
        // Compare the owning network values only on existing node work. The
        // cache contains copies, never ECS buffers, lookups or job handles.
        private bool SameNetwork(Entity node,SignalLayoutEntry entry)
        {
            if(!Live(node)||!EntityManager.HasComponent<Node>(node)||!EntityManager.HasBuffer<ConnectedEdge>(node)||
                !EntityManager.GetComponentData<Node>(node).m_Position.Equals(entry.Centre))return false;
            // A newly added native crossing changes the required attachment
            // footprint. Do not treat a temporarily empty native lane buffer
            // during regeneration as removal of an existing crossing.
            if(!entry.HasCrosswalk&&HasNativeCrosswalk(node))return false;
            var edges=EntityManager.GetBuffer<ConnectedEdge>(node,true);if(edges.Length!=entry.Inputs.Length)return false;
            foreach(var input in entry.Inputs)
            {
                bool found=false;foreach(var edge in edges)if(edge.m_Edge==input.Road){found=true;break;}if(!found||!Live(input.Road))return false;
                if(!EntityManager.HasComponent<Edge>(input.Road)||!EntityManager.HasComponent<EdgeGeometry>(input.Road)||!EntityManager.HasComponent<Composition>(input.Road))return false;
                var ends=EntityManager.GetComponentData<Edge>(input.Road);var comp=EntityManager.GetComponentData<Composition>(input.Road);
                if(!ends.Equals(input.Ends)||!comp.Equals(input.Composition)||!EntityManager.GetComponentData<EdgeGeometry>(input.Road).Equals(input.EdgeGeometry))return false;
                bool start=ends.m_Start==node;var native=start?comp.m_StartNode:comp.m_EndNode;
                if(!EntityManager.HasComponent<NetCompositionData>(comp.m_Edge)||!EntityManager.HasComponent<NetCompositionData>(native)||
                    !EntityManager.GetComponentData<NetCompositionData>(comp.m_Edge).Equals(input.EdgeData)||!EntityManager.GetComponentData<NetCompositionData>(native).Equals(input.NodeData))return false;
                if(start?!EntityManager.HasComponent<StartNodeGeometry>(input.Road):!EntityManager.HasComponent<EndNodeGeometry>(input.Road))return false;
                var geometry=start?EntityManager.GetComponentData<StartNodeGeometry>(input.Road).m_Geometry:EntityManager.GetComponentData<EndNodeGeometry>(input.Road).m_Geometry;
                if(!geometry.Equals(input.NodeGeometry)||!SameBuffer(comp.m_Edge,input.Lanes)||!SameBuffer(native,input.NodeLanes)||!SameBuffer(native,input.Pieces))return false;
            }
            return true;
        }
        private SignalLayoutEntry CaptureNetwork(Entity node,List<FarSideLightPolicy.Approach> roads)
        {
            if(!EntityManager.HasBuffer<ConnectedEdge>(node)||EntityManager.GetBuffer<ConnectedEdge>(node,true).Length!=roads.Count)return null;
            var inputs=new List<LayoutRoadInput>();
            foreach(var road in roads)
            {
                var ends=EntityManager.GetComponentData<Edge>(road.Road);var comp=EntityManager.GetComponentData<Composition>(road.Road);bool start=ends.m_Start==node;var native=start?comp.m_StartNode:comp.m_EndNode;
                if(!EntityManager.HasComponent<NetCompositionData>(native)||(start?!EntityManager.HasComponent<StartNodeGeometry>(road.Road):!EntityManager.HasComponent<EndNodeGeometry>(road.Road)))return null;
                inputs.Add(new LayoutRoadInput{Road=road.Road,Ends=ends,Composition=comp,EdgeGeometry=EntityManager.GetComponentData<EdgeGeometry>(road.Road),
                    NodeGeometry=start?EntityManager.GetComponentData<StartNodeGeometry>(road.Road).m_Geometry:EntityManager.GetComponentData<EndNodeGeometry>(road.Road).m_Geometry,
                    EdgeData=EntityManager.GetComponentData<NetCompositionData>(comp.m_Edge),NodeData=EntityManager.GetComponentData<NetCompositionData>(native),
                    Lanes=CopyBuffer<NetCompositionLane>(EntityManager,comp.m_Edge),NodeLanes=CopyBuffer<NetCompositionLane>(EntityManager,native),Pieces=CopyBuffer<NetCompositionPiece>(EntityManager,native)});
            }
            return new SignalLayoutEntry{Centre=EntityManager.GetComponentData<Node>(node).m_Position,Inputs=inputs.ToArray(),Roads=new List<FarSideLightPolicy.Approach>(roads),HasCrosswalk=HasNativeCrosswalk(node)};
        }
        private List<FarSideLightPolicy.Approach> ReadApproaches(Entity node)
        {
            if(signalLayouts.TryGetValue(node,out var entry)&&SameNetwork(node,entry))
            {
                networkGeometryHits++;TrackLayoutNode(node);_approaches.Clear();
                foreach(var cached in entry.Roads){var road=cached;road.VehicleMask=0;road.LaneMasks=new List<ushort>(new ushort[road.Incoming.Count]);_approaches.Add(road);}
                BindApproachMasks(node);return _approaches;
            }
            networkGeometryReads++;var roads=ReadApproachesUncached(node);var snapshot=CaptureNetwork(node,roads);
            if(snapshot!=null){signalLayouts[node]=snapshot;TrackLayoutNode(node);}else signalLayouts.Remove(node);
            return roads;
        }
        private List<float3> ReadLayoutSurface(Entity node)
        {
            if(signalLayouts.TryGetValue(node,out var entry))return entry.Surface??(entry.Surface=new List<float3>(SurfacePoints(node)));
            return SurfacePoints(node);
        }
        private void ClearSignalLayouts(){signalLayouts.Clear();}
        private bool HasNativeCrosswalk(Entity node)
        {
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(node))return false;
            foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
                if(Live(item.m_SubLane)&&EntityManager.HasComponent<Game.Net.PedestrianLane>(item.m_SubLane)&&
                    (EntityManager.GetComponentData<Game.Net.PedestrianLane>(item.m_SubLane).m_Flags&PedestrianLaneFlags.Crosswalk)!=0)return true;
            return false;
        }
    }
}
