using Game;using Game.City;using Game.Prefabs;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    // Reads one city setting, not city entities. No shared prefab is changed.
    public partial class MarkingThemeSystem:GameSystemBase
    {
        internal static bool CityEuropean;
        private Entity theme;private bool known;private bool lastEuropean,lastEdges;private float next;
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){base.OnGamePreload(purpose,mode);theme=Entity.Null;known=false;CityEuropean=false;next=0;}
        protected override void OnUpdate()
        {
            if(Mod.Settings==null||Mod.Saving||UnityEngine.Time.realtimeSinceStartup<next)return;
            next=UnityEngine.Time.realtimeSinceStartup+1;
            var selected=World.GetOrCreateSystemManaged<CityConfigurationSystem>().defaultTheme;
            if(selected!=theme||!known)
            {
                if(World.GetOrCreateSystemManaged<PrefabSystem>().TryGetPrefab<ThemePrefab>(selected,out var definition)&&definition!=null){CityEuropean=definition.assetPrefix=="EU";theme=selected;known=true;}
            }
            bool european=Mod.Settings.EuropeanMarkings,edges=Mod.Settings.ThemeAssetEdgeColors;
            if(lastEuropean!=european||lastEdges!=edges){lastEuropean=european;lastEdges=edges;CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();JunctionMarkingSystem.RequestRefresh();}
        }
    }
}
