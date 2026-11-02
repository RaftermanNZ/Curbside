using System;

using System.IO;

using System.Linq;

using System.Collections.Generic;

using Game.UI.Widgets;

namespace MedianGrassReplacement

{

    internal static class DecalCatalog

    {

        internal const string BundledValue="__CURBSIDE_ROAD_WEAR__";
        internal static int Revision { get; private set; }

        private static string _folder;

        private static DropdownItem<string>[] _items;

        internal static void Invalidate() { Revision++; _folder = null; }

        internal static DropdownItem<string>[] Items(string folder)

        {

            if (_items != null && _folder == folder) return _items;

            var items = new List<DropdownItem<string>> { new DropdownItem<string> { value = "", displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("None") },new DropdownItem<string>{value=BundledValue,displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Road wear (included)")} };

            try { if (Directory.Exists(folder)) foreach (var path in Directory.EnumerateFiles(folder, "*.png").OrderBy(Path.GetFileName).Take(256)) items.Add(new DropdownItem<string> { value = Path.GetFileName(path), displayName = Path.GetFileNameWithoutExtension(path) }); }

            catch (IOException e) { Mod.Log.Warn("Could not read decal folder: " + e.Message); }

            catch (UnauthorizedAccessException e) { Mod.Log.Warn("Could not read decal folder: " + e.Message); }

            _folder = folder; return _items = items.ToArray();

        }

    }

}

