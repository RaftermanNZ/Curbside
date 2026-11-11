using System;
using System.Reflection;

class MastSnapshotChecks219
{
    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var type = assembly.GetType("MedianGrassReplacement.Systems.SignalMastSnapshotPolicy", true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var meshIndex = type.GetMethod("MeshIndex", flags);
        var requirements = type.GetMethod("Requirements", flags);
        var pole = type.GetMethod("Pole", flags);
        int checks = 0;
        void Check(bool condition, string why) { checks++; if (!condition) throw new Exception(why); }
        foreach (string name in new[] { "CSGMetalWirePole Mesh", "CSGWirePole Mesh", "CSGWireTimber Mesh", "csgwirepole mesh" })
            Check((bool)pole.Invoke(null, new object[] { name }), "wire pole classification failed");
        foreach (string name in new[] { "CSGHeadWire Mesh", "CSGWireLuminaire Mesh", "CSGPedHeadWire Mesh", "", null })
            Check(!(bool)pole.Invoke(null, new object[] { name }), "head/lamp misclassified as mast");
        for (int count = 0; count <= 16; count++)
            for (int first = -2; first <= count + 2; first++)
                for (int last = -2; last <= count + 2; last++)
                    for (int local = -2; local <= count + 2; local++)
                        foreach (bool grouped in new[] { false, true })
                        {
                            int expected = -1;
                            if (local >= 0)
                            {
                                if (!grouped && local < count) expected = local;
                                else if (grouped && first >= 0 && last >= first && last <= count && first + local < last) expected = first + local;
                            }
                            int decoded = (int)meshIndex.Invoke(null, new object[] { local, count, grouped, first, last });
                            Check(decoded == expected, "invalid or partial mesh-group range decoded incorrectly");
                        }
        // Mirror the native renderer's per-part bit subset predicate. A pole
        // can exist in the prefab yet be excluded, independently of its heads.
        for (uint required = 0; required < 64; required++)
            for (uint available = 0; available < 64; available++)
                for (uint locale = 0; locale < 4; locale++)
                    for (uint current = 0; current < 4; current++)
                    {
                        bool expected = (required & ~available) == 0 && (locale & ~current) == 0;
                        Check((bool)requirements.Invoke(null, new object[] { required, available, locale, current }) == expected, "native per-part predicate changed");
                    }
        Check(!(bool)requirements.Invoke(null, new object[] { 4u, 0u, 0u, 0u }) &&
              (bool)requirements.Invoke(null, new object[] { 0u, 0u, 0u, 0u }), "part-only exclusion example lost");
        Console.WriteLine("PASS: " + checks + " compiled snapshot decoding/classification checks. Native draw-state examples only; no missing-mast runtime reproduction or fix claimed.");
    }
}
