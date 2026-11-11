namespace GardenCitySignals
{
    // Local R15 geometry keeps the signal heads fixed and moves only the
    // rigid-style sign. Other styles and legacy packs keep their placement.
    internal static class SignalSignPlacement
    {
        internal const string Layout="RigidNearPoleR15";
        internal const string ClearanceLayout="RigidSignClearanceR23";
        internal static float Along(byte style,bool updated)=>updated&&style==2?1.20f:1.05f;
        internal static float Height(byte style,bool updated)=>updated&&style==2?(SignalPackPolicy.UseLowerRigidSignLayout?5.52f:5.77f):5.95f;
    }
}
