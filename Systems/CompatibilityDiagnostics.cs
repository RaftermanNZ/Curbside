using System;
using System.Collections.Generic;
using System.Reflection;

namespace MedianGrassReplacement.Systems
{
    internal static class CompatibilityDiagnostics
    {
        // Explicit diagnostic snapshots only; no startup polling or city/entity scan.
        internal static void Record()
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var versions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var identity = assembly.GetName();
                string name = identity.Name ?? "", version = identity.Version?.ToString() ?? "unknown";
                if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal)
                    || name.StartsWith("Unity", StringComparison.Ordinal) || name.StartsWith("Colossal", StringComparison.Ordinal)
                    || name == "Game" || name.StartsWith("Mono.", StringComparison.Ordinal)) continue;
                names.Add(name + " " + version);
                if (!versions.TryGetValue(name, out var loaded)) versions[name] = loaded = new HashSet<string>(StringComparer.Ordinal);
                loaded.Add(version);
            }
            Mod.Log.Info("Compatibility snapshot: loaded managed assemblies only; this does not identify enabled features or certify compatibility. Asset-only packs do not appear here.");
            foreach (string name in names) Mod.Log.Info("Compatibility assembly: " + name);
            foreach (var pair in versions) if (pair.Value.Count > 1)
                Mod.Log.Warn("Compatibility snapshot: multiple loaded versions of " + pair.Key + ": " + string.Join(", ", pair.Value));
        }
    }
}
