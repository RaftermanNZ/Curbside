using System;
using System.Reflection;
namespace MedianGrassReplacement
{
    internal static class BuildIdentity
    {
        internal static readonly string Version=(typeof(Mod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"0.0.0-test.0").Split(new[]{'+'})[0];
        internal static readonly int Number=ReadNumber();
        internal static string Label=>"Test"+Number;
        private static int ReadNumber(){var parts=Version.Split(new[]{'.'});return int.TryParse(parts[parts.Length-1],out var value)?value:0;}
    }
}
