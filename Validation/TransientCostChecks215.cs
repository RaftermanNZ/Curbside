using System;
using System.Reflection;

class TransientCostChecks215
{
    static int frameReads;
    static int Frame() { frameReads++; return 123; }
    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var type = assembly.GetType("ModDiagnostics.RuntimeCostEvents", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var reset = type.GetMethod("Reset", flags);
        var record = type.GetMethod("Record", flags);
        var export = type.GetMethod("FreezeCsv", flags);
        int capacity = (int)type.GetField("Capacity", flags).GetRawConstantValue();
        int checks = 0;
        var costs = assembly.GetType("ModDiagnostics.RuntimeCosts", true);
        var trace = assembly.GetType("ModDiagnostics.TraceBuffer", true);
        costs.GetField("FrameNumber", flags).SetValue(null, new Func<int>(Frame));
        trace.GetMethod("SetEnabled", flags).Invoke(null, new object[] { false });
        costs.GetMethod("End", flags).Invoke(null, new object[] { "disabled", System.Diagnostics.Stopwatch.GetTimestamp() });
        if (frameReads != 0) throw new Exception("disabled capture reads engine frame");
        trace.GetMethod("SetEnabled", flags).Invoke(null, new object[] { true });
        reset.Invoke(null, null);
        costs.GetMethod("End", flags).Invoke(null, new object[] { "enabled", System.Diagnostics.Stopwatch.GetTimestamp() });
        if (frameReads != 1 || !((string)export.Invoke(null, new object[] { System.Diagnostics.Stopwatch.Frequency })).Contains(",123,\"enabled\",")) throw new Exception("frame provider not wired to callback");
        trace.GetMethod("SetEnabled", flags).Invoke(null, new object[] { false });
        costs.GetField("FrameNumber", flags).SetValue(null, null);
        checks += 2;
        for (int pass = 0; pass < 20; pass++)
        {
            reset.Invoke(null, null);
            // Many callbacks inside a single polling interval, one brief stall.
            for (int i = 0; i < 300; i++) record.Invoke(null, new object[] { 500 + i / 12, "paint", (long)i * 1000, (long)i * 1000 + (i == 147 ? 20000 : 50) });
            string csv = (string)export.Invoke(null, new object[] { 1000000L });
            if (!csv.Contains("148,512,\"paint\",147000,167000,20.000000") || !csv.Contains("retained=300 total=300")) throw new Exception("brief event lost or wrongly associated");
            record.Invoke(null, new object[] { 900, "snapshot-heavy-work", 1L, 100000L });
            if ((string)export.Invoke(null, new object[] { 1000000L }) != csv) throw new Exception("snapshot contaminated frozen capture");
            checks += 3;
            reset.Invoke(null, null);
            for (int i = 0; i < capacity + 3; i++) record.Invoke(null, new object[] { i, "tool,\"quoted\"", (long)i, (long)i + 1 });
            csv = (string)export.Invoke(null, new object[] { 1000L });
            if (!csv.Contains("retained=" + capacity + " total=" + (capacity + 3)) || !csv.Contains("\n4,3,\"tool,\"\"quoted\"\"\",3,4,1.000000\n") || csv.Contains("\n1,0,")) throw new Exception("ring order/capacity/CSV quoting changed");
            checks += 3;
            reset.Invoke(null, null);
            record.Invoke(null, new object[] { 8, "bad-clock", 20L, 10L });
            csv = (string)export.Invoke(null, new object[] { 1000L });
            if (!csv.Contains("retained=0 total=0") || csv.Contains("bad-clock")) throw new Exception("reset or invalid interval handling failed");
            checks += 2;
        }
        Console.WriteLine("PASS: " + checks + " transient capture checks over 20 rounds, including sub-polling spikes, frame association, bounded overflow, snapshot isolation and CSV escaping. Synthetic clocks only; full game frame attribution remains unmeasured.");
    }
}
