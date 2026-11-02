using System;

using System.IO;

using System.Security.Cryptography;

namespace MedianGrassReplacement

{

    internal static class BundledAssets

    {

        private static string _directory;

        internal static void Initialize(Game.Modding.IMod mod)
        {
            _directory = null;
            var manager = Game.SceneFlow.GameManager.instance?.modManager;
            if (manager != null && manager.TryGetExecutableAsset(mod, out var asset))
                _directory = BundledAssetPathPolicy.DirectoryFromExecutable(asset.path);
        }

        internal static Stream Open(string resource)
        {
            string filename;
            switch(resource)
            {
                case "MedianGrassReplacement.CurbsideGrass.png":filename="CurbsideGrass_BaseColor.png";break;
                case "MedianGrassReplacement.RoadWear.png":filename="CurbsideRoadWear_BaseColor.png";break;
                case "MedianGrassReplacement.RoadSurface.png":filename="CurbsideRoad_BaseColor.png";break;
                case "MedianGrassReplacement.SidewalkSurface.png":filename="CurbsideSidewalk_BaseColor.png";break;
                default:return typeof(Mod).Assembly.GetManifestResourceStream(resource);
            }
            if (string.IsNullOrEmpty(_directory))
                throw new FileNotFoundException("The game did not resolve Curbside's installed asset directory.");
            string path=Path.Combine(_directory,"BundledTextures",filename);
            if(!File.Exists(path))throw new FileNotFoundException("Included texture file missing; reinstall the complete Curbside package.",path);
            return File.OpenRead(path);
        }

        internal static bool Install(string resource, string path)

        {

            using (var source = Open(resource))

            {

                if (source == null) throw new FileNotFoundException("Bundled resource unavailable: " + resource);

                if (File.Exists(path) && new FileInfo(path).Length == source.Length)

                {

                    byte[] expected, current;

                    using (var hash = SHA256.Create())

                    {

                        expected = hash.ComputeHash(source);

                        using (var existing = File.OpenRead(path)) current = hash.ComputeHash(existing);

                    }

                    bool equal = expected.Length == current.Length;

                    for (int i = 0; equal && i < expected.Length; i++) equal = expected[i] == current[i];

                    if (equal) return false;

                    source.Position = 0;

                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (var output = File.Create(path)) source.CopyTo(output);

                return true;

            }

        }

    }

    internal static class BundledAssetPathPolicy
    {
        internal static string DirectoryFromExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.IsPathRooted(path) ? Path.GetDirectoryName(Path.GetFullPath(path)) : null; }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return null; }
        }
    }

}

