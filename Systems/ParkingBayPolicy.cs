using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class ParkingBayPolicy

    {

        internal static float Length(float value) => math.isfinite(value) ? math.clamp(value, 7f, 8f) : 7.5f;

        // Native count calculation adds 0.01 m before flooring. Include a further

        // centimetre so a just-too-short lane cannot fit a bay below our 7 m minimum.

        internal static float NativeInterval(float value) => Length(value) + .02f;

    }

}
