using System;
using System.Reflection;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;

namespace MedianGrassReplacement.Systems
{
    internal static class NativeCrosswalkDragHooks
    {
        private const string Id = "Curbside.NativeCrosswalkTransaction.v1";
        private static Harmony harmony;
        private static NativeCrosswalkDragSystem active;
        private static MethodInfo courseMethod, commitMethod;
        private struct CommitState { internal bool Owned, Captured; }
        internal static bool Install(NativeCrosswalkDragSystem system)
        {
            if (!NativeSignalProjectionSystem.Supported) return false;
            try
            {
                var type = typeof(NetToolSystem);
                var course = type.GetMethod("UpdateCourse", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(JobHandle), typeof(bool) }, null);
                var commit = type.GetMethod("SetAppliedUpgrade", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
                var controls = type.GetField("m_ControlPoints", BindingFlags.Instance | BindingFlags.NonPublic);
                var states = type.GetField("m_UpgradeStates", BindingFlags.Instance | BindingFlags.NonPublic);
                if (course?.ReturnType != typeof(JobHandle) || commit?.ReturnType != typeof(void)
                    || controls?.FieldType != typeof(NativeList<ControlPoint>) || states?.FieldType != typeof(NativeList<NetToolSystem.UpgradeState>)) return false;
                if (active != null) return ReferenceEquals(active, system);
                harmony = new Harmony(Id);
                courseMethod = course; commitMethod = commit;
                harmony.Patch(course, prefix: Method(nameof(CoursePrefix)), postfix: Method(nameof(CoursePostfix)));
                harmony.Patch(commit, prefix: Method(nameof(CommitPrefix)), postfix: Method(nameof(CommitPostfix)));
                active = system; system.Ready = true;
                return true;
            }
            catch (Exception error)
            {
                RemovePatches(); harmony = null; active = null;
                Mod.Log.Warn("Native crosswalk drag handoff unavailable: " + error.Message);
                return false;
            }
        }
        private static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(NativeCrosswalkDragHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        internal static void Uninstall()
        {
            var system = active; active = null;
            try { system?.Release(); }
            finally { RemovePatches(); harmony = null; }
        }
        private static void RemovePatches()
        {
            if (harmony == null) return;
            if (courseMethod != null) harmony.Unpatch(courseMethod, HarmonyPatchType.All, Id);
            if (commitMethod != null) harmony.Unpatch(commitMethod, HarmonyPatchType.All, Id);
            courseMethod = commitMethod = null;
        }
        private static void CoursePrefix(NetToolSystem __instance, ref JobHandle inputDeps, bool removeUpgrade, bool __runOriginal, ref JobHandle __state,
            NativeList<NetToolSystem.UpgradeState> ___m_UpgradeStates)
        {
            if (active != null && __runOriginal)
            { inputDeps = active.PrepareCourse(__instance, ___m_UpgradeStates, removeUpgrade, inputDeps); __state = inputDeps; }
        }
        private static void CoursePostfix(NetToolSystem __instance, bool removeUpgrade, bool __runOriginal, JobHandle __state, ref JobHandle __result,
            NativeList<ControlPoint> ___m_ControlPoints, NativeList<NetToolSystem.UpgradeState> ___m_UpgradeStates)
        {
            if (active != null && __runOriginal)
                __result = active.CaptureCourse(__instance, ___m_ControlPoints, ___m_UpgradeStates, removeUpgrade, __result);
            else __result = JobHandle.CombineDependencies(__result, __state);
        }
        private static void CommitPrefix(NetToolSystem __instance, bool removing, bool __runOriginal, ref CommitState __state,
            NativeList<ControlPoint> ___m_ControlPoints, NativeList<NetToolSystem.UpgradeState> ___m_UpgradeStates)
        {
            __state.Owned = active != null && active.Owns(__instance);
            __state.Captured = __state.Owned && __runOriginal && active.CaptureCommit(__instance, ___m_ControlPoints, ___m_UpgradeStates, removing);
        }
        private static void CommitPostfix(CommitState __state, bool __runOriginal)
        { if (__state.Owned) active?.AcceptCommit(__state.Captured, __runOriginal); }
    }
}
