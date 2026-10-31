using Colossal.Mathematics;using Game.Common;using Game.Net;using Game.Tools;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class SectionUpgradeTargeting
    {
        // Keep the actual network hit for markings/inspection. Section-only
        // decoration targeting deliberately retains TryRoad's existing behavior.
        internal static bool TryMarking(EntityManager em,Entity hit,Entity owner,float3 position,out Entity target)
        {
            target=Entity.Null;if(!math.all(math.isfinite(position)))return false;
            if(StreetLightingSystem.TryLightTarget(em,hit,out var direct)&&em.HasComponent<Node>(direct)){target=direct;return true;}
            if(StreetLightingSystem.TryLightTarget(em,owner,out var parent)&&em.HasComponent<Node>(parent)){target=parent;return true;}
            return TryRoad(em,owner,position,out target)||TryRoad(em,hit,position,out target);
        }
        internal static bool TryRoad(EntityManager em,Entity hit,float3 position,out Entity road)
        {
            road=hit;if(!math.all(math.isfinite(position)))return false;
            for(int depth=0;depth<8;depth++)
            {
                if(em.HasComponent<Temp>(road)){var original=em.GetComponentData<Temp>(road).m_Original;if(original==Entity.Null||original==road)return false;road=original;continue;}
                if(em.HasComponent<Road>(road)&&em.HasComponent<Edge>(road)&&em.HasComponent<Curve>(road)&&!em.HasComponent<Deleted>(road))return true;
                if(em.HasComponent<Node>(road)&&em.HasBuffer<ConnectedEdge>(road))
                {
                    Entity best=Entity.Null;float limit=float.MaxValue;
                    foreach(var item in em.GetBuffer<ConnectedEdge>(road,true))
                    {
                        var candidate=item.m_Edge;if(!em.HasComponent<Road>(candidate)||!em.HasComponent<Edge>(candidate)||!em.HasComponent<Curve>(candidate)||em.HasComponent<Deleted>(candidate)||em.HasComponent<Temp>(candidate))continue;
                        float distance=MathUtils.Distance(em.GetComponentData<Curve>(candidate).m_Bezier,position,out _);
                        if(math.isfinite(distance)&&(distance<limit-.001f||math.abs(distance-limit)<=.001f&&(best==Entity.Null||candidate.Index<best.Index))){best=candidate;limit=distance;}
                    }
                    road=best;return best!=Entity.Null;
                }
                if(!em.HasComponent<Owner>(road))return false;var next=em.GetComponentData<Owner>(road).m_Owner;if(next==road)return false;road=next;
            }
            return false;
        }
    }
}
