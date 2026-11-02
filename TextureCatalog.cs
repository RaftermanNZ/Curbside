using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using Game.UI.Widgets;

using UnityEngine;



namespace MedianGrassReplacement

{

    internal static class TextureCatalog

    {

        internal const string BundledValue = "__CURBSIDE_GRASS__";

        internal static string BundledPath => Path.Combine(DefaultFolder, "Bundled", "CurbsideGrass-v2-2k_BaseColor.png");

        internal static void InstallBundled()

        {

            try { BundledAssets.Install("MedianGrassReplacement.CurbsideGrass.png", BundledPath); }

            catch (Exception error) { Mod.Log.Warn("Bundled grass texture could not be prepared: " + error.Message); }

        }

        internal const string VanillaValue = "__VANILLA__";

        internal const int MaxVisibleTextures = 256;



        private static string _cachedFolder = string.Empty;

        private static int _cachedRevision = -1;

        private static DropdownItem<string>[] _cachedItems =

        {

            new DropdownItem<string> { value = VanillaValue, displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Vanilla") },

                new DropdownItem<string> { value = BundledValue, displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Curbside grass (included)") }

        };



        internal static int Revision { get; private set; }



        internal static string DefaultFolder

        {

            get

            {

                string root = Application.persistentDataPath;

                return Path.Combine(root, "ModsData", Mod.ModId, "Textures");

            }

        }



        internal static void EnsureDefaultFolder()

        {

            try

            {

                Directory.CreateDirectory(DefaultFolder);

            }

            catch (Exception ex)

            {

                Mod.Log.Warn($"Could not create default texture folder: {ex.Message}");

            }

        }



        internal static void Invalidate()

        {

            unchecked { Revision++; }

            _cachedFolder = string.Empty;

            _cachedRevision = -1;

        }



        internal static DropdownItem<string>[] GetItems(string folder)

        {

            string normalized = NormalizeFolder(folder);

            if (string.Equals(normalized, _cachedFolder, StringComparison.OrdinalIgnoreCase)

                && _cachedRevision == Revision)

            {

                return _cachedItems;

            }



            var items = new List<DropdownItem<string>>

            {

                new DropdownItem<string> { value = VanillaValue, displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Vanilla") },

                new DropdownItem<string> { value = BundledValue, displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Curbside grass (included)") }

            };



            try

            {

                if (Directory.Exists(normalized))

                {

                    foreach (string path in SurfaceTextureCatalog.LibraryFiles(normalized)

                                 .Where(IsSelectableAlbedo)

                                 .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.CurrentCultureIgnoreCase)

                                 .Take(MaxVisibleTextures))

                    {

                        string file = Path.GetFileName(path);

                        items.Add(new DropdownItem<string>

                        {

                            value = SurfaceTextureCatalog.LibraryLabel(normalized,path),

                            displayName = GetDisplayName(SurfaceTextureCatalog.LibraryLabel(normalized,path))

                        });

                    }

                }

            }

            catch (Exception ex)

            {

                Mod.Log.Warn($"Could not scan texture folder '{normalized}': {ex.Message}");

            }



            _cachedFolder = normalized;

            _cachedRevision = Revision;

            _cachedItems = items.ToArray();

            return _cachedItems;

        }



        internal static string NormalizeFolder(string folder)

        {

            if (string.IsNullOrWhiteSpace(folder))

            {

                return DefaultFolder;

            }



            try

            {

                return Path.GetFullPath(folder.Trim().Trim(new[] { '"' }));

            }

            catch

            {

                return DefaultFolder;

            }

        }



        internal static bool TryResolveSelection(

            string folder,

            string selectedValue,

            out TextureSetPaths paths,

            out string error)

        {

            paths = default;

            error = string.Empty;



            if (string.IsNullOrWhiteSpace(selectedValue) || selectedValue == VanillaValue)

            {

                error = "Vanilla is selected.";

                return false;

            }



            if (selectedValue == BundledValue)

            {

                if (!File.Exists(BundledPath)) { error = "The included grass texture is unavailable."; return false; }

                paths = new TextureSetPaths(BundledPath, null, null); return true;

            }

            string root = NormalizeFolder(folder);

            string safeName = selectedValue;

            string albedo;
            try { albedo = Path.GetFullPath(Path.Combine(root, safeName)); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { error = "The selected texture name is invalid."; return false; }
            string boundary=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if (Path.IsPathRooted(safeName)||!albedo.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))

            {

                error = "The selected texture name is invalid.";

                return false;

            }



            root = Path.GetDirectoryName(albedo);

            if (!File.Exists(albedo))

            {

                error = $"Selected texture '{safeName}' no longer exists.";

                return false;

            }



            string stem = Path.GetFileNameWithoutExtension(albedo);

            string prefix = StripKnownAlbedoSuffix(stem);



            string normal = FirstExisting(root,

                prefix + "_Normal.png",

                prefix + "_NormalMap.png",

                stem + "_Normal.png",

                stem + "_NormalMap.png");



            string mask = FirstExisting(root,

                prefix + "_Mask.png",

                prefix + "_MaskMap.png",

                stem + "_Mask.png",

                stem + "_MaskMap.png");



            paths = new TextureSetPaths(albedo, normal, mask);

            return true;

        }



        private static bool IsSelectableAlbedo(string path)

        {

            string stem = Path.GetFileNameWithoutExtension(path);

            return !stem.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase)

                && !stem.EndsWith("_NormalMap", StringComparison.OrdinalIgnoreCase)

                && !stem.EndsWith("_Mask", StringComparison.OrdinalIgnoreCase)

                && !stem.EndsWith("_MaskMap", StringComparison.OrdinalIgnoreCase);

        }



        private static string GetDisplayName(string file)

        {

            string stem = Path.GetFileNameWithoutExtension(file);

            string name = StripKnownAlbedoSuffix(stem).Replace('_', ' ');
            string parent = Path.GetDirectoryName(file);
            return string.IsNullOrEmpty(parent) ? name : Path.Combine(parent, name);

        }



        private static string StripKnownAlbedoSuffix(string stem)

        {

            string[] suffixes = { "_BaseColor", "_Albedo", "_Diffuse" };

            foreach (string suffix in suffixes)

            {

                if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))

                {

                    return stem.Substring(0, stem.Length - suffix.Length);

                }

            }



            return stem;

        }



        private static string FirstExisting(string root, params string[] names)

        {

            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))

            {

                string path = Path.Combine(root, name);

                if (File.Exists(path))

                {

                    return path;

                }

            }



            return null;

        }

    }



    internal readonly struct TextureSetPaths

    {

        internal TextureSetPaths(string albedo, string normal, string mask)

        {

            Albedo = albedo;

            Normal = normal;

            Mask = mask;

        }



        internal string Albedo { get; }

        internal string Normal { get; }

        internal string Mask { get; }

    }

}

