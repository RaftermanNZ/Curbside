using Game;

using Game.Prefabs;

using Unity.Collections;

using Unity.Entities;

namespace MedianGrassReplacement.Systems

{

    public partial class UpgradeAssetsSystem : GameSystemBase

    {

        internal static readonly string[] Names = { "RDC Hide Arrows", "RDC Hide Speed Decals", "RDC Hide Both", NodeLineUpgradeTool.AssetName, FarSideLightUpgradeTool.AssetName, GardenCityLightPolicy.AssetName, GardenCityLightPolicy.WireAsset, GardenCityLightPolicy.MastAsset, RoadSideUpgrades.DecalAsset, VisualPowerlineSystem.AssetName, ParkingBanPolicy.AssetName, ApproachStopTool.AssetName, ApproachStopTool.SingleAssetName, CenterlineVisibility.AssetName, ApproachStopTool.YieldAssetName, ApproachStopTool.SingleYieldAssetName, CenterlineUpgradePolicy.Single, CenterlineUpgradePolicy.Double, CenterlineUpgradePolicy.WhiteSingle, CenterlineUpgradePolicy.WhiteDouble, CenterlineUpgradePolicy.YellowSingle, CenterlineUpgradePolicy.YellowDouble, StreetLightingSystem.AssetName, SegmentUpgradePolicy.All, SegmentUpgradePolicy.Lanes, SegmentUpgradePolicy.Edges, SegmentUpgradePolicy.ResetLines, SegmentUpgradePolicy.ResetParking, SegmentUpgradePolicy.ResetAll, RoadFeatureInspector.AssetName, NodeLineUpgradeTool.EdgeAssetName, SegmentUpgradePolicy.Native, LaneDividerPolicy.AssetName, LaneDividerPolicy.Short, LaneDividerPolicy.Long, SectionEdgePolicy.AssetName, EdgeUpgradePolicy.Yellow, EdgeUpgradePolicy.DoubleWhite, EdgeUpgradePolicy.DoubleYellow, CrosswalkUpgradeSystem.FullTName, CrosswalkUpgradeSystem.DividerOnlyName, ParkingHatchPolicy.AssetName, LaneDividerPolicy.Yellow,LaneDividerPolicy.YellowShort,LaneDividerPolicy.YellowLong,LineShapePolicy.Visibility,ParkingHatchPolicy.Yellow,PassingLinePolicy.White,PassingLinePolicy.Yellow };

        private readonly PrefabBase[] _assets = new PrefabBase[Names.Length]; private EntityQuery _sources; private PrefabSystem _prefabs; private UIGroupPrefab _group; private Entity _source; private bool _created;

        internal PrefabBase FindAsset(string name) { for(int i=0;i<Names.Length;i++)if(Names[i]==name)return _assets[i];return null; }

        internal static byte Mask(string name) { for (int i = 0; i < 3; i++) if (name == Names[i]) return (byte)(i + 1); return 0; }

        protected override void OnCreate() { base.OnCreate(); _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>(); _sources = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<PlaceableNetData>()); }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            if ((mode & GameMode.GameOrEditor) != GameMode.None) Enabled = true;

        }

        protected override void OnUpdate()

        {

            if (!_created)

            {

                using (var items = _sources.ToEntityArray(Allocator.Temp)) foreach (var entity in items)

                    {

                        if (!_prefabs.TryGetPrefab<NetPrefab>(entity, out var source) || source == null || source.name != "Crosswalk") continue;

                        var original = source.GetComponent<UIObject>(); if (original == null || original.m_Group == null) continue;

                        _group = original.m_Group; _source = entity; var icons = new[] { "HideArrows.svg", "HideSpeed.svg", "HideBoth.svg", "HideNodeLines.svg", "FarSideLights.svg", "FarSideLights.svg", "FarSideLights.svg", "FarSideLights.svg", "CurbsideDecals.svg", "VisualPowerlines.svg", "NoParking.svg", "TwoWayStop.svg", "OneWayStop.svg", "HideCenterline.svg", "TwoWayYield.svg", "OneWayYield.svg", "SingleYellow.svg", "DoubleYellow.svg", "CenterSingleWhite.svg", "CenterDoubleWhite.svg", "CenterSingle.svg", "CenterDouble.svg", "StreetLights.svg", "HideAllLines.svg", "HideLaneLines.svg", "HideEdgeLines.svg", "ResetLines.svg", "ResetParking.svg", "ResetSection.svg", "InspectRoad.svg", "JunctionEdges.svg", "HideNativeLines.svg", "LaneDividers.svg", "LaneDividers.svg", "LaneDividers.svg", "SectionEdges.svg", "YellowEdges.svg", "DoubleWhiteEdges.svg", "DoubleYellowEdges.svg", "ParkingTs.svg", "ParkingNotched.svg", "ParkingHatch.svg", "YellowDashStandard.svg", "YellowDashShort.svg", "YellowDashLong.svg","HideAllLines.svg","ParkingHatchYellow.svg","PassingWhite.svg","PassingYellow.svg" };

                        for (int i = 0; i < Names.Length; i++) { var asset = source.Clone(Names[i]); var ui = asset.GetComponent<UIObject>(); ui.m_Group = null; ui.m_Priority = original.m_Priority + 20 + i; ui.m_Icon = "coui://mgr-icons/" + icons[i]; _assets[i] = asset; _prefabs.AddPrefab(asset); }

                        _created = true; break;

                    }

            }

            if (!_created || !_prefabs.TryGetEntity(_group, out var group) || !EntityManager.HasBuffer<UIGroupElement>(group)) return;

            for (int i = 0; i < Names.Length; i++)

            {

                if (!_prefabs.TryGetEntity(_assets[i], out var entity) || !EntityManager.HasComponent<UIObjectData>(entity)) return;


                var elements=EntityManager.GetBuffer<UIGroupElement>(group);for(int index=elements.Length-1;index>=0;index--)if(elements[index].m_Prefab==entity)elements.RemoveAt(index);

                var data = EntityManager.GetComponentData<UIObjectData>(entity); data.m_Group = Entity.Null; EntityManager.SetComponentData(entity, data);

                bool locked = EntityManager.HasComponent<Locked>(_source) && EntityManager.IsComponentEnabled<Locked>(_source);

                if (EntityManager.HasComponent<Locked>(entity)) EntityManager.SetComponentEnabled<Locked>(entity, locked);

                if (!locked && !EntityManager.HasComponent<Unlock>(entity)) EntityManager.AddComponent<Unlock>(entity);

            }

            Mod.Debug("Curbside tools registered for the dedicated panel."); Enabled = false;

        }

    }

}
