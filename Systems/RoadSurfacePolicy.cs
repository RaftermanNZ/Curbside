using Game.Prefabs;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class RoadSurfacePolicy
    {
        internal static bool IsHighway(EntityManager em,Entity road)
        {
            if(!em.HasComponent<PrefabRef>(road))return false;
            var prefab=em.GetComponentData<PrefabRef>(road).m_Prefab;
            return em.HasComponent<RoadData>(prefab)&&(em.GetComponentData<RoadData>(prefab).m_Flags&Game.Prefabs.RoadFlags.UseHighwayRules)!=0;
        }
        internal static bool IsPaved(EntityManager em,Entity road)
        {
            if(!em.HasComponent<Game.Net.Composition>(road))return false;
            var composition=em.GetComponentData<Game.Net.Composition>(road).m_Edge;
            return em.HasComponent<NetCompositionData>(composition)&&RoadSurfacePolicy.PavedFlags(em.GetComponentData<NetCompositionData>(composition).m_Flags.m_General);
        }
        internal static bool PavedFlags(CompositionFlags.General flags)=>(flags&CompositionFlags.General.Gravel)==0;
        // Edge controls require an identified alley. Narrowness alone also matches ordinary roads.
        internal static bool IsAlleyForEdges(EntityManager em,Entity road)
        {
            if(!IsPaved(em,road)||IsHighway(em,road)||!em.HasComponent<PrefabRef>(road))return false;
            var prefabs=em.World.GetExistingSystemManaged<PrefabSystem>();
            return prefabs!=null&&prefabs.TryGetPrefab<RoadPrefab>(em.GetComponentData<PrefabRef>(road).m_Prefab,out var native)&&native!=null&&native.name.Equals("Alley",System.StringComparison.OrdinalIgnoreCase);
        }
        internal static bool IsAlley(EntityManager em,Entity road)
        {
            if(!IsPaved(em,road)||IsHighway(em,road)||!em.HasComponent<Game.Net.Composition>(road))return false;
            var composition=em.GetComponentData<Game.Net.Composition>(road).m_Edge;
            if(!em.HasComponent<NetCompositionData>(composition))return false;
            var data=em.GetComponentData<NetCompositionData>(composition);
            var prefabs=em.World.GetExistingSystemManaged<PrefabSystem>();
            if(prefabs!=null&&em.HasComponent<PrefabRef>(road)&&prefabs.TryGetPrefab<RoadPrefab>(em.GetComponentData<PrefabRef>(road).m_Prefab,out var native)&&native!=null&&native.name.Equals("Alley",System.StringComparison.OrdinalIgnoreCase))return true;
            float left=float.MaxValue,right=float.MinValue;
            if(em.HasBuffer<NetCompositionLane>(composition))foreach(var lane in em.GetBuffer<NetCompositionLane>(composition,true))
            {
                if((lane.m_Flags&(LaneFlags.Road|LaneFlags.Master|LaneFlags.Underground))!=LaneFlags.Road||!em.HasComponent<NetLaneData>(lane.m_Lane))continue;
                float width=em.GetComponentData<NetLaneData>(lane.m_Lane).m_Width;
                if(!Unity.Mathematics.math.isfinite(width)||width<=0)continue;
                left=Unity.Mathematics.math.min(left,lane.m_Position.x-width*.5f);right=Unity.Mathematics.math.max(right,lane.m_Position.x+width*.5f);
            }
            return AlleyGeometry(data.m_Width,right-left,data.m_Flags.m_Left,data.m_Flags.m_Right);
        }
        internal static bool AlleyGeometry(float totalWidth,float drivingWidth,CompositionFlags.Side left,CompositionFlags.Side right)
        {
            const CompositionFlags.Side sidewalk=CompositionFlags.Side.Sidewalk|CompositionFlags.Side.WideSidewalk;
            bool raisedLeft=(left&sidewalk)!=0&&(left&CompositionFlags.Side.Raised)!=0;
            bool raisedRight=(right&sidewalk)!=0&&(right&CompositionFlags.Side.Raised)!=0;
            return Unity.Mathematics.math.isfinite(totalWidth)&&totalWidth>0&&!raisedLeft&&!raisedRight&&(totalWidth<=10||Unity.Mathematics.math.isfinite(drivingWidth)&&drivingWidth>0&&drivingWidth<=8);
        }
        internal static float DecalOffset(EntityManager em,Entity road)=>IsHighway(em,road)?Mod.Settings.HighwayDecalOffset:IsAlley(em,road)?Mod.Settings.AlleyDecalOffset:Mod.Settings.CurbDecalOffset;
    }
}
