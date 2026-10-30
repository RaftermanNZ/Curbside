using Game.Prefabs;

using Game.Tools;

using Unity.Jobs;

namespace MedianGrassReplacement.Systems

{

    // The toolbar asset is only a launcher. NetToolSystem never receives its clone.

    public partial class LineCrosswalkLaunchTool : ToolBaseSystem

    {

        private PrefabBase _selected;
        protected override void OnCreate(){base.OnCreate();m_ToolSystem.tools.Remove(this);m_ToolSystem.tools.Insert(0,this);}

        public override string toolID => "MGRLineCrosswalkLaunch";

        public override PrefabBase GetPrefab() => _selected;

        public override bool TrySetPrefab(PrefabBase prefab)

        {

            

            if (prefab == null || (prefab.name != CrosswalkUpgradeSystem.LineName && prefab.name != CrosswalkUpgradeSystem.InvisibleName && prefab.name != "Crosswalk")) return false;

            _selected = prefab; m_ToolSystem.EventPrefabChanged?.Invoke(prefab); return true;

        }

        protected override JobHandle OnUpdate(JobHandle deps)

        {

            if(m_ToolSystem.activeTool!=this||Mod.Saving)return deps;
            var system = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();

            bool success = _selected?.name == "Crosswalk" ? system.LaunchNativeZebraCrosswalk() : (_selected?.name == CrosswalkUpgradeSystem.InvisibleName ? system.LaunchNativeInvisibleCrosswalk() : system.LaunchNativeCrosswalk());

            if (!success) m_ToolSystem.activeTool = m_DefaultToolSystem;

            return deps;

        }

    }

}

