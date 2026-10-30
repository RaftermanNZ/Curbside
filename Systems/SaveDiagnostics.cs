using System;
namespace MedianGrassReplacement.Systems
{
    internal static class SaveDiagnostics
    {
        // Heavy native-array audits are explicit developer diagnostics, not an
        // automatic cost for every player who enables the ordinary trace ring.
        internal static readonly bool DeepEnabled=Environment.GetEnvironmentVariable("CURBSIDE_DEEP_SAVE_AUDIT")=="1";
        // Keep the established writer fence in the first hardening build. The
        // native async writer comparison is a separately selectable experiment.
        internal static readonly bool JoinWriter=Environment.GetEnvironmentVariable("CURBSIDE_ASYNC_SAVE_WRITER")!="1";
    }
}
