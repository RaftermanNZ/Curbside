using Game.Net;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class DecalUpgradeTool
    {
        private void ApplyHoverVisibility(Entity target,bool node,byte role,bool restore)
        {
            if(!node)World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(target);
            if(restore){byte mask=SegmentUpgradePolicy.Mode(EntityManager,target);
                if(mask!=0){
                    if((mask&SegmentUpgradePolicy.HideAll)!=0&&role!=0){if(!EntityManager.HasComponent<MGRCenterlineStyle>(target))EntityManager.AddComponent<MGRCenterlineStyle>(target);EntityManager.SetComponentData(target,new MGRCenterlineStyle{Value=3});}
                    if((mask&(SegmentUpgradePolicy.HideAll|SegmentUpgradePolicy.HideLanes))!=0&&role!=4){if(!EntityManager.HasComponent<MGRLaneDivider>(target))EntityManager.AddComponent<MGRLaneDivider>(target);EntityManager.SetComponentData(target,new MGRLaneDivider{Value=2});}
                    if((mask&(SegmentUpgradePolicy.HideAll|SegmentUpgradePolicy.HideLanes|SegmentUpgradePolicy.HideEdges))!=0){var edges=EntityManager.HasComponent<MGREdgePaint>(target)?EntityManager.GetComponentData<MGREdgePaint>(target):default;if(role!=1&&role!=3)edges.Left=5;if(role!=2&&role!=3)edges.Right=5;if(!EntityManager.HasComponent<MGREdgePaint>(target))EntityManager.AddComponent<MGREdgePaint>(target);EntityManager.SetComponentData(target,edges);}
                    if((mask&SegmentUpgradePolicy.HideNative)!=0)EntityManager.SetComponentData(target,new MGRSegmentLineVisibility{Mask=SegmentUpgradePolicy.HideNative});else EntityManager.RemoveComponent<MGRSegmentLineVisibility>(target);
                }
            }
            if(role==0){if(restore){EntityManager.RemoveComponent<MGRCenterlineStyle>(target);EntityManager.RemoveComponent<MGRHideCenterline>(target);}else {if(!EntityManager.HasComponent<MGRCenterlineStyle>(target))EntityManager.AddComponent<MGRCenterlineStyle>(target);EntityManager.SetComponentData(target,new MGRCenterlineStyle{Value=3});}}
            else if(role==4){if(restore)EntityManager.RemoveComponent<MGRLaneDivider>(target);else {if(!EntityManager.HasComponent<MGRLaneDivider>(target))EntityManager.AddComponent<MGRLaneDivider>(target);EntityManager.SetComponentData(target,new MGRLaneDivider{Value=2});}}
            else {var edges=EntityManager.HasComponent<MGREdgePaint>(target)?EntityManager.GetComponentData<MGREdgePaint>(target):default;if(role==1||role==3)edges.Left=restore?(byte)0:(byte)5;if(role==2||role==3)edges.Right=restore?(byte)0:(byte)5;if(!EntityManager.HasComponent<MGREdgePaint>(target))EntityManager.AddComponent<MGREdgePaint>(target);EntityManager.SetComponentData(target,edges);if(restore){EntityManager.RemoveComponent<MGRSectionEdges>(target);if(node){EntityManager.RemoveComponent<MGRRemoveJunctionEdges>(target);EntityManager.RemoveComponent<MGRJunctionEdgeLines>(target);}}else if(node&&!EntityManager.HasComponent<MGRRemoveJunctionEdges>(target))EntityManager.AddComponent<MGRRemoveJunctionEdges>(target);}
            var emitter=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();if(node){emitter.RefreshNodePaint(target);emitter.QueueNodeRefresh(target);}else{CurbLineEmissionSystem.QueueRoad(target);SegmentUpgradePolicy.QueuePaint(EntityManager,target);}
            UpgradeFeedback.Report(LineShapePolicy.Visibility,restore?"Highlighted category restored to defaults; independent visibility rules still apply.":"Highlighted category hidden, including native and Curbside paint.");
        }
    }
}
