using System;
namespace MedianGrassReplacement.Systems
{
    internal static class SaveDiagnostics
    {
        // Heavy native-array audits are explicit developer diagnostics, not an
        // automatic cost for every player who enables the ordinary trace ring.
        internal static readonly bool DeepEnabled=Environment.GetEnvironmentVariable("CURBSIDE_DEEP_SAVE_AUDIT")=="1";
        // Vanilla SaveGameSystem owns asynchronous writer completion. Preserve
        // an explicit developer comparison (value 0), without blocking by default.
        internal static readonly bool JoinWriter=Environment.GetEnvironmentVariable("CURBSIDE_ASYNC_SAVE_WRITER")=="0";
    }
}
