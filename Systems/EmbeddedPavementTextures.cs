using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedianGrassReplacement.Systems
{
    // Only native Pavement 01/02 lack TextureAsset provenance in the shipped
    // renderer. Verify their embedded originals once, not every area or hover.
    internal sealed class EmbeddedPavementTextures
    {
        private sealed class Entry
        {
            internal string Target;
            internal uint Version;
            internal bool Approved;
        }

        private readonly Dictionary<Texture, Entry> entries = new Dictionary<Texture, Entry>();
        private readonly Action ready;
        private int epoch;
        private bool pending;
        private static readonly Func<UnityEngine.Object, bool> persistent = PersistentReader();

        private static Func<UnityEngine.Object, bool> PersistentReader()
        {
            var method = typeof(UnityEngine.Object).GetMethod("IsPersistent", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(UnityEngine.Object) }, null);
            if (method == null || method.ReturnType != typeof(bool)) return null;
            try { return (Func<UnityEngine.Object, bool>)Delegate.CreateDelegate(typeof(Func<UnityEngine.Object, bool>), method); }
            catch (Exception) { return null; }
        }

        internal EmbeddedPavementTextures(Action ready) { this.ready = ready; }

        internal void Clear()
        {
            unchecked { epoch++; }
            entries.Clear();
            pending = false;
        }

        internal static bool NativeContext(AreaPrefab prefab, Material template)
        {
            if (prefab == null || template == null) return false;
            string prefabId, materialId;
            switch (prefab.name)
            {
                case "Pavement Surface 01":
                    prefabId = "cd18afa90fdd2ed499af3dabde90708e";
                    materialId = "b63d130c60f819443a0d680915aadc40";
                    break;
                case "Pavement Surface 02":
                    prefabId = "f1feb094a83e7de45863812e2db1a39d";
                    materialId = "8aa2a4abc641a04429b74a7655d532d4";
                    break;
                default: return false;
            }
            var resources = AssetDatabase.global?.resources;
            return resources != null && resources.prefabsMap != null && resources.unityObjectsMap != null
                && resources.prefabsMap.TryGetObject(prefabId, out var nativePrefab) && nativePrefab == prefab
                && resources.unityObjectsMap.TryGetObject(materialId, out var nativeMaterial) && nativeMaterial == template;
        }

        internal bool Allows(AreaPrefab prefab, Material template, Texture current)
        {
            if (!NativeContext(prefab, template) || !(current is Texture2D texture)
                || persistent == null || !persistent(texture)
                || template.GetTexture("_BaseColorMap") != current
                || texture.width != 2048 || texture.height != 2048 || texture.format != TextureFormat.DXT1
                || texture.mipmapCount != 12 || texture.filterMode != FilterMode.Trilinear
                || texture.wrapMode != TextureWrapMode.Repeat || texture.mipMapBias != 0f) return false;
            if (entries.TryGetValue(current, out var known))
                return known.Approved && known.Target == prefab.name && known.Version == texture.updateCount;
            var context = SynchronizationContext.Current;
            if (pending || entries.Count >= 4 || context == null || !SystemInfo.supportsAsyncGPUReadback
                || QualitySettings.activeColorSpace != ColorSpace.Linear) return false;
            // Only settings/discovery calls enter this path. Never await the GPU
            // or hash on the game thread; at most one verification is in flight.
            int level = Mathf.Clamp(QualitySettings.globalTextureMipmapLimit, 0, 4);
            int size = 2048 >> level;
            int generation = epoch;
            var entry = new Entry { Target = prefab.name, Version = texture.updateCount };
            entries.Add(current, entry);
            pending = true;
            RenderTexture copy = null;
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                copy = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                if (!copy.Create()) throw new InvalidOperationException("Pavement verification copy unavailable.");
                GL.sRGBWrite = false;
                Graphics.Blit(texture, copy);
                var retainedCopy = copy;
                AsyncGPUReadback.Request(retainedCopy, 0, TextureFormat.RGBA32, request =>
                {
                    byte[] pixels = null;
                    try
                    {
                        if (generation == epoch && !request.hasError && texture != null
                            && texture.updateCount == entry.Version && request.width == size && request.height == size)
                            pixels = request.GetData<byte>().ToArray();
                    }
                    catch (Exception) { /* Failed verification leaves the native material intact. */ }
                    finally
                    {
                        if (retainedCopy != null)
                        {
                            retainedCopy.Release();
                            UnityEngine.Object.Destroy(retainedCopy);
                        }
                    }
                    if (generation != epoch) return;
                    if (pixels == null) { pending = false; ready?.Invoke(); return; }
                    var ownedPixels = pixels;
                    Task.Run(() =>
                    {
                        bool approved = false;
                        try { approved = Matches(entry.Target, level, ownedPixels); }
                        catch (Exception) { /* Fail closed on hashing errors. */ }
                        context.Post(_ =>
                        {
                            if (generation != epoch) return;
                            pending = false;
                            entry.Approved = approved && texture != null && texture.updateCount == entry.Version;
                            ready?.Invoke();
                        }, null);
                    });
                });
            }
            catch (Exception)
            {
                if (copy != null) { copy.Release(); UnityEngine.Object.Destroy(copy); }
                pending = false;
            }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
            return false;
        }

        // Fingerprints from the installed game's native DXT1 originals through
        // the same linear copy, at normal texture-quality sizes. Unknown data
        // or GPU conversion differences retain vanilla rather than broadening
        // the target rule. No game texture pixels are shipped in the mod.
        internal static bool Matches(string target, int level, byte[] pixels)
        {
            if (pixels == null || level < 0 || level > 4 || pixels.Length != (2048 >> level) * (2048 >> level) * 4) return false;
            string[] expected;
            switch (target)
            {
                case "Pavement Surface 01": expected = Road; break;
                case "Pavement Surface 02": expected = Sidewalk; break;
                default: return false;
            }
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(pixels)).Replace("-", "").ToLowerInvariant() == expected[level];
        }

        private static readonly string[] Road = {
            "f11ab7925d3acb4332efa721d48ab38c65dfbc9d23d2611ad7355f7749ea805c",
            "1a6bfcd133b14354b3706fe5e92fdd6a1ed5a1c6b5fbed83a9540052926be23d",
            "7ce954b169b9f5b44411ea706587e1754d1bb0c6274d62bdf8765c2491f2cddf",
            "2647fb73234c508d1b37f91bbf9cbe6f4ae2942f9b535c87c5ebbe759fd2d974",
            "6bb688e8956cea2e9846b6887b4c0ed46b899d03cea88492ae2931bf61d8e968" };
        private static readonly string[] Sidewalk = {
            "de2d752f5c5cdaea25dd90b2166a7b6bed05a50a16a00f926b0ff435743f27f4",
            "405d886d108cd21dd8a3f9fec690945900afa5f3e57887f88c04ba960aa0c858",
            "35017ba930d7ce197b9011fe6267391a461e58cc784ee1e4202a8b42dd53d304",
            "797c707faf5412cf56f651465f5cca280a6e6fd0ba7facd0ad31eca235d27509",
            "a86079efe61e541ac677bd802b90e92dc9fc5fa61b843594a8f2661d607960c8" };
    }
}
