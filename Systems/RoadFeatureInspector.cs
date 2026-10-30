using System.Collections.Generic;
using Game.Net;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class RoadFeatureInspector
    {
        internal const string AssetName="MGR Inspect Road";
        private static string Sides(byte mask)=>(mask&3)==3?"both sides":(mask&1)!=0?"left":"right";
        private static string Parking(byte v)=>v==1?"Unmarked parking bays":v==2?"Marked parking bays":v==3?"T-mark parking bays":v==5?"Full-depth T parking bays":v==6?"Parking dividers only":"Parking dividers to curb";
        internal static string Describe(EntityManager em,Entity road)
        {
            var lines=new List<string>();
            if(em.HasComponent<MGRParkingHatching>(road))lines.Add("Parking-end hatching - "+Sides(em.GetComponentData<MGRParkingHatching>(road).Sides));
            byte center=CenterlineUpgradePolicy.Mode(em,road);if(center!=0)lines.Add(CenterlineUpgradePolicy.Label(center));
            if(em.HasComponent<MGRHideCenterline>(road))lines.Add("Hide centerline");
            byte hidden=SegmentUpgradePolicy.Mode(em,road);
            if((hidden&1)!=0)lines.Add("Hide all section lines");
            if((hidden&2)!=0)lines.Add("Hide lane lines, keep center");
            if((hidden&4)!=0)lines.Add("Hide edge lines");
            if((hidden&8)!=0)lines.Add("Hide vanilla lines");
            foreach(bool side in new[]{true,false}){byte edgeMode=EdgeUpgradePolicy.Mode(em,road,side);if(edgeMode!=0)lines.Add(EdgeUpgradePolicy.Label(edgeMode)+(side?" (left)":" (right)"));}
            byte sectionEdges=SectionEdgePolicy.Mode(em,road);if(sectionEdges!=0)lines.Add(sectionEdges==1?"Section edge lines":"Remove section edge lines");
            byte divider=LaneDividerPolicy.Mode(em,road);if(divider!=0)lines.Add(divider==2?"Remove lane dividers":LaneDividerPolicy.Label(divider));
            byte ban=em.HasComponent<MGRParkingBanSides>(road)?em.GetComponentData<MGRParkingBanSides>(road).Mask:(byte)0;
            var parking=RoadSideUpgrades.Parking(em,road);
            if(parking.Left!=0&&(ban&1)==0)lines.Add(Parking(parking.Left)+" — left");
            if(parking.Right!=0&&(ban&2)==0)lines.Add(Parking(parking.Right)+" — right");
            if(ban!=0)lines.Add("No parking — "+Sides(ban));
            if(em.HasComponent<MGRCurbDecalSides>(road)){byte mask=em.GetComponentData<MGRCurbDecalSides>(road).Mask;if(mask!=0)lines.Add("Road-wear decals — "+Sides(mask));}
            if(em.HasComponent<MGRVisualPowerlineUpgrade>(road)){byte mask=em.GetComponentData<MGRVisualPowerlineUpgrade>(road).Sides;if(mask!=0)lines.Add("Decorative powerlines — "+Sides(mask));}
            if(em.HasComponent<RDCSideUpgrade>(road))
            {
                var h=em.GetComponentData<RDCSideUpgrade>(road);
                if((h.Left&1)!=0)lines.Add("Hide lane arrows — left");if((h.Right&1)!=0)lines.Add("Hide lane arrows — right");
                if((h.Left&2)!=0)lines.Add("Hide speed decals — left");if((h.Right&2)!=0)lines.Add("Hide speed decals — right");
            }
            byte lighting=StreetLightingSystem.Mode(em,road);if(lighting!=0)lines.Add(lighting==2?"Remove street lights":"Add street lights");
            if(em.HasComponent<Edge>(road))
            {
                var edge=em.GetComponentData<Edge>(road);foreach(bool start in new[]{true,false})
                {
                    var node=start?edge.m_Start:edge.m_End;string suffix=start?" — start node":" — end node";
                    byte nodeCenter=CenterlineUpgradePolicy.Mode(em,node);if(nodeCenter!=0)lines.Add((CenterlineUpgradePolicy.Label(nodeCenter))+suffix);
                    byte nodeEdge=EdgeUpgradePolicy.Mode(em,node,true);if(nodeEdge!=0)lines.Add(EdgeUpgradePolicy.Label(nodeEdge)+suffix);
                    byte nodeDivider=LaneDividerPolicy.Mode(em,node);if(nodeDivider!=0)lines.Add((nodeDivider==2?"Remove lane dividers":LaneDividerPolicy.Label(nodeDivider))+suffix);
                    byte nodeHidden=SegmentUpgradePolicy.Mode(em,node);if((nodeHidden&1)!=0)lines.Add("Hide native lines"+suffix);if((nodeHidden&2)!=0)lines.Add("Hide native lane lines"+suffix);if((nodeHidden&4)!=0)lines.Add("Hide native edge lines"+suffix);if((nodeHidden&8)!=0)lines.Add("Hide vanilla lines"+suffix);
                    if(em.HasComponent<MGRHideCenterline>(node))lines.Add("Hide native centerline"+suffix);
                    if(em.HasComponent<MGRHideNodeLines>(node))lines.Add("Hide junction lines"+suffix);
                    if(nodeEdge==0&&em.HasComponent<MGRRemoveJunctionEdges>(node))lines.Add("Remove junction edge lines"+suffix);
                    if(nodeEdge==0&&em.HasComponent<MGRJunctionEdgeLines>(node))lines.Add("Junction edge lines"+suffix);
                    if(em.HasComponent<MGRNodeStreetLighting>(node))lines.Add((em.GetComponentData<MGRNodeStreetLighting>(node).Value==2?"Remove street lights":"Add street lights")+suffix);
                    if(em.HasComponent<MGRFarSideLights>(node))lines.Add("Far-side traffic lights"+suffix);
                    if(em.HasComponent<MGRCrosswalkRoadStyle>(road)&&em.GetComponentData<MGRCrosswalkRoadStyle>(road).TryGet(start,out bool line)&&line)lines.Add("Lined crosswalk"+suffix);
                    if(em.HasComponent<MGRInvisibleCrosswalkStyle>(road)&&em.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start))lines.Add("Invisible crosswalk"+suffix);
                    if(em.HasBuffer<MGRStopApproach>(node)){var direction=ApproachStopSystem.Away(em,road,node);foreach(var choice in em.GetBuffer<MGRStopApproach>(node,true))if(CrosswalkIntentPolicy.Score(direction,choice.Outward)>.85f){if(choice.Stop==1)lines.Add("Stop"+suffix);else if(choice.Stop==2)lines.Add("Yield"+suffix);break;}}
                }
            }
            return lines.Count==0?"No Curbside tools applied.":string.Join("\n",lines);
        }
    }
}
