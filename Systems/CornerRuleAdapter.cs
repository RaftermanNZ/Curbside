using System;
using System.Collections.Generic;
using Curbside.CornerStudy;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    internal sealed class CornerSupportList : List<float3> { internal bool UsesCornerRule; }
    internal static partial class GardenCityLightPolicy
    {
        internal static bool PedestrianCornerEnvelope(byte style, out float radius)
        {
            radius=.4f;
            if(!modules.TryGetValue(style==4?"CSFPedHead":IsWireStyle(style)?"PedHeadWire":"PedHead",out var module) ||
                module.m_Meshes==null || module.m_Meshes.Length!=1 || !(module.m_Meshes[0].m_Mesh is RenderPrefab mesh))return false;
            var bounds=mesh.bounds;
            if(!math.all(math.isfinite(bounds.min))||!math.all(math.isfinite(bounds.max)))return false;
            foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.max.z})
                radius=math.max(radius,math.length(new float2(x,z+PedestrianOffset)));
            return true;
        }
    }
    public partial class FarSideLightSystem
    {
        private bool DrivingBounds(Entity composition,out float low,out float high)
        {
            low=high=0;if(!EntityManager.HasBuffer<NetCompositionPiece>(composition)||!EntityManager.HasComponent<NetCompositionData>(composition))return false;
            var lanes=new List<float2>();
            foreach(var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition,true))
            {
                if((piece.m_SectionFlags&(NetSectionFlags.Overhead|NetSectionFlags.Underground))!=0||!EntityManager.HasBuffer<NetPieceLane>(piece.m_Piece))continue;
                foreach(var lane in EntityManager.GetBuffer<NetPieceLane>(piece.m_Piece,true))
                {
                    if(!EntityManager.HasComponent<CarLaneData>(lane.m_Lane)||!EntityManager.HasComponent<NetLaneData>(lane.m_Lane))continue;
                    var data=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane);var flags=data.m_Flags|lane.m_ExtraFlags;
                    if((flags&(LaneFlags.Master|LaneFlags.Virtual|LaneFlags.Parking|LaneFlags.BicyclesOnly))!=0)continue;
                    float x=piece.m_Offset.x+((piece.m_SectionFlags&NetSectionFlags.Invert)!=0?-lane.m_Position.x:lane.m_Position.x);
                    lanes.Add(new float2(x,data.m_Width));
                }
            }
            return CornerRule.DrivingEdges(lanes,EntityManager.GetComponentData<NetCompositionData>(composition).m_Width,out low,out high);
        }
        private bool ReadCornerSlices(Entity node,IList<FarSideLightPolicy.Approach> roads,out List<CurbSlice> slices)
        {
            slices=new List<CurbSlice>();var marking=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            for(int index=0;index<roads.Count;index++)
            {
                var road=roads[index].Road;
                if(!Live(road)||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<Composition>(road))return false;
                var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;if(!start&&edge.m_End!=node)return false;
                if(start?!EntityManager.HasComponent<StartNodeGeometry>(road):!EntityManager.HasComponent<EndNodeGeometry>(road))return false;
                var composition=EntityManager.GetComponentData<Composition>(road);var native=start?composition.m_StartNode:composition.m_EndNode;
                if(!EntityManager.HasComponent<NetCompositionData>(native))return false;
                var data=EntityManager.GetComponentData<NetCompositionData>(native);
                var anchors=ReadSignalCurbAnchors(native,marking);float low=float.MaxValue,high=float.MinValue;
                foreach(var anchor in anchors){low=math.min(low,anchor.x);high=math.max(high,anchor.x);}
                var kind=BoundaryKind.Curb;
                if(anchors.Count<2||high-low<1)
                {kind=BoundaryKind.TrafficLaneEdge;if(!DrivingBounds(native,out low,out high))return false;}
                var geometry=start?EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry:EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry;
                var inner=junctionFootprints.Get(road,start,native,geometry,data,low,high);
                var outer=sidewalkFootprints.Get(road,start,native,geometry,data,-data.m_Width*.5f,data.m_Width*.5f);
                if(inner?.NativeCurves==null||inner.NativeCurves.Length!=2)return false;
                var middle=(inner.NativeCurves[0].a+inner.NativeCurves[1].a)*.5f;
                for(int side=0;side<2;side++)slices.Add(new CurbSlice{Road=index,Side=side,Kind=kind,Curb=inner.NativeCurves[side],
                    HasBack=outer?.NativeCurves!=null,Back=outer?.NativeCurves==null?default:outer.NativeCurves[side],Outside=inner.NativeCurves[side].a-middle});
            }
            return true;
        }
    }
}
