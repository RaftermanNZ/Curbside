using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class NativePreviewSection
    {
        // Native split nodes name the original edge and its curve parameter.
        // Endpoint identity supplies direction. No geometric nearest-road match.
        internal static bool TryRange(EntityManager manager, Entity preview, Entity original, out float2 range)
        {
            range = new float2(0, 1);
            if (!manager.HasComponent<Edge>(preview) || !manager.HasComponent<Edge>(original)) return true;
            var before = manager.GetComponentData<Edge>(original);
            var after = manager.GetComponentData<Edge>(preview);
            bool Endpoint(Entity node, out float position)
            {
                position = 0;
                if (node == before.m_Start) return true;
                if (node == before.m_End) { position = 1; return true; }
                if (!manager.Exists(node) || manager.HasComponent<Deleted>(node) || !manager.HasComponent<Temp>(node)) return false;
                var temp = manager.GetComponentData<Temp>(node);
                if (temp.m_Original == before.m_Start) return true;
                if (temp.m_Original == before.m_End) { position = 1; return true; }
                if (temp.m_Original != original || !math.isfinite(temp.m_CurvePosition)
                    || temp.m_CurvePosition < 0 || temp.m_CurvePosition > 1) return false;
                position = temp.m_CurvePosition; return true;
            }
            if (!Endpoint(after.m_Start, out float start) || !Endpoint(after.m_End, out float end)) return false;
            range = new float2(start, end);
            return math.abs(end - start) > .000001f;
        }
    }
}
