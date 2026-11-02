using System;using System.Collections.Generic;using System.IO;using System.Linq;using Game.UI.Widgets;
namespace MedianGrassReplacement
{
    internal static class SurfaceTextureCatalog
    {
        internal const string BundledRoad="__CURBSIDE_ROAD__";
        internal const string BundledSidewalk="__CURBSIDE_SIDEWALK__";
        internal static string BundledRoadPath=>Path.Combine(TextureCatalog.DefaultFolder,"Bundled","CurbsideRoad-v2-2k_BaseColor.png");
        internal static string BundledSidewalkPath=>Path.Combine(TextureCatalog.DefaultFolder,"Bundled","CurbsideSidewalk-v2-2k_BaseColor.png");
        internal static string Resolve(string value)=>value==TextureCatalog.BundledValue?TextureCatalog.BundledPath:value==BundledRoad?BundledRoadPath:value==BundledSidewalk?BundledSidewalkPath:value;
        internal static void InstallBundled(){BundledAssets.Install("MedianGrassReplacement.RoadSurface.png",BundledRoadPath);BundledAssets.Install("MedianGrassReplacement.SidewalkSurface.png",BundledSidewalkPath);}
        internal static DropdownItem<string>[] WithCurrent(DropdownItem<string>[] items,params string[] current)
        {
            var list=new List<DropdownItem<string>>(items);foreach(var value in current)if(!string.IsNullOrWhiteSpace(value)&&!list.Exists(item=>item.value==value))list.Add(new DropdownItem<string>{value=value,displayName=MedianGrassReplacement.Localization.LocalizationService.Text("Current: ")+Path.GetFileNameWithoutExtension(value)});return list.ToArray();
        }
        internal static int Revision{get;private set;}
        private static readonly Dictionary<string,DropdownItem<string>[]> Cache=new Dictionary<string,DropdownItem<string>[]>();
        internal static void Invalidate(){Revision++;Cache.Clear();}
        internal static string LibraryLabel(string folder,string file){string root=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;string path=Path.GetFullPath(file);return path.StartsWith(root,StringComparison.OrdinalIgnoreCase)?path.Substring(root.Length):Path.GetFileName(file);}
        internal static IEnumerable<string> LibraryFiles(string folder)
        {
            var folders=new List<string>{folder};int count=0;
            foreach(var child in Directory.EnumerateDirectories(folder).OrderBy(x=>x).Take(64))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0){folders.Add(child);foreach(var nested in Directory.EnumerateDirectories(child).OrderBy(x=>x).Take(64))if((File.GetAttributes(nested)&FileAttributes.ReparsePoint)==0&&folders.Count<128)folders.Add(nested);}
            foreach(var path in folders)foreach(var file in Directory.EnumerateFiles(path,"*.png").OrderBy(Path.GetFileName)){yield return file;if(++count>=256)yield break;}
        }
        internal static DropdownItem<string>[] Items(string folder)
        {
            if(Cache.TryGetValue(folder,out var found))return found;if(Cache.Count>=8)Cache.Clear();
            var items=new List<DropdownItem<string>>{new DropdownItem<string>{value="",displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Vanilla / no replacement")},new DropdownItem<string>{value=TextureCatalog.BundledValue,displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Curbside grass (included)")},new DropdownItem<string>{value=BundledRoad,displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Curbside road (included)")},new DropdownItem<string>{value=BundledSidewalk,displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Curbside sidewalk (included)")}};
            try{if(Directory.Exists(folder))foreach(var file in LibraryFiles(folder))items.Add(new DropdownItem<string>{value=Path.GetFullPath(file),displayName=LibraryLabel(folder,file)});}
            catch(IOException error){Mod.Log.Warn("Surface folder: "+error.Message);}catch(UnauthorizedAccessException error){Mod.Log.Warn("Surface folder: "+error.Message);}
            return Cache[folder]=items.ToArray();
        }
    }
}

