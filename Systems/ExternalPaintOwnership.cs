using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
namespace MedianGrassReplacement.Systems
{
    // Optional component identity only: no third-party code or assembly reference.
    internal static class ExternalPaintPolicy
    {
        internal static bool IsUserPaint(string name)=>name=="TownRoadLane.Components.TRLSegmentLink"||name=="TownRoadLane.Components.TRLAreaLink"||name!=null&&name.StartsWith("TownRoadLane.Components.Marking",StringComparison.Ordinal);
        internal static string Key(string name)=>(name??"").Replace(" ","").Replace("_","").Replace("-","");
        internal static bool IsBoundary(string name)
        {
            string key=Key(name);
            return key.IndexOf("CenterLine",StringComparison.OrdinalIgnoreCase)>=0||key.IndexOf("EdgeLine",StringComparison.OrdinalIgnoreCase)>=0||key.IndexOf("LaneLine",StringComparison.OrdinalIgnoreCase)>=0;
        }
    }
    internal sealed class ExternalPaintOwnership
    {
        private readonly Dictionary<EntityArchetype,bool> _cache=new Dictionary<EntityArchetype,bool>();
        internal void Clear()=>_cache.Clear();
        internal bool Contains(EntityManager em,Entity entity)
        {
            if(!em.Exists(entity))return false;
            var archetype=em.GetChunk(entity).Archetype;
            if(_cache.TryGetValue(archetype,out bool owned))return owned;
            using(var components=em.GetComponentTypes(entity,Allocator.Temp))
                foreach(var component in components)
                    if(ExternalPaintPolicy.IsUserPaint(component.GetManagedType()?.FullName)){owned=true;break;}
            _cache[archetype]=owned;return owned;
        }
    }
}
