namespace MedianGrassReplacement.Systems
{
    internal static class SignalLaneBindingPolicy
    {
        internal static bool Physical(bool master,bool secondary)=>!master&&!secondary;
    }
}
