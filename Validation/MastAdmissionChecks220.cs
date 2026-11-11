using System;
using System.Reflection;
class MastAdmissionChecks220
{
    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var method = assembly.GetType("MedianGrassReplacement.Systems.SignalMastSnapshotPolicy", true)
            .GetMethod("NormalLayersAllowed", BindingFlags.Static | BindingFlags.NonPublic);
        int checks = 0;
        for (int mask = 0; mask < 16; mask++)
        {
            bool overridden = (mask & 1) != 0, marker = (mask & 2) != 0, outside = (mask & 4) != 0, editor = (mask & 8) != 0;
            // Exact static-object gate from installed PreCullingSystem,
            // InitializeCullingJob.Execute, lines730-735 of fresh readback.
            bool expected = !overridden && (!(marker && !outside) || editor);
            bool actual = (bool)method.Invoke(null, new object[] { overridden, marker, outside, editor });
            checks++;
            if (actual != expected) throw new Exception("Native admission predicate mismatch: " + mask);
        }
        Console.WriteLine("PASS: " + checks + " native visibility-admission decisions. Scalar rule only; actual missing actors' native tags remain unmeasured until a live snapshot.");
    }
}
