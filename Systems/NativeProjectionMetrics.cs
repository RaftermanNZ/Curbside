namespace MedianGrassReplacement.Systems
{
    internal struct NativeProjectionMetrics
    {
        internal long Batches, NativeActors, ActorsVisited, OwnersIndexed, DependenciesRead;
        internal long DirectLinks, FallbackLinks, Unresolved, Writes, Releases;
        internal long AppearanceWrites;
        internal long SourceEvents, SourceCoalesced, ElapsedTicks, PeakTicks;
    }

    public partial class NativeSignalProjectionSystem
    {
        private NativeProjectionMetrics metrics;
        private bool measureBatch;
        internal NativeProjectionMetrics Metrics => metrics;
        internal void RecordSnapshot()
        {
            double unit = 1000d / System.Diagnostics.Stopwatch.Frequency;
            Mod.Log.Info("Native signal visibility projection: batches=" + metrics.Batches
                + " nativeActors=" + metrics.NativeActors + " visited=" + metrics.ActorsVisited
                + " owners=" + metrics.OwnersIndexed + " dependencies=" + metrics.DependenciesRead
                + " direct=" + metrics.DirectLinks + " fallback=" + metrics.FallbackLinks
                + " unresolved=" + metrics.Unresolved + " writes=" + metrics.Writes
                + " appearanceWrites=" + metrics.AppearanceWrites + " mode=" + (Mod.Settings?.CurbsidePreviews == true ? "Curbside" : "Vanilla (performance)")
                + " releases=" + metrics.Releases + " sourceEvents=" + metrics.SourceEvents
                + " coalesced=" + metrics.SourceCoalesced + " elapsedMs=" + metrics.ElapsedTicks * unit
                + " peakMs=" + metrics.PeakTicks * unit + " activeLeases=" + leases.Count
                + ". Opt-in batch timings include ECS waits; allocations require profiler measurement.");
        }
    }
}
