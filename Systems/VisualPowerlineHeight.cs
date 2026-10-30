using System.Collections.Generic;
using Colossal.Mathematics;using Game.Common;using Game.Prefabs;
using Unity.Collections;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class PoleHeightPolicy
    {
        internal static float Requested(float value)=>math.isfinite(value)?math.clamp(value,5,100):10;
        internal static float Scale(float requested,float authored)=>math.isfinite(authored)&&authored>0?math.max(.5f,Requested(requested)/authored):1;
        internal static StackData Definition(float min,float max)=>new StackData{m_FirstBounds=new Bounds1(min,max),m_MiddleBounds=new Bounds1(0,0),m_LastBounds=new Bounds1(0,0),m_Direction=StackDirection.Up,m_DontScale=new bool3(false,true,true)};
    }
    public partial class VisualPowerlineSystem
    {
        private readonly Dictionary<Entity,Entity> _scaledPoles=new Dictionary<Entity,Entity>();
        private EntityQuery _poleReferences;private float _poleScale=1;private Bounds1 _poleRange;
        private void InitializePoleHeights()=>_poleReferences=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<PrefabRef>()},Options=EntityQueryOptions.IncludeDisabledEntities|EntityQueryOptions.IncludePrefab});
        private void PreparePoleHeight()
        {
            var source=_pole;var geometry=EntityManager.GetComponentData<ObjectGeometryData>(source);
            float low=geometry.m_Bounds.min.y,high=geometry.m_Bounds.max.y;
            if(!math.isfinite(high)||high<=0)return;
            _poleScale=PoleHeightPolicy.Scale(_preferredHeight,high);_poleRange=new Bounds1(low*_poleScale,high*_poleScale);
            if(!_scaledPoles.TryGetValue(source,out var copy))
            {
                copy=EntityManager.Instantiate(source);
                if(!EntityManager.HasComponent<Prefab>(copy))EntityManager.AddComponent<Prefab>(copy);
                if(EntityManager.HasComponent<PrefabData>(copy))EntityManager.SetComponentEnabled<PrefabData>(copy,false);
                if(EntityManager.HasComponent<Created>(copy))EntityManager.RemoveComponent<Created>(copy);
                if(EntityManager.HasComponent<Updated>(copy))EntityManager.RemoveComponent<Updated>(copy);
                _scaledPoles[source]=copy;
            }
            var definition=PoleHeightPolicy.Definition(low,high);
            if(EntityManager.HasComponent<StackData>(copy))EntityManager.SetComponentData(copy,definition);else EntityManager.AddComponentData(copy,definition);
            if(EntityManager.HasBuffer<SubMesh>(source))
            {
                var meshes=EntityManager.GetBuffer<SubMesh>(copy);meshes.CopyFrom(EntityManager.GetBuffer<SubMesh>(source,true));
                for(int i=0;i<meshes.Length;i++){var mesh=meshes[i];mesh.m_Flags&=~(SubMeshFlags.IsStackMiddle|SubMeshFlags.IsStackEnd);mesh.m_Flags|=SubMeshFlags.IsStackStart;mesh.m_Position.y*=_poleScale;meshes[i]=mesh;}
            }
            for(int i=0;i<_attachments.Length;i++)_attachments[i].y*=_poleScale;
            _pole=copy;
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("powerline.height","requested="+_preferredHeight+" authored="+high+" scale="+_poleScale+" actual="+_poleRange.max);
        }
        private void RestorePoleDefinitions()
        {
            if(_scaledPoles.Count==0)return;Dependency.Complete();
            var reverse=new Dictionary<Entity,Entity>();foreach(var pair in _scaledPoles)reverse[pair.Value]=pair.Key;
            // Save/disposal only, including deleted/temp references not in the live pole pool.
            using(var refs=_poleReferences.ToEntityArray(Allocator.Temp))foreach(var e in refs)
            {
                var reference=EntityManager.GetComponentData<PrefabRef>(e);
                if(reverse.TryGetValue(reference.m_Prefab,out var native)&&EntityManager.Exists(native))EntityManager.SetComponentData(e,new PrefabRef(native));
            }
            foreach(var copy in _scaledPoles.Values)if(EntityManager.Exists(copy))EntityManager.DestroyEntity(copy);
            _scaledPoles.Clear();_pole=Entity.Null;
        }
        internal void DisposeResources(){Clear();RestorePoleDefinitions();}
    }
}
