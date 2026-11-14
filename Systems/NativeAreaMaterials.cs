using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Rendering;
using UnityEngine;

namespace MedianGrassReplacement.Systems
{
    internal static class NativeAreaMaterialPolicy
    {
        internal static bool Shader(string name) =>
            name == "BH/AreaDecalShader" || name == "Shader Graphs/AreaDecalShader";

        internal static bool Admits(string prefabName, string materialName, string shader,
            bool builtin, bool ambiguous, bool rendererOwned, bool template)
            => VanillaSurfacePolicy.TargetArea(prefabName) && builtin && !ambiguous
                && rendererOwned && !template && Shader(shader)
                && materialName == VanillaSurfacePolicy.AreaBatchName(prefabName);
    }

    // Read the engine's managed material list using the prefab's batch index.
    // Never obtain native batches, culling buffers, jobs or texture-load handles.
    // A changed private layout fails closed instead of falling back to names.
    internal static class NativeAreaMaterials
    {
        private static readonly FieldInfo batches = typeof(AreaBatchSystem).GetField(
            "m_ManagedBatchData", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type batch = batches?.FieldType.IsGenericType == true
            ? batches.FieldType.GetGenericArguments()[0] : null;
        private static readonly FieldInfo material = batch?.GetField("m_Material",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static bool Supported => batches != null && batch != null
            && batches.FieldType == typeof(List<>).MakeGenericType(batch)
            && batch.DeclaringType == typeof(AreaBatchSystem)
            && batch.Name == "ManagedBatchData" && material?.FieldType == typeof(Material);

        internal static Material Read(AreaBatchSystem system, int index)
        {
            if (system == null || index < 0 || !Supported) return null;
            var values = batches.GetValue(system) as IList;
            if (values == null || index >= values.Count) return null;
            var entry = values[index];
            return entry != null && entry.GetType() == batch ? material.GetValue(entry) as Material : null;
        }
    }
}
