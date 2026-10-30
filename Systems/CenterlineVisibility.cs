using Colossal.Serialization.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRHideCenterline : IComponentData,IEmptySerializable {}
    internal static class CenterlineVisibility
    {
        internal const string AssetName="MGR Hide Centerline";
        internal static bool Hidden(EntityManager em,Entity road)=>em.HasComponent<MGRHideCenterline>(road)||CenterlineUpgradePolicy.Mode(em,road)==CenterlineUpgradePolicy.Removed;
        internal static bool AffectsNodePaint(EntityManager em,Entity node,Bezier4x3 curve)
        {
            if(!em.HasBuffer<ConnectedEdge>(node))return false;
            foreach(var connection in em.GetBuffer<ConnectedEdge>(node,true))
            {
                var road=connection.m_Edge;if(!Hidden(em,road)||!em.HasComponent<Edge>(road)||!em.HasComponent<EdgeGeometry>(road))continue;
                var edge=em.GetComponentData<Edge>(road);bool start=edge.m_Start==node;if(!start&&edge.m_End!=node)continue;
                var geometry=em.GetComponentData<EdgeGeometry>(road);var segment=start?geometry.m_Start:geometry.m_End;
                var left=start?segment.m_Left.a:segment.m_Left.d;var right=start?segment.m_Right.a:segment.m_Right.d;
                var axis=right-left;float length=math.lengthsq(axis.xz);if(length<.01f)continue;
                var tangent=math.normalizesafe(MathUtils.Tangent(MathUtils.Lerp(segment.m_Left,segment.m_Right,.5f),start?0:1).xz);
                for(int i=0;i<2;i++)
                {
                    var point=i==0?curve.a:curve.d;var nearest=math.lerp(left,right,math.clamp(math.dot((point-left).xz,axis.xz)/length,0,1));
                    if(math.distancesq(point.xz,nearest.xz)<=36&&math.abs(point.y-nearest.y)<=.5f&&math.abs(math.dot(tangent,math.normalizesafe(MathUtils.Tangent(curve,i).xz)))>.7f)return true;
                }
            }return false;
        }
    }
}
