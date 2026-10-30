using System;
using System.IO;
using System.Reflection;
class BundledAssetPathChecks
{
    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var type = assembly.GetType("MedianGrassReplacement.BundledAssetPathPolicy");
        var resolve = type.GetMethod("DirectoryFromExecutable", BindingFlags.Static | BindingFlags.NonPublic);
        int count = 0;
        foreach (string path in new[] { null, "", "   ", "relative.dll", "bad\0.dll" })
        {
            if (resolve.Invoke(null, new object[] { path }) != null) throw new Exception("Invalid/empty assembly paths must not resolve");
            count++;
        }
        foreach (string directory in new[] { Path.GetFullPath("local mod"), Path.GetFullPath("pdx cache/165355_2") })
        {
            var result = (string)resolve.Invoke(null, new object[] { Path.Combine(directory, "MedianGrassReplacement.dll") });
            if (result != directory) throw new Exception("Sidecar root must follow the game-resolved executable, including spaces and cache versions");
            count++;
        }
        var open = assembly.GetType("MedianGrassReplacement.BundledAssets").GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic);
        string icon = Array.Find(assembly.GetManifestResourceNames(), name => name.EndsWith(".svg"));
        using (var stream = (Stream)open.Invoke(null, new object[] { icon }))
            if (stream == null || stream.Length == 0) throw new Exception("Icons must remain available without a sidecar directory");
        count++;
        try { open.Invoke(null, new object[] { "MedianGrassReplacement.CurbsideGrass.png" }); throw new Exception("Missing sidecar root must fail explicitly"); }
        catch (TargetInvocationException ex) when (ex.InnerException is FileNotFoundException) { count++; }
        Console.WriteLine(count + " sidecar asset-path checks passed; icons stay embedded and missing roots fail cleanly.");
    }
}
