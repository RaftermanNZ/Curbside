using System;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class VisualLifecycleSystem
    {
        private readonly DefinitionReferenceCache definitionReferenceCache=new DefinitionReferenceCache();
        private readonly HashSet<Entity> referenceValues=new HashSet<Entity>();
        private readonly List<DefinitionReference> referenceMatches=new List<DefinitionReference>();
        private int referenceTargetRevision;
        private static readonly bool ReferenceChunksSupported=typeof(EntityQuery).Module.ModuleVersionId
            ==new Guid("bfb47e8c-9b6e-4acf-8c6d-c80a7bff1c41");

        private void DefinitionReferenceTargetsChanged()
        {
            if(referenceTargetRevision==int.MaxValue){definitionReferenceCache.Clear();referenceTargetRevision=0;}
            referenceTargetRevision++;
        }

        private void CollectDefinitionReferences(List<DefinitionReference> output)
        {
            output.Clear();
            if(!ReferenceChunksSupported)
            {
                // Unknown runtime retains the original dependency-safe reads.
                definitionReferenceCache.Clear();
                using(var actors=references.ToEntityArray(Allocator.Temp))foreach(var actor in actors)
                {
                    var prefab=EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
                    if(definitionSources.ContainsKey(prefab))output.Add(new DefinitionReference{Actor=actor,Prefab=prefab});
                }
                return;
            }
            // Do not certify headers while a producer can still write later.
            // All existing CompleteReaders calls also remain at their checkpoints.
            EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
            var data=EntityManager.GetComponentTypeHandle<PrefabRef>(true);
            var actorsType=EntityManager.GetEntityTypeHandle();
            uint version=EntityManager.GlobalSystemVersion;
            definitionReferenceCache.Begin(version);
            try
            {
                using(var chunks=references.ToArchetypeChunkArray(Allocator.Temp))foreach(var chunk in chunks)
                {
                    var stamp=new DefinitionReferenceStamp{Sequence=chunk.SequenceNumber,Order=chunk.GetOrderVersion(),
                        Data=chunk.GetChangeVersion(ref data),Count=chunk.Count,SystemVersion=version};
                    if(definitionReferenceCache.TryAppend(stamp,referenceTargetRevision,definitionSources,output))continue;
                    // Consume both views within this read-only scope. The caller
                    // makes structural changes only after this method returns.
                    var prefabs=chunk.GetNativeArray(ref data);
                    var actors=chunk.GetNativeArray(actorsType);
                    referenceValues.Clear();referenceMatches.Clear();
                    for(int i=0;i<prefabs.Length;i++)
                    {
                        var prefab=prefabs[i].m_Prefab;
                        referenceValues.Add(prefab);
                        if(definitionSources.ContainsKey(prefab))referenceMatches.Add(new DefinitionReference{Actor=actors[i],Prefab=prefab});
                    }
                    definitionReferenceCache.Store(stamp,referenceTargetRevision,referenceValues,referenceMatches);
                    output.AddRange(referenceMatches);
                }
                definitionReferenceCache.End();
            }
            catch
            {
                definitionReferenceCache.Clear();output.Clear();throw;
            }
            finally { referenceValues.Clear();referenceMatches.Clear(); }
        }
    }
}
