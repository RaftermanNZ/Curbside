using System.Collections.Generic;
using Game.Prefabs;
namespace GardenCitySignals
{
 // Borrowed game-owned prefab references only. No asset streams, geometry
 // headers, native array views or allocations are retained or disposed here.
 internal static class NativeSignalCatalog
 {
  private static PrefabSystem owner;
  private static readonly Dictionary<string,StaticObjectPrefab> models=new Dictionary<string,StaticObjectPrefab>();
  internal static void Register(PrefabSystem system,IDictionary<string,StaticObjectPrefab> source)
  {owner=system;models.Clear();foreach(var item in source)models.Add(item.Key,item.Value);}
  internal static bool TryGet(PrefabSystem system,string name,out StaticObjectPrefab model)
  {model=null;return owner==system&&models.TryGetValue(name,out model)&&model!=null;}
  internal static void Reset(PrefabSystem system=null){if(system!=null&&owner!=system)return;owner=null;models.Clear();}
 }
}
