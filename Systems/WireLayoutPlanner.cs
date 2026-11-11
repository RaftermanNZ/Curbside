using System;
using System.Collections.Generic;
using Curbside.CornerStudy;
using Unity.Entities;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;
namespace MedianGrassReplacement.Systems
{
    internal static class WireLayoutPlanner
    {
        // Three fixed corner stations, only on a network-cache miss. The
        // centre station is preferred; a small move along the same curb can
        // clear an authored head/sign conflict at a skewed junction. Every
        // station preserves the setback and complete physical containment.
        internal static bool Fit(IList<CurbSlice> slices,float envelope,IList<FarSideLightPolicy.Approach> roads,
            IList<int> sources,IList<float3> preferred,byte style,out float3[] supports,out int2[] pairs,out string reason)
        {
            supports=null;pairs=null;reason="incomplete corner geometry";
            foreach(float fraction in new[]{.5f,.45f,.55f})
            {
                var proposals=CornerRule.Calculate(slices,envelope,null,CornerRule.Setback,fraction);
                if(Build(proposals,roads,sources,preferred,style,out supports,out pairs,out reason))return true;
            }
            return false;
        }
        internal static bool Across(float3 point,IList<FarSideLightPolicy.Approach> roads,int source)
        {
            var road=roads[source];float2 centre=road.JunctionCentre.xz;
            if(!road.HasJunctionCentre)
            {
                float xx=0,xy=0,yy=0,bx=0,by=0;centre=float2.zero;
                foreach(var r in roads)
                {
                    var outwards=math.normalizesafe(r.Outward);var normal=new float2(-outwards.y,outwards.x);var mouth=(r.Left.xz+r.Right.xz)*.5f;float projection=math.dot(normal,mouth);
                    xx+=normal.x*normal.x;xy+=normal.x*normal.y;yy+=normal.y*normal.y;bx+=normal.x*projection;by+=normal.y*projection;centre+=mouth;
                }
                float determinant=xx*yy-xy*xy;
                centre=math.abs(determinant)>.0001f?new float2((yy*bx-xy*by)/determinant,(xx*by-xy*bx)/determinant):centre/math.max(1,roads.Count);
            }
            // Physical safety was established on the actual corner strip.
            // The legacy straight mouth-envelope/depth test would reject valid
            // curved corners, so only the across-junction direction is used here.
            return math.all(math.isfinite(point))&&math.all(math.isfinite(centre))&&math.dot(point.xz-centre,-math.normalizesafe(road.Outward))>=.25f;
        }
        internal static bool Build(IList<CornerProposal> proposals,IList<FarSideLightPolicy.Approach> roads,
            IList<int> sources,IList<float3> preferred,byte style,out float3[] supports,out int2[] pairs,out string reason)
        {
            supports=null;pairs=null;reason="incomplete corner geometry";
            if(sources.Count==0||sources.Count>proposals.Count||proposals.Count>6)return false;
            var points=new List<float3>();foreach(var p in proposals){if(!p.Fits){reason=p.Reason;return false;}points.Add(p.Position);}
            int count=points.Count;var allowed=new bool[sources.Count,count];var cost=new float[sources.Count,count];
            for(int source=0;source<sources.Count;source++)for(int corner=0;corner<count;corner++)
            {
                var road=roads[sources[source]];
                if(!Across(points[corner],roads,sources[source])||
                    !GardenCityLightPolicy.TryWireLayout(road,points[corner],points,style,out var inward,out var heads,out int other))continue;
                if(other<0||math.dot(points[other]-points[corner],inward)<heads[heads.Length-1]+.3f)continue;
                allowed[source,corner]=true;cost[source,corner]=math.distancesq(preferred[source].xz,points[corner].xz);
            }
            int states=1<<count;var best=new float[states];var previous=new int[states];var chosen=new int[states];
            for(int i=1;i<states;i++)best[i]=float.PositiveInfinity;
            for(int mask=0;mask<states;mask++)
            {
                int source=math.countbits((uint)mask);if(source>=sources.Count||float.IsPositiveInfinity(best[mask]))continue;
                for(int corner=0;corner<count;corner++)if((mask&(1<<corner))==0&&allowed[source,corner])
                {int next=mask|(1<<corner);float value=best[mask]+cost[source,corner];if(value<best[next]){best[next]=value;previous[next]=mask;chosen[next]=corner;}}
            }
            int finish=-1;float minimum=float.PositiveInfinity;
            for(int mask=0;mask<states;mask++)if(math.countbits((uint)mask)==sources.Count&&best[mask]<minimum){finish=mask;minimum=best[mask];}
            if(finish<0){reason="no complete lane-to-corner assignment";return false;}
            supports=new float3[count];var used=new bool[count];
            for(int source=sources.Count-1;source>=0;source--){int corner=chosen[finish];used[corner]=true;supports[source]=points[corner];finish=previous[finish];}
            int at=sources.Count;for(int corner=0;corner<count;corner++)if(!used[corner])supports[at++]=points[corner];
            var spans=new List<int2>();
            for(int source=0;source<sources.Count;source++)
            {
                var road=roads[sources[source]];
                GardenCityLightPolicy.TryWireLayout(road,supports[source],supports,style,out _,out _,out int other);
                if(other<0){supports=null;reason="assigned corner has no adjacent span";return false;}
                if(!GardenCityLightPolicy.HasRequiredSpan(spans,source,other))spans.Add(new int2(source,other));
            }
            pairs=spans.ToArray();reason="native curb corner layout";return true;
        }
    }
    public partial class FarSideLightSystem
    {
        private bool TryPrepareWireLayout(Entity node,IList<FarSideLightPolicy.Approach> roads)
        {
            if(!signalLayouts.TryGetValue(node,out var entry))return false;
            byte requested=EntityManager.GetComponentData<MGRSignalStyle>(node).Value;
            if(entry.WireAttemptStyle!=requested){entry.Wire=null;entry.WireFailure=null;entry.WireAttemptStyle=requested;}
            if(entry.WireFailure!=null){lastLayoutResult=entry.WireFailure;deferSignalApplication=true;return true;}
            var leaders=new Dictionary<int,Entity>();
            foreach(var prop in _primarySignals)if(_supportLeaders[prop]==prop)
            {
                int source=SourceApproach(prop,roads);if(source<0)continue;
                if(leaders.ContainsKey(source)){lastLayoutResult="waiting for proven native phase support sharing";deferSignalApplication=true;return true;}leaders[source]=prop;
            }
            var sources=new List<int>();for(int i=0;i<roads.Count;i++)if(roads[i].Incoming.Count>0)sources.Add(i);
            foreach(int source in sources)if(!leaders.ContainsKey(source))
            {lastLayoutResult="waiting for native source controllers; previous cables retained";deferSignalApplication=true;return true;}
            if(sources.Count==0){deferSignalApplication=true;return true;}
            if(entry.Wire==null)
            {
                byte style=EntityManager.GetComponentData<MGRSignalStyle>(node).Value;
                float envelope=.4f;
                if(!GardenCityLightPolicy.Ready(World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>(),EntityManager)||
                    (entry.HasCrosswalk&&!GardenCityLightPolicy.PedestrianCornerEnvelope(style,out envelope)))
                {lastLayoutResult="waiting for registered signal module definitions";deferSignalApplication=true;return true;}
                if(!ReadCornerSlices(node,roads,out var slices))
                {entry.WireFailure="native corner surface is unsupported or incomplete; waiting for network change";lastLayoutResult=entry.WireFailure;deferSignalApplication=true;return true;}
                var curves=new List<Colossal.Mathematics.Bezier4x3>();foreach(var slice in slices)curves.Add(slice.Curb);
                for(int i=0;i<roads.Count;i++){var road=roads[i];road.WireBank=road.Incoming;road.WireCurbs=curves;roads[i]=road;entry.Roads[i]=road;}
                var preferred=new List<float3>();
                foreach(int source in sources)
                {
                    var original=OriginalTransform(leaders[source]);
                    if(!FarSideLightPolicy.TrySignalMountFrom(original,roads,source,out var target))target=original;
                    preferred.Add(target.m_Position);
                }
                if(!WireLayoutPlanner.Fit(slices,envelope,roads,sources,preferred,style,out var supports,out var pairs,out var reason))
                {entry.WireFailure=reason;lastLayoutResult=reason;deferSignalApplication=true;return true;}
                if(!GardenCityLightPolicy.CacheWireLayouts(roads,supports,sources,style))
                {lastLayoutResult="wire head layout changed during plan creation";deferSignalApplication=true;return true;}
                for(int i=0;i<roads.Count;i++)entry.Roads[i]=roads[i];
                var plan=new WireCornerPlan{Style=style,Supports=supports,Pairs=pairs};
                for(int i=0;i<sources.Count;i++)plan.RoadSlots[roads[sources[i]].Road]=i;
                entry.Wire=plan;wirePlansBuilt++;lastLayoutResult=reason;
            }
            else{wirePlansReused++;lastLayoutResult="rebound native actors to unchanged corner layout";}
            _supportPositions.AddRange(entry.Wire.Supports);_requiredWireSpans.AddRange(entry.Wire.Pairs);((CornerSupportList)_supportPositions).UsesCornerRule=true;
            _virtualSupport=entry.Wire.Supports.Length>sources.Count?sources.Count:-1;
            foreach(var source in sources)
            {
                if(!entry.Wire.RoadSlots.TryGetValue(roads[source].Road,out int slot)){deferSignalApplication=true;return true;}
                var prop=leaders[source];var placed=OriginalTransform(prop);placed.m_Position=entry.Wire.Supports[slot];_plannedSignals[prop]=placed;_sourceApproaches[prop]=source;
            }
            foreach(var prop in _primarySignals)
            {var leader=_supportLeaders[prop];if(leader!=prop&&_plannedSignals.TryGetValue(leader,out var pose)){_plannedSignals[prop]=pose;_sourceApproaches[prop]=_sourceApproaches[leader];}}
            return true;
        }
    }
}
