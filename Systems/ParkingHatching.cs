using System.Collections.Generic;using Colossal.Mathematics;using Colossal.Serialization.Entities;
using Game.Common;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRParkingHatching:IComponentData,ISerializable
    {
        public byte Sides;
        public void Serialize<T>(T w)where T:IWriter=>w.Write(Sides);
        public void Deserialize<T>(T r)where T:IReader=>r.Read(out Sides);
    }
    internal static class ParkingHatchPolicy
    {
        internal const string AssetName="MGR Parking End Hatching",Yellow="MGR Yellow Parking End Hatching";
        internal static byte Select(byte mask,bool left,bool remove,bool yellow){byte side=RoadSideUpgrades.SideMask(left),color=(byte)(side<<4);mask=(byte)(mask&~color);mask=remove?(byte)(mask&~side):(byte)(mask|side);if(!remove&&yellow)mask|=color;return mask;}
        internal static List<Bezier4x3> Build(Bezier4x3 center,float width,float lower,float upper)
        {
            var result=new List<Bezier4x3>();
            if(!math.isfinite(width)||width<.5f||width>6||!math.isfinite(lower)||!math.isfinite(upper)||lower<0||upper>1||upper<=lower)return result;
            var path=MathUtils.Cut(center,new float2(lower,upper));float length=MathUtils.Length(path);if(!math.isfinite(length)||length<math.max(2f,width*1.5f)||length>100)return result;
            width=math.max(.2f,width-.7f);var left=NetUtils.OffsetCurveLeftSmooth(path,-width*.5f);var right=NetUtils.OffsetCurveLeftSmooth(path,width*.5f);
            result.Add(left);result.Add(right);result.Add(Line(left.a,right.a));result.Add(Line(left.d,right.d));
            for(float s=-width;s<length&&result.Count<132;s+=1f)
            {
                float first=math.max(0,s),last=math.min(length,s+width);if(last-first<.1f)continue;
                float ta=ParkingMarkPlacement.SlotParameter(path,first),tb=ParkingMarkPlacement.SlotParameter(path,last);
                float acrossA=(first-s)/width-.5f,acrossB=(last-s)/width-.5f;
                var a=MathUtils.Position(path,ta);var b=MathUtils.Position(path,tb);
                var na=MathUtils.Left(math.normalizesafe(MathUtils.Tangent(path,ta).xz));var nb=MathUtils.Left(math.normalizesafe(MathUtils.Tangent(path,tb).xz));
                a+=new float3(na.x,0,na.y)*acrossA*width;b+=new float3(nb.x,0,nb.y)*acrossB*width;
                if(math.all(math.isfinite(a))&&math.all(math.isfinite(b)))result.Add(Line(a,b));
            }return result;
        }
        private static Bezier4x3 Line(float3 a,float3 b)=>new Bezier4x3(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b);
    }
    public partial class CurbLineEmissionSystem
    {
        private bool BuildingAccess(Entity entity,bool commercialOnly=false)
        {
            for(int i=0;i<8&&EntityManager.Exists(entity);i++)
            {
                if(EntityManager.HasComponent<Game.Buildings.Building>(entity))return !commercialOnly||EntityManager.HasComponent<Game.Buildings.CommercialProperty>(entity);
                if(!EntityManager.HasComponent<Owner>(entity))return false;
                var parent=EntityManager.GetComponentData<Owner>(entity).m_Owner;if(parent==entity)return false;entity=parent;
            }return false;
        }
        private bool AccessBlocked(Entity lane,float lower,float upper,bool commercialOnly=false)
        {
            if(!EntityManager.HasBuffer<LaneOverlap>(lane))return false;
            foreach(var overlap in EntityManager.GetBuffer<LaneOverlap>(lane,true))if(overlap.m_ThisStart/255f<upper&&overlap.m_ThisEnd/255f>lower&&BuildingAccess(overlap.m_Other,commercialOnly))return true;
            return false;
        }
        private bool ParkingPaintAvailable(Entity road,Entity lane,Bezier4x3 path,float lower,float upper,bool usable,float first,float last)
        {
            bool access=AccessBlocked(lane,lower,upper);
            if(access)
            {
                bool hatched=false;
                if(Mod.Settings.HatchAtBuildingEntrances&&AccessBlocked(lane,lower,upper,true)&&EntityManager.HasComponent<MGRParkingHatching>(road))
                {bool left=RoadSideUpgrades.IsLeft(EntityManager,road,MathUtils.Position(path,.5f));hatched=(EntityManager.GetComponentData<MGRParkingHatching>(road).Sides&RoadSideUpgrades.SideMask(left))!=0;}
                return !Mod.Settings.CloseParkingAtBuildingEntrances&&!hatched;
            }
            if(!ParkingBlocked(lane,lower,upper))return true;
            return usable&&lower>=first&&upper<=last;
        }
        internal List<Bezier4x3> ParkingHatchPlan(Entity road,bool left)
        {
            var output=new List<Bezier4x3>();if(!EntityManager.HasBuffer<Game.Net.SubLane>(road))return output;
            int lanes=0;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
            {
                if(++lanes>256)break;var lane=entry.m_SubLane;
                if(!EntityManager.HasComponent<Game.Net.ParkingLane>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                var curve=EntityManager.GetComponentData<Curve>(lane);if(RoadSideUpgrades.IsLeft(EntityManager,road,MathUtils.Position(curve.m_Bezier,.5f))!=left)continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;if(!EntityManager.HasComponent<ParkingLaneData>(prefab)||!EntityManager.HasComponent<NetLaneData>(prefab))continue;
                var data=EntityManager.GetComponentData<ParkingLaneData>(prefab);if(math.abs(data.m_SlotAngle)>.25f)continue;
                var parking=EntityManager.GetComponentData<Game.Net.ParkingLane>(lane);if((parking.m_Flags&(ParkingLaneFlags.VirtualLane|ParkingLaneFlags.SpecialVehicles))!=0)continue;
                int count=NetUtils.GetParkingSlotCount(curve,parking,data);if(count<1||count>512)continue;
                float interval=NetUtils.GetParkingSlotInterval(curve,parking,data,count),offset=(curve.m_Length-count*interval)*.5f;
                if((parking.m_Flags&ParkingLaneFlags.StartingLane)!=0)offset=curve.m_Length-count*interval;else if((parking.m_Flags&ParkingLaneFlags.EndingLane)!=0)offset=0;
                var ranges=new List<float2>();bool nearStart=ParkingClosesAt(road,curve.m_Bezier.a),nearEnd=ParkingClosesAt(road,curve.m_Bezier.d);
                GetStopRange(curve.m_Bezier,ReadStopMarks(road),LaneForward(road,lane,curve.m_Bezier),out float lower,out float upper);
                bool usable=ParkingSetback.TryLimits(curve.m_Bezier,lower,upper,nearStart,nearEnd,Mod.Settings.ParkingIntersectionSetback,out float first,out float last);
                // Exact setback length, independent of the native bay grid.
                // Native parking availability is unchanged; paint is never cut to fit hatches.
                if(usable&&Mod.Settings.ParkingIntersectionSetback>0)
                {
                    if(EntityManager.HasBuffer<LaneOverlap>(lane))foreach(var overlap in EntityManager.GetBuffer<LaneOverlap>(lane,true))
                    {
                        var other=overlap.m_Other;
                        if(!EntityManager.Exists(other)||EntityManager.HasComponent<Deleted>(other)||BuildingAccess(other)||(!EntityManager.HasComponent<Game.Net.CarLane>(other)&&!EntityManager.HasComponent<Game.Net.TrackLane>(other)))continue;
                        float a=math.max(lower,overlap.m_ThisStart/255f),b=math.min(upper,overlap.m_ThisEnd/255f);
                        if(nearStart&&math.min(first,b)>a)ranges.Add(new float2(a,math.min(first,b)));
                        if(nearEnd&&b>math.max(last,a))ranges.Add(new float2(math.max(last,a),b));
                    }
                }
                if(Mod.Settings.HatchAtBuildingEntrances)
                    for(int i=0;i<count;i++)
                    {
                        float a=ParkingMarkPlacement.SlotParameter(curve.m_Bezier,math.max(0,offset+i*interval)),b=ParkingMarkPlacement.SlotParameter(curve.m_Bezier,math.min(curve.m_Length,offset+(i+1)*interval));
                        if(ParkingBlocked(lane,a,b)&&AccessBlocked(lane,a,b,true)&&b>a)ranges.Add(new float2(a,b));
                    }
                var path=curve.m_Bezier;path.a.y+=.015f;path.b.y+=.015f;path.c.y+=.015f;path.d.y+=.015f;
                float nativeWidth=EntityManager.GetComponentData<NetLaneData>(prefab).m_Width;
                var priorityStops=ReadStopMarks(road);
                foreach(var range in NorthAmericanLinePolicy.Merge(ranges))
                {
                    float a=math.max(lower,range.x),b=math.min(upper,range.y);var strokes=ParkingHatchPolicy.Build(path,nativeWidth,a,b);
                    foreach(var stop in priorityStops)
                    {
                        float width=EntityManager.HasComponent<NetLaneData>(stop.Prefab)?math.max(.1f,EntityManager.GetComponentData<NetLaneData>(stop.Prefab).m_Width):.3f;
                        var remaining=new List<Bezier4x3>();foreach(var stroke in strokes)remaining.AddRange(StopPaintPriority.Exclude(stroke,stop.Curve,width*.5f+.10f,math.distance(stop.Curve.a.xz,stop.Curve.d.xz)*.5f+width*.5f));strokes=remaining;
                    }
                    output.AddRange(strokes);

                }
            }return output;
        }
        internal void ApplyHatching(Entity road,bool left,bool remove,bool yellow=false)
        {
            if(Mod.Saving)return;
            bool waiting=!remove&&ParkingHatchPlan(road,left).Count==0;
            if(!remove&&(!RoadSurfacePolicy.IsPaved(EntityManager,road)||!EntityManager.HasBuffer<Game.Net.SubLane>(road))){UpgradeFeedback.Report(ParkingHatchPolicy.AssetName,"Select a paved road with native parking geometry.");return;}
            World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
            byte mask=EntityManager.HasComponent<MGRParkingHatching>(road)?EntityManager.GetComponentData<MGRParkingHatching>(road).Sides:(byte)0;
            mask=ParkingHatchPolicy.Select(mask,left,remove,yellow);
            if(mask==0){if(EntityManager.HasComponent<MGRParkingHatching>(road))EntityManager.RemoveComponent<MGRParkingHatching>(road);}
            else {if(!EntityManager.HasComponent<MGRParkingHatching>(road))EntityManager.AddComponent<MGRParkingHatching>(road);EntityManager.SetComponentData(road,new MGRParkingHatching{Sides=mask});}
            QueueRoad(road);UpgradeFeedback.Report(ParkingHatchPolicy.AssetName,remove?"Hatching removed on this side; parking kept.":waiting?"Hatching requested. No eligible hatch space yet. Uses Parking setback length; short areas are skipped. Request retained; parking kept.":"Hatching applied on this side; parking behavior kept.");
        }
        private void AddParkingHatching(Entity road,List<MarkCurve> output)
        {
            if(!EntityManager.HasComponent<MGRParkingHatching>(road))return;
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            byte sides=EntityManager.GetComponentData<MGRParkingHatching>(road).Sides;
            foreach(bool left in new[]{true,false})if((sides&RoadSideUpgrades.SideMask(left))!=0)
                foreach(var path in ParkingHatchPlan(road,left)){var asset=palette.Explicit(1,(sides&(RoadSideUpgrades.SideMask(left)<<4))!=0);if(asset==Entity.Null)continue;var mark=new MarkCurve(path,asset,0);mark.Protected=true;output.Add(mark);}
        }
    }
}
