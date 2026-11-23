using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FixedParkingSystem
    {
        private sealed class ParkingRoadInputs
        {
            internal Entity Blocker;
            internal bool StartJunction, EndJunction, HasCurve;
            internal Curve Curve;
            internal CurbLineEmissionSystem.ParkingStopSnapshot Stops;
        }
        private readonly Dictionary<Entity, ParkingRoadInputs> parkingRoadInputs = new Dictionary<Entity, ParkingRoadInputs>();

        private ParkingRoadInputs ReadParkingRoadInputs(Entity road)
        {
            if (parkingRoadInputs.TryGetValue(road, out var known)) return known;
            var input = new ParkingRoadInputs();
            foreach (var entry in EntityManager.GetBuffer<SubLane>(road, true))
            {
                var other = entry.m_SubLane;
                if (EntityManager.HasComponent<Deleted>(other)) continue;
                if (input.Blocker == Entity.Null && EntityManager.HasComponent<CarLane>(other)) input.Blocker = other;
            }
            if (input.Blocker != Entity.Null)
            {
                var edge = EntityManager.GetComponentData<Edge>(road);
                input.StartJunction = Junction(edge.m_Start);
                input.EndJunction = Junction(edge.m_End);
                input.HasCurve = EntityManager.HasComponent<Curve>(road);
                if (input.HasCurve) input.Curve = EntityManager.GetComponentData<Curve>(road);
            }
            // ApplyBays writes parking references/overlaps only, never this road
            // membership/topology/curve. Retain copied inputs for this pass only.
            if (parkingRoadInputs.Count < 512) parkingRoadInputs.Add(road, input);
            return input;
        }
    }
}
