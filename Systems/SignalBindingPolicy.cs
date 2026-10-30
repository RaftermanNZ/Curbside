namespace MedianGrassReplacement.Systems
{
    internal static class SignalBindingPolicy
    {
        internal static bool Covered(ushort requiredVehicle,ushort requiredPedestrian,bool needsVehicle,bool needsPedestrian,ushort visibleVehicle,ushort visiblePedestrian)
            => (!needsVehicle||requiredVehicle!=0&&(visibleVehicle&requiredVehicle)==requiredVehicle)
            && (!needsPedestrian||requiredPedestrian!=0&&(visiblePedestrian&requiredPedestrian)==requiredPedestrian);
        // Group membership changes when native initialization or an optional phase
        // controller reassigns movements. Current lamp colors are deliberately absent.
        internal static bool Changed(ushort vehicle, ushort pedestrian, ushort appliedVehicle, ushort appliedPedestrian)
            => vehicle != appliedVehicle || pedestrian != appliedPedestrian;
    }
}
