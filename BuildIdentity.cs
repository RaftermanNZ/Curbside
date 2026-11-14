using System.Reflection;

namespace MedianGrassReplacement
{
    internal static class BuildIdentity
    {
        // Asset manifests use this monotonically increasing compatibility level.
        // It must remain independent of the public major.minor.patch version.
        internal const int Number = 243;
        internal static readonly string Version =
            (typeof(Mod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.6.0").Split('+')[0];
        internal static string Label => Version;
    }
}
