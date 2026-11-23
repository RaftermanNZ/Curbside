using System.Collections.Generic;using Colossal.Mathematics;using Unity.Entities;using Unity.Mathematics;using Game.Common;using Game.Net;using Game.Prefabs;
namespace MedianGrassReplacement.Systems { public partial class CurbLineEmissionSystem {
        private List<Bezier4x3> ReadNativeCornerGuides(Entity node)
        {
            var guides = nativeCornerGuides; guides.Clear();
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(node)) return guides;
            int inspected=0;
            foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                if(++inspected>256)break;var lane=item.m_SubLane;
                if(!EntityManager.HasComponent<Game.Net.PedestrianLane>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Deleted>(lane))continue;
                if((EntityManager.GetComponentData<Game.Net.PedestrianLane>(lane).m_Flags&PedestrianLaneFlags.Crosswalk)!=0)continue;
                guides.Add(EntityManager.GetComponentData<Curve>(lane).m_Bezier);
            }
            return guides;
        }
        private bool TryNativeCorner(List<Bezier4x3> guides,JoinCap a,JoinCap b,out Bezier4x3 result)
        {
            result=default;float best=4.5f;bool found=false;
            foreach(var originalGuide in guides)
            {
                var guide=originalGuide;
                if(math.distance(guide.d,a.Point)+math.distance(guide.a,b.Point)<math.distance(guide.a,a.Point)+math.distance(guide.d,b.Point))
                    guide=new Bezier4x3(guide.d,guide.c,guide.b,guide.a);
                // Endpoints come from composed pavement/curb anchors. A grass strip
                // changes their distance to the walking guide without changing lane width.
                var unit=NetUtils.OffsetCurveLeftSmooth(guide,1);
                if(!JunctionEdgePolicy.TryGuideOffsets(guide,unit,a.Point,b.Point,out float startOffset,out float endOffset))continue;
                var start=NetUtils.OffsetCurveLeftSmooth(guide,startOffset);
                var end=NetUtils.OffsetCurveLeftSmooth(guide,endOffset);
                var candidate=new Bezier4x3(start.a,math.lerp(start.b,end.b,1f/3),math.lerp(start.c,end.c,2f/3),end.d);
                float score=math.distance(candidate.a,a.Point)+math.distance(candidate.d,b.Point);
                if(score>=best||math.distance(candidate.a,a.Point)>2.25f||math.distance(candidate.d,b.Point)>2.25f)continue;
                if(math.dot(math.normalizesafe(MathUtils.Tangent(candidate,0)),a.Outward)<.7f||math.dot(math.normalizesafe(MathUtils.Tangent(candidate,1)),-b.Outward)<.7f)continue;
                var first=a.Point-candidate.a;var last=b.Point-candidate.d;
                candidate=new Bezier4x3(a.Point,candidate.b+math.lerp(first,last,1f/3),candidate.c+math.lerp(first,last,2f/3),b.Point);
                float clearance=math.min(math.abs(startOffset),math.abs(endOffset));
                if(!CornerClearanceCache.Shared.Clear(guide,candidate,clearance)||!Valid(candidate))continue;best=score;result=candidate;found=true;
            }return found;
        }

        private readonly List<Bezier4x3> nativeCornerGuides = new List<Bezier4x3>(256);
}}
