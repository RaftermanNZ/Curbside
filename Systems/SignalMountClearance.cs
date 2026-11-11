using System;
namespace GardenCitySignals
{
    internal static class SignalMountClearance
    {
        // Geometry clearance for the approved sign and rear support rail.
        // This rejects an unsupported layout; no source position is adjusted.
        internal const float MinimumNonRigidHeadAlong = 2.1f;
        internal static bool AllowHead(byte style, bool updatedPack, float distance)
        {
            if (!updatedPack || style == 2) return true;
            return !float.IsNaN(distance) && !float.IsInfinity(distance) && distance >= MinimumNonRigidHeadAlong - .001f;
        }
    }
}
