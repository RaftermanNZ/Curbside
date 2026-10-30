using Game.Common;using Game.Net;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class SegmentUpgradeSystem
    {
        internal void ApplyNode(Entity node,string asset,bool remove)
        {
            if(Mod.Saving||!EntityManager.HasComponent<Node>(node)||EntityManager.HasComponent<Deleted>(node))return;
            byte selected=SegmentUpgradePolicy.Selection(asset);
            if(selected!=0)
            {
                if(selected==SegmentUpgradePolicy.HideAll||selected==SegmentUpgradePolicy.HideNative)Remove<MGRHideNodeLines>(node);
                if(!remove)ClearPlacedLines(node,selected);
                byte mask=SegmentUpgradePolicy.Mode(EntityManager,node);mask=(byte)(remove?mask&~selected:mask|selected);
                if(mask==0)Remove<MGRSegmentLineVisibility>(node);else{if(!EntityManager.HasComponent<MGRSegmentLineVisibility>(node))EntityManager.AddComponent<MGRSegmentLineVisibility>(node);EntityManager.SetComponentData(node,new MGRSegmentLineVisibility{Mask=mask});}
            }
            else if(asset==CenterlineVisibility.AssetName)
            {
                if(!remove)Remove<MGRCenterlineStyle>(node);
                if(remove)Remove<MGRHideCenterline>(node);else if(!EntityManager.HasComponent<MGRHideCenterline>(node))EntityManager.AddComponent<MGRHideCenterline>(node);
            }
            else if(asset==SegmentUpgradePolicy.ResetLines&&!remove)
            {
                Remove<MGRCenterlineStyle>(node);Remove<MGRLaneDivider>(node);Remove<MGREdgePaint>(node);Remove<MGRHideCenterline>(node);Remove<MGRSegmentLineVisibility>(node);Remove<MGRHideNodeLines>(node);Remove<MGRJunctionEdgeLines>(node);Remove<MGRRemoveJunctionEdges>(node);
            }
            else return;
            var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();emitter.QueueNodeRefresh(node);emitter.RefreshNodePaint(node);
            UpgradeFeedback.Report(asset,asset==SegmentUpgradePolicy.ResetLines?"Node: line choices restored to defaults. Sections kept.":remove?"Node: selected native paint visibility restored.":"Node: selected paint hidden; matching placed line choices cleared. Crossings and stop bars kept.");
        }
    }
}
