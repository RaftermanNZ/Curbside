using System;
using System.IO;
using System.Reflection;
class GrassCatalogChecks
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var type = assembly.GetType("MedianGrassReplacement.TextureCatalog");
        var resolve = type.GetMethod("TryResolveSelection", BindingFlags.Static | BindingFlags.NonPublic);
        var display = type.GetMethod("GetDisplayName", BindingFlags.Static | BindingFlags.NonPublic);
        Check((string)display.Invoke(null,new object[]{"Grass_BaseColor.png"}) == "Grass", "Root texture label stays unchanged");
        Check((string)display.Invoke(null,new object[]{Path.Combine("Pack A","Grass_BaseColor.png")}) == Path.Combine("Pack A","Grass"), "Nested texture label identifies its pack");
        Check(!Equals(display.Invoke(null,new object[]{Path.Combine("Pack A","Grass_BaseColor.png")}),display.Invoke(null,new object[]{Path.Combine("Pack B","Grass_BaseColor.png")})), "Same-name grass textures have distinct visible labels");
        string parent = Path.GetFullPath(Directory.GetCurrentDirectory());
        string root = Path.Combine(parent, "grass-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Pack"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Grass_BaseColor.png"), "fixture");
            File.WriteAllText(Path.Combine(root, "Pack", "Grass_BaseColor.png"), "fixture");
            File.WriteAllText(Path.Combine(root, "Pack", "Grass_Normal.png"), "fixture");
            File.WriteAllText(Path.Combine(root, "Pack", "Grass_Mask.png"), "fixture");
            foreach (string selection in new[] { "Grass_BaseColor.png", "Pack/Grass_BaseColor.png" })
            {
                object[] args = { root, selection, null, null };
                Check((bool)resolve.Invoke(null, args), "Existing root/nested selection resolves");
                var paths = args[2];
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Check((string)paths.GetType().GetProperty("Albedo", flags).GetValue(paths) == Path.GetFullPath(Path.Combine(root, selection)), "Same-name files resolve to the selected pack");
                if (selection.StartsWith("Pack"))
                    foreach (string property in new[] { "Normal", "Mask" })
                        Check(((string)paths.GetType().GetProperty(property, flags).GetValue(paths)).StartsWith(Path.Combine(root, "Pack")), "Companion maps come from the same pack");
            }
            foreach (string selection in new[] { "../outside.png", Path.Combine(parent, "outside.png"), "missing.png", "bad\0.png", "__VANILLA__", "" })
            {
                object[] args = { root, selection, null, null };
                Check(!(bool)resolve.Invoke(null, args), "Missing, vanilla, malformed and escaping paths fail cleanly");
                Check(!string.IsNullOrEmpty((string)args[3]), "Failure explains why");
            }
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
        Console.WriteLine(checks + " grass selection checks passed: root, nested, duplicate names, companion maps, malformed and escaping paths.");
    }
}
