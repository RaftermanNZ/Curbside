using System;
using System.Collections.Generic;
using Curbside.CornerStudy;
using Unity.Mathematics;
using Unity.Entities;
using Transform=Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    internal static class ArmCornerLayoutPlanner
    {
        internal static bool Build(IList<CornerProposal> corners,IList<FarSideLightPolicy.Approach> roads,
            IList<int> sources,byte style,out float3[] supports)
        {
            supports=null;
            if(corners==null||sources.Count<1||sources.Count>corners.Count||corners.Count>6)return false;
            foreach(var corner in corners)if(!corner.Fits)return false;
            int count=corners.Count,states=1<<count;
            var allowed=new bool[sources.Count,count];var cost=new float[sources.Count,count];
            for(int i=0;i<sources.Count;i++)for(int c=0;c<count;c++)
            {
                int source=sources[i];if(source<0||source>=roads.Count)return false;
                var road=roads[source];var point=corners[c].Position;
                if(!WireLayoutPlanner.Across(point,roads,source)||!GardenCityLightPolicy.AppearanceCandidate(road,point,style)||
                    !FarSideLightPolicy.ArmCandidate(point,road,null,out float reach))continue;
                allowed[i,c]=true;cost[i,c]=FarSideLightPolicy.ArmBankCost(reach,FarSideLightPolicy.DriverRightBank(point,road));
            }
            var best=new float[states];var previous=new int[states];var chosen=new int[states];
            for(int i=1;i<states;i++)best[i]=float.PositiveInfinity;
            for(int mask=0;mask<states;mask++)
            {
                int bank=math.countbits((uint)mask);if(bank>=sources.Count||float.IsPositiveInfinity(best[mask]))continue;
                for(int c=0;c<count;c++)if((mask&(1<<c))==0&&allowed[bank,c])
                {
                    int next=mask|(1<<c);float value=best[mask]+cost[bank,c];
                    if(value<best[next]){best[next]=value;previous[next]=mask;chosen[next]=c;}
                }
            }
            int finish=-1;float minimum=float.PositiveInfinity;
            for(int mask=0;mask<states;mask++)if(math.countbits((uint)mask)==sources.Count&&best[mask]<minimum){finish=mask;minimum=best[mask];}
            if(finish<0)return false;supports=new float3[sources.Count];
            for(int i=sources.Count-1;i>=0;i--){supports[i]=corners[chosen[finish]].Position;finish=previous[finish];}
            return true;
        }
    }

    public partial class FarSideLightSystem
    {
        private bool TryPrepareArmCornerLayout(Entity node,IList<FarSideLightPolicy.Approach> roads)
        {
            if(!signalLayouts.TryGetValue(node,out var entry))return false;
            byte style=EntityManager.HasComponent<MGRSignalStyle>(node)?EntityManager.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;
            if(entry.ArmAttemptStyle!=style){entry.Arms=null;entry.ArmFailure=false;entry.ArmAttemptStyle=style;}
            if(entry.ArmFailure)return false;
            var leaders=new Dictionary<int,Entity>();
            foreach(var prop in _primarySignals)if(_supportLeaders[prop]==prop)
            {
                int source=SourceApproach(prop,roads);if(source<0)continue;
                // Do not merge controllers without the existing phase-sharing proof.
                if(leaders.ContainsKey(source))return false;leaders[source]=prop;
            }
            var sources=new List<int>();for(int i=0;i<roads.Count;i++)if(roads[i].Incoming.Count>0)sources.Add(i);
            foreach(int source in sources)if(!leaders.ContainsKey(source))return false;
            if(sources.Count==0)return false;
            if(entry.Arms==null||entry.Arms.Style!=style)
            {
                var prefabs=World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
                string asset=style==4?GardenCityLightPolicy.FloridaAsset:style==2?GardenCityLightPolicy.MastAsset:GardenCityLightPolicy.AssetName;
                float envelope=.4f;
                if(!GardenCityLightPolicy.ReadyStyle(prefabs,EntityManager,asset)||
                    entry.HasCrosswalk&&!GardenCityLightPolicy.PedestrianCornerEnvelope(style,out envelope))return false;
                if(!ReadCornerSlices(node,roads,out var slices)){entry.ArmFailure=true;return false;}
                var proposals=CornerRule.Calculate(slices,envelope);
                if(!ArmCornerLayoutPlanner.Build(proposals,roads,sources,style,out var supports)){entry.ArmFailure=true;return false;}
                var plan=new WireCornerPlan{Style=style,Supports=supports,Pairs=Array.Empty<int2>()};
                for(int i=0;i<sources.Count;i++)plan.RoadSlots[roads[sources[i]].Road]=i;
                entry.Arms=plan;
            }
            // Rebind replacement native actors to the same physical layout.
            // No new actor, controller, lane mask or native owner buffer is made.
            foreach(int source in sources)
            {
                if(!entry.Arms.RoadSlots.TryGetValue(roads[source].Road,out int slot))return false;
                var prop=leaders[source];var placed=OriginalTransform(prop);placed.m_Position=entry.Arms.Supports[slot];
                _plannedSignals[prop]=placed;_sourceApproaches[prop]=source;_supportPositions.Add(placed.m_Position);
            }
            foreach(var prop in _primarySignals)
            {
                var leader=_supportLeaders[prop];
                if(leader!=prop&&_plannedSignals.TryGetValue(leader,out var placed))
                {_plannedSignals[prop]=placed;_sourceApproaches[prop]=_sourceApproaches[leader];}
            }
            lastLayoutResult="native curved corner arm layout";
            return true;
        }
    }
}
