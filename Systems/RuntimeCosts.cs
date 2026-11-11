using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.Text;
namespace ModDiagnostics
{
    internal static class RuntimeCosts
    {
        private sealed class Cost {internal long Count,Ticks,Maximum,OverOne,OverFour,OverSixteen;}
        private sealed class Gauge {internal long Current,Pending;}
        private static readonly Dictionary<string,Cost> Costs=new Dictionary<string,Cost>();
        private static readonly Dictionary<string,Gauge> Gauges=new Dictionary<string,Gauge>();
        internal static Func<int> FrameNumber;
        private static long windowStart=Stopwatch.GetTimestamp();
        internal static long Begin()=>TraceBuffer.Enabled?Stopwatch.GetTimestamp():0;
        internal static void End(string name,long start)
        {
            if(start==0||!TraceBuffer.Enabled)return;long end=Stopwatch.GetTimestamp();long ticks=end-start;
            RuntimeCostEvents.Record(FrameNumber?.Invoke()??-1,name,start,end);
            if(!Costs.TryGetValue(name,out var cost))Costs[name]=cost=new Cost();cost.Count++;cost.Ticks+=ticks;if(ticks>cost.Maximum)cost.Maximum=ticks;
            double ms=ticks*(1000d/Stopwatch.Frequency);
            if(ms>=1)cost.OverOne++;if(ms>=4)cost.OverFour++;if(ms>=16)cost.OverSixteen++;
        }
        internal static void Workload(string name,long current,long pending)
        {
            if(!TraceBuffer.Enabled)return;
            if(!Gauges.TryGetValue(name,out var gauge))Gauges[name]=gauge=new Gauge();
            gauge.Current=Math.Max(0,current);gauge.Pending=Math.Max(0,pending);
        }
        internal static void Reset(){Costs.Clear();Gauges.Clear();RuntimeCostEvents.Reset();windowStart=Stopwatch.GetTimestamp();}
        internal static void ExportEvents(string folder)
        {
            if(!TraceBuffer.Enabled)return;
            try
            {
                // Freeze before the legacy snapshot collectors run, so their
                // expensive inspection work cannot masquerade as a hover spike.
                var csv=RuntimeCostEvents.FreezeCsv(Stopwatch.Frequency);
                System.IO.Directory.CreateDirectory(folder);
                var path=System.IO.Path.Combine(folder,"callback-costs-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+".csv");
                System.IO.File.WriteAllText(path,csv,new UTF8Encoding(false));
                MedianGrassReplacement.Mod.Log.Info("Diagnostic callback events exported: "+path);
            }
            catch(Exception error){MedianGrassReplacement.Mod.Log.Warn("Diagnostic callback export failed: "+error.GetType().Name);}
        }
        internal static string Snapshot()
        {
            double seconds=Math.Max(.001,(Stopwatch.GetTimestamp()-windowStart)/(double)Stopwatch.Frequency);
            var result=new StringBuilder("Curbside diagnostic update elapsed time (includes dependency waits; not CPU self-time, game frame time or GPU time). Nested measurements may overlap. WindowSeconds=");
            result.Append(seconds.ToString("F2",CultureInfo.InvariantCulture));
            foreach(var pair in Costs){var cost=pair.Value;double scale=1000d/Stopwatch.Frequency;result.Append("\n").Append(pair.Key).Append(" calls=").Append(cost.Count).Append(" meanMs=").Append((cost.Ticks*scale/Math.Max(1,cost.Count)).ToString("F3",CultureInfo.InvariantCulture)).Append(" maxMs=").Append((cost.Maximum*scale).ToString("F3",CultureInfo.InvariantCulture)).Append(" msPerWallSecond=").Append((cost.Ticks*scale/seconds).ToString("F3",CultureInfo.InvariantCulture)).Append(" callsOver1ms=").Append(cost.OverOne).Append(" callsOver4ms=").Append(cost.OverFour).Append(" callsOver16ms=").Append(cost.OverSixteen);}
            foreach(var pair in Gauges)result.Append("\nWorkload ").Append(pair.Key).Append(" current=").Append(pair.Value.Current).Append(" pending=").Append(pair.Value.Pending);
            return result.ToString();
        }
    }
}
