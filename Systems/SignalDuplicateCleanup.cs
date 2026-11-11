using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using ObjectChild = Game.Objects.SubObject;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalMedianApproachPolicy
    {
        // Median signals can be more than 12 m from an outside curb. Their
        // native approach is established at its road mouth, not by guessing
        // a nearby sidewalk. Ambiguous/unmapped phases are left intact.
        internal static int Find(float3 source,ushort mask,IList<FarSideLightPolicy.Approach> roads)
        {
            if(mask==0 || roads==null || !math.all(math.isfinite(source)))return -1;
            int winner=-1;float best=float.MaxValue,second=float.MaxValue;
            for(int i=0;i<roads.Count;i++)
            {
                var road=roads[i];
                if(road.Incoming==null || road.Incoming.Count==0 || road.LaneMasks==null ||
                    road.Incoming.Count!=road.LaneMasks.Count || !SignalSupportGroupPolicy.FullyMapped(mask,road.LaneMasks) ||
                    !math.all(math.isfinite(road.Left)) || !math.all(math.isfinite(road.Right)) ||
                    !math.all(math.isfinite(road.Outward)) || math.lengthsq(road.Outward)<.5f)continue;
                var mouth=(road.Left+road.Right)*.5f;var offset=source.xz-mouth.xz;
                var direction=math.normalizesafe(road.Outward);float along=math.abs(math.dot(offset,direction));
                if(along>3||math.abs(source.y-mouth.y)>2)continue;
                float lateral=math.abs(math.dot(offset,new float2(-direction.y,direction.x)));
                if(lateral>math.distance(road.Left.xz,road.Right.xz)*.5f+.5f)continue;
                if(along<best){second=best;best=along;winner=i;}else second=math.min(second,along);
            }
            return second-best>.5f?winner:-1;
        }
    }
    public partial class FarSideLightSystem
    {
        private void NodeDuplicateSignals(Entity node,IList<FarSideLightPolicy.Approach> approaches)
        {
            if(!EnabledNode(node)||!HasSignalChildren(node))return;
            var coverage=ReadSignalCoverage(node,approaches);
            CopySignalChildren(node,signalDuplicateChildren);
            foreach(var actor in signalDuplicateChildren)
            {
                if(!SignalProp(actor,node)||_moved.ContainsKey(actor)||_plannedSignals.ContainsKey(actor))continue;
                bool hiddenHere=_hiddenSignals.TryGetValue(actor,out var hidden)&&hidden==node;
                bool covered=CoveredVehiclePhase(actor,node,approaches,coverage);
                if(covered)HideSignal(actor,node);else if(hiddenHere)ReleaseHiddenSignal(actor);
            }
        }
    }
}
