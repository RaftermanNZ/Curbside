using System.Collections.Generic;using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public partial class DecalVisibilitySystem
    {
        internal List<float3> PreviewPositions(Entity road,bool left,byte mask)
        {
            var result=new List<float3>();PreviewChildren(road,road,left,mask,0,result);
            if(EntityManager.HasComponent<Edge>(road)){var edge=EntityManager.GetComponentData<Edge>(road);PreviewChildren(edge.m_Start,road,left,mask,0,result);PreviewChildren(edge.m_End,road,left,mask,0,result);}return result;
        }
        private void PreviewChildren(Entity owner,Entity road,bool left,byte mask,int depth,List<float3> result)
        {
            if(depth>4||result.Count>=64||!Live(owner))return;
            if(EntityManager.HasComponent<PrefabRef>(owner)&&EntityManager.HasComponent<Game.Objects.Transform>(owner)&&(Kind(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab)&mask)!=0&&RoadFor(owner)==road)
            {var point=EntityManager.GetComponentData<Game.Objects.Transform>(owner).m_Position;if(RoadSideUpgrades.IsLeft(EntityManager,road,point)==left)result.Add(point);}
            if(EntityManager.HasBuffer<Game.Objects.SubObject>(owner))foreach(var child in EntityManager.GetBuffer<Game.Objects.SubObject>(owner,true))PreviewChildren(child.m_SubObject,road,left,mask,depth+1,result);
        }
    }
}
