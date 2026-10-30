using System;

using System.IO;

using System.Security.Cryptography;

namespace MedianGrassReplacement

{

    internal static class BundledAssets

    {

        internal static bool Install(string resource, string path)

        {

            using (var source = typeof(Mod).Assembly.GetManifestResourceStream(resource))

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

}

