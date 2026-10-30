using System;
using Colossal.Mathematics;using Colossal.Serialization.Entities;
using Game;using Game.Common;using Game.Net;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Separate component keeps all previous serialized layouts unchanged.
    public struct MGRSegmentLineVisibility:IComponentData,ISerializable
    {
        public byte Mask;
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Mask);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Mask);
    }
    internal static class SegmentUpgradePolicy
    {
        internal const string All="MGR Hide Segment Lines",Lanes="MGR Hide Lane Lines",Edges="MGR Hide Edge Lines",Native="MGR Hide Native Lines",ResetLines="MGR Restore Line Defaults",ResetParking="MGR Restore Parking Defaults",ResetAll="MGR Reset Segment Upgrades";
        internal const byte HideAll=1,HideLanes=2,HideEdges=4,HideNative=8;
        internal static bool Known(string name)=>name==All||name==Lanes||name==Edges||name==Native||name==ResetLines||name==ResetParking||name==ResetAll;
        internal static byte Selection(string name)=>name==All?HideAll:name==Lanes?HideLanes:name==Edges?HideEdges:name==Native?HideNative:(byte)0;
        internal static byte Mode(EntityManager em,Entity road)=>em.HasComponent<MGRSegmentLineVisibility>(road)?em.GetComponentData<MGRSegmentLineVisibility>(road).Mask:(byte)0;
        internal static bool Hides(byte mask,bool center,bool edge)=>(mask&HideAll)!=0||!center&&((mask&HideLanes)!=0||edge&&(mask&HideEdges)!=0);
        internal static bool Hides(EntityManager em,Entity road,bool center,bool edge)=>Hides(Mode(em,road),center,edge);
        internal static void RevealCenter(EntityManager em,Entity road)
        {if(!em.HasComponent<MGRSegmentLineVisibility>(road))return;var value=em.GetComponentData<MGRSegmentLineVisibility>(road);if((value.Mask&HideAll)!=0){value.Mask=(byte)((value.Mask&~HideAll)|HideLanes);em.SetComponentData(road,value);}}
        internal static void QueuePaint(EntityManager em,Entity road)
        {
            if(em.HasBuffer<Game.Net.SubLane>(road))foreach(var lane in em.GetBuffer<Game.Net.SubLane>(road,true))IntersectionPaintSystem.RequestSource(lane.m_SubLane);
            if(!em.HasComponent<Edge>(road))return;var edge=em.GetComponentData<Edge>(road);
            foreach(var node in new[]{edge.m_Start,edge.m_End}){JunctionMarkingSystem.RequestNodeRefresh(node);if(em.HasBuffer<Game.Net.SubLane>(node))foreach(var lane in em.GetBuffer<Game.Net.SubLane>(node,true))IntersectionPaintSystem.RequestSource(lane.m_SubLane);}
        }
        internal static bool HidesNative(EntityManager em,Entity road)=>(Mode(em,road)&HideNative)!=0;
        internal static bool HidesNode(EntityManager em,Entity node,Bezier4x3 curve,bool center,bool edge,bool native=false)
        {
            if(!em.HasBuffer<ConnectedEdge>(node))return false;
            foreach(var item in em.GetBuffer<ConnectedEdge>(node,true))
            {
                var road=item.m_Edge;if(!(native?HidesNative(em,road):Hides(em,road,center,edge))||!em.HasComponent<EdgeGeometry>(road)||!em.HasComponent<Edge>(road))continue;
                var e=em.GetComponentData<Edge>(road);bool start=e.m_Start==node;if(!start&&e.m_End!=node)continue;
                var g=em.GetComponentData<EdgeGeometry>(road);var s=start?g.m_Start:g.m_End;var a=start?s.m_Left.a:s.m_Left.d;var b=start?s.m_Right.a:s.m_Right.d;
                var axis=(b-a).xz;float length=math.lengthsq(axis);if(length<.01f)continue;
                var tangent=math.normalizesafe(MathUtils.Tangent(MathUtils.Lerp(s.m_Left,s.m_Right,.5f),start?0:1).xz);
                for(int i=0;i<2;i++){var p=i==0?curve.a:curve.d;var nearest=math.lerp(a,b,math.clamp(math.dot((p-a).xz,axis)/length,0,1));if(math.distancesq(p.xz,nearest.xz)<36&&math.abs(p.y-nearest.y)<.5f&&math.abs(math.dot(tangent,math.normalizesafe(MathUtils.Tangent(curve,i).xz)))>.7f)return true;}
            }return false;
        }
    }
    public partial class SegmentUpgradeSystem:GameSystemBase
    {
        protected override void OnCreate(){base.OnCreate();Enabled=false;}
        protected override void OnUpdate(){} // Click-driven only; no city scan.
        private void Remove<T>(Entity road)where T:struct,IComponentData {if(EntityManager.HasComponent<T>(road))EntityManager.RemoveComponent<T>(road);}
        private void ClearPlacedLines(Entity target,byte selection)
        {
            // Explicit native-only hiding remains available as a separate action.
            if(selection==SegmentUpgradePolicy.HideNative)return;
            if(EntityManager.HasBuffer<MGRHighwayLineIntent>(target))
            {
                var intents=EntityManager.GetBuffer<MGRHighwayLineIntent>(target);
                for(int i=intents.Length-1;i>=0;i--)
                    if(selection==SegmentUpgradePolicy.HideAll||SegmentUpgradePolicy.Hides(selection,intents[i].Role==1,intents[i].Role==2))intents.RemoveAt(i);
            }
            if(selection==SegmentUpgradePolicy.HideAll)Remove<MGRCenterlineStyle>(target);
            if(selection==SegmentUpgradePolicy.HideAll||selection==SegmentUpgradePolicy.HideLanes)Remove<MGRLaneDivider>(target);
            if(selection==SegmentUpgradePolicy.HideAll||selection==SegmentUpgradePolicy.HideLanes||selection==SegmentUpgradePolicy.HideEdges){Remove<MGREdgePaint>(target);Remove<MGRSectionEdges>(target);Remove<MGRJunctionEdgeLines>(target);Remove<MGRRemoveJunctionEdges>(target);}
        }
        private void Refresh(Entity road,bool parking=false,bool all=false)
        {
            CurbLineEmissionSystem.QueueRoad(road);if(parking)FixedParkingSystem.QueueRoad(road);if(all)DecalVisibilitySystem.QueueRoad(road);
            SegmentUpgradePolicy.QueuePaint(EntityManager,road);
            if(all){World.GetOrCreateSystemManaged<CurbDecalSystem>().QueueRoad(road);World.GetOrCreateSystemManaged<VisualPowerlineSystem>().QueueRoad(road,true);}
        }
        internal void Apply(Entity road,string asset,bool remove)
        {
            if(Mod.Saving||!EntityManager.HasComponent<Road>(road)||EntityManager.HasComponent<Game.Tools.Temp>(road)||EntityManager.HasComponent<Deleted>(road))return;
            World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
            byte selection=SegmentUpgradePolicy.Selection(asset);
            if(selection!=0)
            {
                if(!remove)ClearPlacedLines(road,selection);
                byte mask=SegmentUpgradePolicy.Mode(EntityManager,road);mask=(byte)(remove?mask&~selection:mask|selection);
                if(mask==0)Remove<MGRSegmentLineVisibility>(road);else{if(!EntityManager.HasComponent<MGRSegmentLineVisibility>(road))EntityManager.AddComponent<MGRSegmentLineVisibility>(road);EntityManager.SetComponentData(road,new MGRSegmentLineVisibility{Mask=mask});}
                Refresh(road);UpgradeFeedback.Report(asset,remove?"Selected line visibility restored.":"Selected lines hidden. Crosswalks and stop bars are kept.");return;
            }
            // Reset is an idempotent action, not an upgrade with an inverse.
            if(remove){UpgradeFeedback.Report(asset,"Click to restore defaults. Reset actions have no right-click inverse.");return;}
            if(asset==SegmentUpgradePolicy.ResetLines||asset==SegmentUpgradePolicy.ResetAll){Remove<MGRCenterlineStyle>(road);Remove<MGRSectionEdges>(road);Remove<MGREdgePaint>(road);Remove<MGRLaneDivider>(road);Remove<MGRHideCenterline>(road);Remove<MGRSegmentLineVisibility>(road);}
            if(asset==SegmentUpgradePolicy.ResetParking||asset==SegmentUpgradePolicy.ResetAll){Remove<MGRParkingHatching>(road);Remove<MGRParkingSides>(road);Remove<MGRParkingUpgrade>(road);Remove<MGRParkingStyle>(road);Remove<MGRParkingBanSides>(road);}
            if(asset==SegmentUpgradePolicy.ResetAll)
            {
                Remove<MGRSurfaceSlots>(road);
                Remove<MGRCurbDecalSides>(road);Remove<MGRCustomLineSides>(road);Remove<MGRVisualPowerlineUpgrade>(road);Remove<RDCSideUpgrade>(road);Remove<RDCUpgrade>(road);
                World.GetOrCreateSystemManaged<StreetLightingSystem>().ResetRoad(road);
                World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().ResetRoadAppearance(road);
                if(EntityManager.HasComponent<Edge>(road))
                {
                    var e=EntityManager.GetComponentData<Edge>(road);foreach(var node in new[]{e.m_Start,e.m_End})
                    {
                        World.GetOrCreateSystemManaged<ApproachStopSystem>().ResetApproach(road,node);
                        World.GetOrCreateSystemManaged<StreetLightingSystem>().ResetNode(node);
                        // Node-wide upgrades cannot be removed for only half a junction.
                        Remove<MGRSurfaceSlots>(node);
                        if(EntityManager.HasBuffer<MGRHighwayLineIntent>(node))EntityManager.RemoveComponent<MGRHighwayLineIntent>(node);Remove<MGREdgePaint>(node);Remove<MGRLaneDivider>(node);Remove<MGRCenterlineStyle>(node);Remove<MGRSegmentLineVisibility>(node);Remove<MGRHideCenterline>(node);Remove<MGRHideNodeLines>(node);Remove<MGRJunctionEdgeLines>(node);Remove<MGRRemoveJunctionEdges>(node);Remove<MGRFarSideLights>(node);FarSideLightSystem.Queue(node);
                    }
                }
            }
            Refresh(road,asset==SegmentUpgradePolicy.ResetParking||asset==SegmentUpgradePolicy.ResetAll,asset==SegmentUpgradePolicy.ResetAll);UpgradeFeedback.Report(asset,asset==SegmentUpgradePolicy.ResetAll?"Curbside section upgrades reset. Shared junction hide/reverse upgrades also restored; native crossings and global settings kept.":asset==SegmentUpgradePolicy.ResetParking?"Curbside parking adjustments cleared on both sides; native parking restored.":"Section line adjustments cleared; default and global markings restored.");
        }
    }
}
