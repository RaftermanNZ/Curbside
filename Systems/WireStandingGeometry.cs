using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class WireStandingGeometry
    {
        internal static bool Footprint(IList<Bounds3> poles,out float3 size)
        {
            size=default;float radius=0;
            foreach(var bounds in poles)
            {
                if(!math.all(math.isfinite(bounds.min))||!math.all(math.isfinite(bounds.max)))return false;
                foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.max.z})radius=math.max(radius,math.length(new float2(x,z)));
            }
            if(radius<=0||radius>.5f)return false;
            // Cover actual authored support parts, rather than the inherited
            // vanilla 0.2 m leg. The full native render/body bounds stay intact.
            size=new float3(2*radius,1.7f,2*radius);return true;
        }
        internal static void Configure(StaticObjectPrefab prefab,IList<ObjectMeshInfo> parts)
        {
            var poles=new List<Bounds3>();bool remote=false;
            foreach(var part in parts)if(part?.m_Mesh is RenderPrefab render&&SignalMastSnapshotPolicy.Pole(render.name))
            {
                if(math.lengthsq(part.m_Position.xz)>.0001f){remote=true;continue;}
                poles.Add(Game.Objects.ObjectUtils.CalculateBounds(part.m_Position,part.m_Rotation,render.bounds));
            }
            // Head-only phase actors have no standing support. Keep standing
            // semantics for assemblies with a local support, including those
            // that also carry a virtual T-corner pole: removing it would make
            // the full low body box cover the roadway for native lane blocking.
            // A remote-only assembly keeps its inherited native definition.
            if(remote&&poles.Count==0)return;
            if(!Footprint(poles,out var size)){prefab.Remove<StandingObject>();return;}
            var standing=prefab.AddOrGetComponent<StandingObject>();standing.m_LegSize=size;standing.m_LegGap=float2.zero;standing.m_CircularLeg=true;
        }
    }
}
