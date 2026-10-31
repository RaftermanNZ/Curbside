using System;
using System.Diagnostics;
using System.Reflection;
class PerformanceTelemetryChecks
{
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var costs=assembly.GetType("ModDiagnostics.RuntimeCosts",true);
        var trace=assembly.GetType("ModDiagnostics.TraceBuffer",true);
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        Action<bool> enabled=value=>trace.GetMethod("SetEnabled",flags).Invoke(null,new object[]{value});
        Action reset=()=>costs.GetMethod("Reset",flags).Invoke(null,null);
        Func<string> snapshot=()=> (string)costs.GetMethod("Snapshot",flags).Invoke(null,null);
        Action<string,long,long> gauge=(name,current,pending)=>costs.GetMethod("Workload",flags).Invoke(null,new object[]{name,current,pending});
        Action<string,long> end=(name,start)=>costs.GetMethod("End",flags).Invoke(null,new object[]{name,start});
        enabled(false);reset();gauge("disabled",10,20);end("disabled",Stopwatch.GetTimestamp()-Stopwatch.Frequency);
        if((long)costs.GetMethod("Begin",flags).Invoke(null,null)!=0||snapshot().Contains("disabled"))throw new Exception("Disabled diagnostics retained measurements");
        enabled(true);reset();
        // Supply an elapsed interval without sleeping or blocking a game frame.
        end("spike",Stopwatch.GetTimestamp()-Stopwatch.Frequency/10);
        var measured=snapshot();
        foreach(var text in new[]{"spike calls=1","callsOver1ms=1","callsOver4ms=1","callsOver16ms=1","includes dependency waits","not CPU self-time","Nested measurements may overlap"})
            if(!measured.Contains(text))throw new Exception("Missing honest measurement or spike counter: "+text);
        gauge("owned",100,7);gauge("owned",3,2);
        if(!snapshot().Contains("Workload owned current=3 pending=2"))throw new Exception("Workload accumulated instead of showing current resources");
        gauge("owned",-1,-2);
        if(!snapshot().Contains("Workload owned current=0 pending=0"))throw new Exception("Negative resource count");
        reset();if(snapshot().Contains("Workload owned")||snapshot().Contains("spike calls="))throw new Exception("Previous city/session measurements survived reset");
        enabled(false);
        Console.WriteLine("PASS: diagnostics-off isolation, spike counters, current-resource gauges, reset and measurement caveats. No runtime FPS/TPS claim.");
    }
}
