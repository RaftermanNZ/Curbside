using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        internal sealed class ParkingStopSnapshot
        {
            private readonly List<StopMark> stops;
            internal readonly bool Reusable;
            private ParkingStopSnapshot(List<StopMark> values, bool reusable)
            { stops = values; Reusable = reusable; }
            internal static ParkingStopSnapshot Capture(CurbLineEmissionSystem source, Entity road)
            {
                var values = source.ReadStopMarks(road);
                bool reusable = values.Count <= 256;
                // Only parking-data definitions can be replaced by ApplyBays.
                // Keep fresh stop discovery if a foreign hybrid uses such a
                // definition as stop paint; ordinary native paint is immutable
                // during this parking pass. RefreshDefinitions ran before capture.
                // Check the whole stop-definition set, including a currently
                // absent stop restored from a private parking copy later in
                // this same pass. Looking only at captured stops misses that
                // introduction case.
                foreach (var prefab in source._stopPrefabs)
                    if (source.EntityManager.HasComponent<ParkingLaneData>(prefab)) { reusable = false; break; }
                return new ParkingStopSnapshot(values, reusable);
            }
            internal void Range(Bezier4x3 curve, bool forward, out float lower, out float upper)
                => GetStopRange(curve, stops, forward, out lower, out upper);
        }
        internal ParkingStopSnapshot CaptureParkingStops(Entity road) => ParkingStopSnapshot.Capture(this, road);
    }
}
