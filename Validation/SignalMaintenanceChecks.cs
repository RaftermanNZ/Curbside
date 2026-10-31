using System;
using System.Reflection;
class SignalMaintenanceChecks
{
    static void Main()
    {
        var a=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var t=a.GetType("MedianGrassReplacement.Systems.SignalMaintenancePolicy",true);
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var state=t.GetMethod("State",flags);var moved=t.GetMethod("KeepMoved",flags);var custom=t.GetMethod("KeepCustom",flags);
        int cases=0;
        foreach(bool live in new[]{false,true})foreach(bool enabled in new[]{false,true})foreach(bool isCustom in new[]{false,true})foreach(bool native in new[]{false,true})
        {
            byte snapshot=(byte)state.Invoke(null,new object[]{enabled,isCustom});
            // Preserve the pre-optimization cleanup predicates, including disabled,
            // removed/custom-style and ordinary far-side/native-fallback cases.
            bool oldRemoveMoved=!live||!enabled||(!isCustom&&!native);
            bool keep=(bool)moved.Invoke(null,new object[]{live,snapshot,native});
            if(keep==oldRemoveMoved)throw new Exception("Moved signal retirement changed");
            bool oldKeepCustom=enabled&&isCustom;
            if((bool)custom.Invoke(null,new object[]{snapshot})!=oldKeepCustom)throw new Exception("Custom lamp/suppression ownership changed");
            cases+=2;
        }
        var gate=a.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true).GetProperty("TraceEnabled",flags);
        var diagnostics=a.GetType("ModDiagnostics.TraceBuffer",true);
        if(gate==null)throw new Exception("Missing diagnostic caller gate");
        if((bool)gate.GetValue(null))throw new Exception("Signal tracing must be off by default");
        var set=diagnostics.GetMethod("SetEnabled",flags);
        var notices=(System.Collections.Generic.HashSet<string>)a.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true).GetField("notices",flags).GetValue(null);
        try
        {
            set.Invoke(null,new object[]{true});
            if(!(bool)gate.GetValue(null))throw new Exception("Enabled tracing must retain diagnostics");
            for(int i=0;i<128;i++)notices.Add("test-"+i);
            if((bool)gate.GetValue(null))throw new Exception("Full trace budget must stop diagnostic work");
            notices.Clear();
            if(!(bool)gate.GetValue(null))throw new Exception("New trace budget must be usable");
        }
        finally{notices.Clear();set.Invoke(null,new object[]{false});}
        Console.WriteLine("PASS: "+cases+" cleanup decisions match baseline; inactive tracing gate is off. Runtime regeneration, save and timing-controller tests pending.");
    }
}
