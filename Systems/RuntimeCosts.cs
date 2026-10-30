using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.Text;
namespace ModDiagnostics
{
    internal static class RuntimeCosts
    {
        private sealed class Cost {internal long Count,Ticks,Maximum;}
        private static readonly Dictionary<string,Cost> Costs=new Dictionary<string,Cost>();
        internal static long Begin()=>TraceBuffer.Enabled?Stopwatch.GetTimestamp():0;
        internal static void End(string name,long start)
        {
            if(start==0||!TraceBuffer.Enabled)return;long ticks=Stopwatch.GetTimestamp()-start;
            if(!Costs.TryGetValue(name,out var cost))Costs[name]=cost=new Cost();cost.Count++;cost.Ticks+=ticks;if(ticks>cost.Maximum)cost.Maximum=ticks;
        }
        internal static void Reset()=>Costs.Clear();
        internal static string Snapshot()
        {
            var result=new StringBuilder("Curbside managed update costs since diagnostics enabled (not total game/GPU frame time):");
            foreach(var pair in Costs){var cost=pair.Value;double scale=1000d/Stopwatch.Frequency;result.Append("\n").Append(pair.Key).Append(" calls=").Append(cost.Count).Append(" meanMs=").Append((cost.Ticks*scale/Math.Max(1,cost.Count)).ToString("F3",CultureInfo.InvariantCulture)).Append(" maxMs=").Append((cost.Maximum*scale).ToString("F3",CultureInfo.InvariantCulture));}return result.ToString();
        }
    }
}
