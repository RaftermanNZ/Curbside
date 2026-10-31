using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using CarLane = Game.Net.CarLane;
namespace MedianGrassReplacement.Systems
{
    // Copies only. Curves already follow travel direction, as authored by LaneSystem.
    internal struct NativeTrafficBoundary
    {
        internal Bezier4x3 Curve;
        internal float Width;
        internal bool Left,Right;
    }
    internal static class NativeTrafficBoundaryPolicy
    {
        // 0 unknown; 1 traffic-left/median; 2 outside edge; 3 same-direction divider.
        internal static byte Classify(Bezier4x3 paint,IList<NativeTrafficBoundary> lanes)
        {
            byte result=0;int votes=0;
            for(int sample=0;sample<3;sample++)
            {
                float t=.2f+sample*.3f;var point=MathUtils.Position(paint,t);
                var tangent=math.normalizesafe(MathUtils.Tangent(paint,t).xz);
                bool left=false,right=false,opposite=false,same=false;
                float2 first=default;bool found=false;
                foreach(var lane in lanes)
                {
                    if(!math.isfinite(lane.Width)||lane.Width<=0)continue;
                    MathUtils.Distance(lane.Curve,point,out float at);
                    var center=MathUtils.Position(lane.Curve,at);
                    var forward=math.normalizesafe(MathUtils.Tangent(lane.Curve,at).xz);
                    if(math.abs(center.y-point.y)>.3f||math.abs(math.dot(tangent,forward))<.95f)continue;
                    float side=math.dot(point.xz-center.xz,new float2(forward.y,-forward.x));
                    if(math.abs(math.abs(side)-lane.Width*.5f)>.35f)continue;
                    // Reject a distant endpoint extension, including other approaches.
                    if(math.abs(math.dot(point.xz-center.xz,forward))>.35f)continue;
                    if(found){float direction=math.dot(first,forward);opposite|=direction<-.95f;same|=direction>.95f;}
                    else{first=forward;found=true;}
                    left|=side<0&&lane.Left;right|=side>0&&lane.Right;
                }
                byte role=opposite?(byte)1:same?(byte)3:left&&!right?(byte)1:right&&!left?(byte)2:(byte)0;
                if(role==0)continue;
                // A crossing or role-changing curve must not inherit a guessed color.
                if(result!=0&&result!=role)return 0;
                result=role;votes++;
            }
            return votes>=2?result:(byte)0;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        internal List<NativeTrafficBoundary> NativeTrafficBoundaries(Entity owner)
        {
            var result=new List<NativeTrafficBoundary>();
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return result;
            int visited=0;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
            {
                if(++visited>512)break;var lane=entry.m_SubLane;
                if(!EntityManager.HasComponent<CarLane>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<MasterLane>(lane)||EntityManager.HasComponent<Deleted>(lane)||EntityManager.HasComponent<Game.Tools.Temp>(lane))continue;
                var flags=EntityManager.GetComponentData<CarLane>(lane).m_Flags;
                if((flags&CarLaneFlags.Twoway)!=0)continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                if(!EntityManager.HasComponent<NetLaneData>(prefab))continue;
                result.Add(new NativeTrafficBoundary{Curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier,Width=EntityManager.GetComponentData<NetLaneData>(prefab).m_Width,Left=(flags&CarLaneFlags.LeftLimit)!=0,Right=(flags&CarLaneFlags.RightLimit)!=0});
            }
            return result;
        }
    }
}
