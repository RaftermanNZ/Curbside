using UnityEngine;
using UnityEngine.Profiling;

namespace MedianGrassReplacement.Systems
{
    internal static class PerformanceDiagnostics
    {
        // Explicit snapshot only. No hardware polling, renderer enumeration,
        // profiler capture or settings changes during ordinary play.
        internal static void Record()
        {
            Mod.Log.Info("Performance environment: Curbside="+BuildIdentity.Version+
                " Unity="+Application.unityVersion+" graphicsAPI="+SystemInfo.graphicsDeviceType+
                " device="+SystemInfo.graphicsDeviceName+" cpuThreads="+SystemInfo.processorCount+
                " systemMemoryMiB="+SystemInfo.systemMemorySize+" graphicsMemoryMiB="+SystemInfo.graphicsMemorySize+
                " screen="+Screen.width+"x"+Screen.height+" anisotropicFiltering="+QualitySettings.anisotropicFiltering);
            Mod.Log.Info("Performance memory snapshot: managedUsedBytes="+Profiler.GetMonoUsedSizeLong()+
                " unityAllocatedBytes="+Profiler.GetTotalAllocatedMemoryLong()+
                "; these are process-wide Unity counters, not Curbside-only or total VRAM measurements. Zero may mean unavailable.");
        }
    }
}
