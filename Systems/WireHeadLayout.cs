using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal sealed class WireHeadLayout
    {
        private float3 pole,inward,left,right;
        private float2 outward;
        private float3[] bank,supports;
        private float[] distances;
        private int target;
        private byte style;
        private bool rigidSigns,lowerSigns;

        internal static WireHeadLayout Create(FarSideLightPolicy.Approach road,float3 pole,
            IList<float3> supports,byte style)
        {
            road.WireHeads=null;
            if(!GardenCityLightPolicy.TryWireLayout(road,pole,supports,style,out var inward,out var heads,out int target))return null;
            var full=road.WireBank??road.Incoming;
            if(full==null||full.Count!=heads.Length)return null;
            var old=GardenCityLightPolicy.TryLayout(full,road.Outward,pole,out _,out var straight);
            float projection=math.dot(supports[target]-pole,inward);
            old=old&&straight[straight.Length-1]+.3f<=projection;
            var order=new List<int>();for(int i=0;i<full.Count;i++)order.Add(i);
            var span=supports[target].xz-pole.xz;var across=road.Right.xz-road.Left.xz;
            float direction=math.dot(across,span)<0?-1:1;
            order.Sort((a,b)=>
            {
                float first=old?math.dot(full[a].xz-pole.xz,inward.xz):direction*math.dot(full[a].xz-road.Left.xz,across);
                float second=old?math.dot(full[b].xz-pole.xz,inward.xz):direction*math.dot(full[b].xz-road.Left.xz,across);
                return first.CompareTo(second);
            });
            var plan=new WireHeadLayout{pole=pole,inward=inward,target=target,style=style,
                left=road.Left,right=road.Right,outward=road.Outward,
                bank=new float3[full.Count],supports=new float3[supports.Count],distances=new float[heads.Length],
                rigidSigns=global::GardenCitySignals.SignalPackPolicy.UseRigidSignLayout,
                lowerSigns=global::GardenCitySignals.SignalPackPolicy.UseLowerRigidSignLayout};
            for(int i=0;i<full.Count;i++){plan.bank[i]=full[i];plan.distances[order[i]]=heads[i];}
            for(int i=0;i<supports.Count;i++)plan.supports[i]=supports[i];
            return plan;
        }

        internal bool TryRead(FarSideLightPolicy.Approach road,float3 position,IList<float3> points,byte requested,
            out float3 direction,out float[] heads,out int other)
        {
            direction=default;heads=null;other=-1;
            var full=road.WireBank??road.Incoming;
            if(requested!=style||!position.Equals(pole)||!road.Left.Equals(left)||!road.Right.Equals(right)||
                !road.Outward.Equals(outward)||points==null||points.Count!=supports.Length||
                full==null||full.Count!=bank.Length||road.Incoming==null||road.Incoming.Count==0||
                rigidSigns!=global::GardenCitySignals.SignalPackPolicy.UseRigidSignLayout||
                lowerSigns!=global::GardenCitySignals.SignalPackPolicy.UseLowerRigidSignLayout)return false;
            for(int i=0;i<supports.Length;i++)if(!points[i].Equals(supports[i]))return false;
            for(int i=0;i<bank.Length;i++)if(!full[i].Equals(bank[i]))return false;
            heads=new float[road.Incoming.Count];
            for(int i=0;i<road.Incoming.Count;i++)
            {
                int index=Array.IndexOf(bank,road.Incoming[i]);if(index<0)return false;
                heads[i]=distances[index];
            }
            Array.Sort(heads);direction=inward;other=target;return true;
        }
    }

    internal static partial class GardenCityLightPolicy
    {
        internal static bool CacheWireLayouts(IList<FarSideLightPolicy.Approach> roads,IList<float3> supports,
            IList<int> sources,byte style)
        {
            var plans=new WireHeadLayout[sources.Count];
            for(int i=0;i<sources.Count;i++)
            {plans[i]=WireHeadLayout.Create(roads[sources[i]],supports[i],supports,style);if(plans[i]==null)return false;}
            for(int i=0;i<sources.Count;i++){var road=roads[sources[i]];road.WireHeads=plans[i];roads[sources[i]]=road;}
            return true;
        }
    }
}
