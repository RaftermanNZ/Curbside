using Game.Net;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class RoadTopologyPolicy
    {
        internal static bool ShareEndpoint(Edge a,Edge b)=>a.m_Start!=Entity.Null&&(a.m_Start==b.m_Start||a.m_Start==b.m_End)||a.m_End!=Entity.Null&&(a.m_End==b.m_Start||a.m_End==b.m_End);
    }
}
