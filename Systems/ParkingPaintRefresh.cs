using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal enum PaintRefreshReason { Required, Observed, Parking }

    // Explicit edits/recovery dominate observations in either arrival order.
    // An observed notification may avoid forwarding only after EXACT output and
    // source-ownership equality. A source change remains independently admitted.
    internal sealed class ParkingPaintRefreshReasons
    {
        private readonly Dictionary<Entity, PaintRefreshReason> reasons = new Dictionary<Entity, PaintRefreshReason>();
        internal void Parking(Entity road) { if (!reasons.ContainsKey(road)) reasons.Add(road, PaintRefreshReason.Parking); }
        internal void Observe(Entity road)
        {
            if (!reasons.TryGetValue(road, out var value) || value == PaintRefreshReason.Parking)
                reasons[road] = PaintRefreshReason.Observed;
        }
        internal void Require(Entity road) { reasons[road] = PaintRefreshReason.Required; }
        internal PaintRefreshReason TakeReason(Entity road)
        {
            var reason = reasons.TryGetValue(road, out var value) ? value : PaintRefreshReason.Required;
            reasons.Remove(road); return reason;
        }
        internal bool Take(Entity road) => TakeReason(road) == PaintRefreshReason.Parking;
        internal void Remove(Entity road) { reasons.Remove(road); }
        internal void Clear() { reasons.Clear(); }
    }

    public partial class CurbLineEmissionSystem
    {
        private static readonly HashSet<Entity> _externalParkingRoads = new HashSet<Entity>();
        private readonly ParkingPaintRefreshReasons _paintRefreshReasons = new ParkingPaintRefreshReasons();
        internal static void QueueParkingRoad(Entity road) { _externalParkingRoads.Add(road); }

        private static bool SamePaintOwnership(List<MarkCurve> previous, List<MarkCurve> current)
        {
            if (previous.Count != current.Count) return false;
            for (int i = 0; i < previous.Count; i++)
            {
                var a = previous[i]; var b = current[i];
                if (!math.all(a.Curve.a == b.Curve.a) || !math.all(a.Curve.b == b.Curve.b)
                    || !math.all(a.Curve.c == b.Curve.c) || !math.all(a.Curve.d == b.Curve.d)
                    || a.Prefab != b.Prefab || a.NativeSource != b.NativeSource || !a.Width.Equals(b.Width)
                    || a.Median != b.Median || a.Curb != b.Curb || a.CenterUpgrade != b.CenterUpgrade
                    || a.DividerUpgrade != b.DividerUpgrade || a.Protected != b.Protected) return false;
            }
            return true;
        }
    }
}
