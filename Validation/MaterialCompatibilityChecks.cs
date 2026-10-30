using System;
using System.Reflection;

class MaterialCompatibilityChecks
{
    static int count;
    static readonly Assembly A = Assembly.LoadFrom("MedianGrassReplacement.dll");
    static readonly Type Policy = A.GetType("MedianGrassReplacement.Systems.MaterialOwnership", true);
    static T Invoke<T>(string name, params object[] args) => (T)Policy.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(typeof(T)).Invoke(null, args);
    static bool CanRestore<T>(bool owned, T current, T applied) => (bool)Policy.GetMethod("CanRestore", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(typeof(T)).Invoke(null, new object[] { owned, current, applied });
    static void Check(bool value, string reason) { count++; if (!value) throw new Exception(reason); }
    static void Main()
    {
        // A is the original channel, Curbside writes B, another controller writes C.
        // Reapplying B must retain C as the next restoration target, independently
        // for albedo, normal, mask, color and UV channels.
        foreach (string channel in new[] { "albedo", "normal", "mask", "color", "uv" })
        {
            foreach (bool owned in new[] { false, true })
                foreach (string current in new[] { "A", "B", "C", null })
                {
                    string baseline = Invoke<string>("Baseline", owned, current, "B", "A");
                    Check(baseline == (owned && current == "B" ? "A" : current), channel + " lost external baseline");
                    Check(CanRestore(owned, current, "B") == (owned && current == "B"), channel + " overwrote an external edit");
                }
            string before = Invoke<string>("Baseline", false, "A", null, "A");
            before = Invoke<string>("Baseline", true, "B", "B", before);
            Check(before == "A", channel + " captured our own replacement as baseline");
            Check(!CanRestore(true, "C", "B"), channel + " restored over foreign change");
            before = Invoke<string>("Baseline", false, "C", "B", before);
            Check(before == "C" && CanRestore(true, "B", "B"), channel + " reapply then disable lost C");
        }
        Check(Invoke<float>("Baseline", true, .3f, .2f, .1f) == .3f, "external tiling lost");
        Check(Invoke<float>("Baseline", true, .2f, .2f, .1f) == .1f, "owned tiling baseline drifted");
        var paint = A.GetType("MedianGrassReplacement.Systems.ExternalPaintPolicy", true).GetMethod("IsUserPaint", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (string name in new[] { "TownRoadLane.Components.TRLSegmentLink", "TownRoadLane.Components.TRLAreaLink", "TownRoadLane.Components.MarkingData" })
            Check((bool)paint.Invoke(null, new object[] { name }), "foreign paint identity not protected");
        foreach (string name in new[] { null, "Game.Net.Lane", "OtherMod.MarkingData", "TownRoadLane.Components.Unrelated" })
            Check(!(bool)paint.Invoke(null, new object[] { name }), "unrelated native paint excluded");
        Console.WriteLine("PASS: " + count + " material/paint compatibility checks: per-channel takeover, reapply, restore, UV and foreign paint identities. Live mod combinations remain unverified.");
    }
}
