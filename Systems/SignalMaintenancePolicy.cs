namespace MedianGrassReplacement.Systems
{
    internal static class SignalMaintenancePolicy
    {
        internal static byte State(bool enabled, bool custom) => (byte)(enabled ? custom ? 3 : 1 : 0);
        internal static bool KeepCustom(byte state) => state == 3;
        internal static bool KeepMoved(bool live, byte state, bool nativePrefab) => live && (state & 1) != 0 && ((state & 2) != 0 || nativePrefab);
    }
}
