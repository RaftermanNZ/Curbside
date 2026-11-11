using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedianGrassReplacement.Systems
{
    internal static class SidewalkRepeatPolicy
    {
        // Whole repeats preserve the source image's periodic boundary. A
        // fractional baked repeat would create seams at native UV boundaries.
        internal static int Repeats(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1;
            return (int)Math.Floor(Math.Max(1f, Math.Min(10f, value)) + .5f);
        }

        internal static bool NeedsBake(string shader, int role)
            => shader == "BH/NetCompositionMeshLitShader" && role == 2;

        internal static long Bytes(int width, int height) => (long)width * height * 16 / 3;
    }

    internal static class SidewalkRepeatTexture
    {
        // Called on explicit texture/tiling changes only. One shared image,
        // independent of road count. No mesh edits, shader replacement, native
        // asset mutation, readback, or retained RenderTexture is involved.
        internal static Texture2D Create(Texture source, int repeats)
        {
            if (source == null || repeats < 2 || repeats > 10) return null;
            const CopyTextureSupport required = CopyTextureSupport.Basic | CopyTextureSupport.RTToTexture;
            if ((SystemInfo.copyTextureSupport & required) != required) return null;

            RenderTexture target = null;
            Texture2D result = null;
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                target = new RenderTexture(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    hideFlags = HideFlags.DontSave,
                    useMipMap = true,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear
                };
                if (!target.Create()) return null;

                GL.sRGBWrite = true;
                Graphics.Blit(source, target, new Vector2(repeats, repeats), Vector2.zero);
                target.GenerateMips();
                result = new Texture2D(source.width, source.height, target.graphicsFormat,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
                {
                    name = "MGR::SidewalkRepeat-" + repeats,
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 8,
                    ignoreMipmapLimit = true
                };

                // Discard initial CPU pixels before copying; Apply afterwards
                // would overwrite the GPU result with those initial pixels.
                result.Apply(false, true);
                for (int mip = 0; mip < result.mipmapCount; mip++)
                    Graphics.CopyTexture(target, 0, mip, result, 0, mip);
                var completed = result;
                result = null;
                return completed;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                if (result != null) UnityEngine.Object.Destroy(result);
                if (target != null) UnityEngine.Object.Destroy(target);
            }
        }
    }
}
