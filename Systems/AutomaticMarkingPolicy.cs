using Game.Common;
using Game.Net;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class AutomaticMarkingPolicy
    {
        internal static bool Allows(EntityManager em, Entity owner)
        {
            var settings=Mod.Settings;
            if(settings==null||settings.DisableAutomaticMarkings)return false;
            if(!settings.DisableAutomaticHighwayMarkings)return true;
            if(em.HasComponent<Road>(owner))return !RoadSurfacePolicy.IsHighway(em,owner);
            if(em.HasBuffer<ConnectedEdge>(owner))
                foreach(var entry in em.GetBuffer<ConnectedEdge>(owner,true))
                    if(em.HasComponent<Road>(entry.m_Edge)&&!em.HasComponent<Deleted>(entry.m_Edge)&&RoadSurfacePolicy.IsHighway(em,entry.m_Edge))return false;
            return true;
        }
        internal static bool Adjusts(EntityManager em,Entity owner)=>Allows(em,owner)&&!Mod.Settings.DisableAutomaticLineAdjustments;
        internal static bool PreservesNode(EntityManager em,Entity owner)=>Mod.Settings!=null&&Mod.Settings.PreserveVanillaJunctionLines&&em.HasComponent<Node>(owner);
        internal static bool PreservesGeometry(EntityManager em,Entity owner)=>Mod.Settings!=null&&Mod.Settings.PreserveVanillaJunctionLines&&(em.HasComponent<Node>(owner)||em.HasComponent<Edge>(owner));
    }
}
