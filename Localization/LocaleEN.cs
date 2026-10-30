using System.Collections.Generic;

using Colossal;

namespace MedianGrassReplacement.Localization

{

    public sealed class LocaleEN : IDictionarySource

    {

        private readonly Setting _setting;

        public LocaleEN(Setting setting) { _setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> counts)

        {

            var entries = new Dictionary<string, string> { { _setting.GetSettingsLocaleID(), "The Curbside Mod" } };

            void Tab(string id, string label) => entries.Add(_setting.GetOptionTabLocaleID(id), label);

            void Group(string id, string label) => entries.Add(_setting.GetOptionGroupLocaleID(id), label);

            void Option(string id, string label, string help) { entries.Add(_setting.GetOptionLabelLocaleID(id), label); entries.Add(_setting.GetOptionDescLocaleID(id), help); }

            void Asset(string id, string label, string help) { entries["Assets.NAME[" + id + "]"]=label; entries["Assets.DESCRIPTION[" + id + "]"]=help; }
            entries[_setting.GetOptionTabLocaleID(Setting.kHighwaysTab)]="Highway markings";
            entries[_setting.GetOptionGroupLocaleID(Setting.kCompatibilityGroup)]="Compatibility controls";
            entries[_setting.GetOptionGroupLocaleID(Setting.kHighwayControlGroup)]="Automatic highway markings";
            Option(nameof(Setting.DisableAutomaticMarkings),"Disable automatic road lines","Keep manual line, parking, crossing and visibility upgrades. Stop automatically added curb/median lines; saved upgrade choices remain intact. Default: off.");
            Option(nameof(Setting.DisableAutomaticHighwayMarkings),"Disable automatic highway lines","Keep vanilla highway markings and your manual upgrades; stop automatic highway additions at sections, nodes and junctions. Default: off.");
            Option(nameof(Setting.DisableAutomaticLineAdjustments),"Disable automatic line adjustments","Stop unsolicited native line recoloring and junction cleanup. Explicit manual upgrades and visibility choices remain active. Default: off.");
            Option(nameof(Setting.DisableTextureReplacement),"Disable surface texture replacements","Restore Curbside-owned road, sidewalk and grass surface changes. Keep your texture selections for re-enabling. Decorative road-wear decals are separate upgrades. Default: off.");


            Asset(MedianGrassReplacement.Systems.ParkingBanPolicy.AssetName, "Ban parking on this side", "Click near one side of a road to ban parking there. Right-click restores parking and the previous bay layout. Existing parked cars are not moved.");
            Asset(MedianGrassReplacement.Systems.CenterlineVisibility.AssetName,"Hide centerline","Click a road section to hide its opposing-traffic center and yellow median-edge lines. Right-click restores normal markings. Manual Town Road Lane paint is preserved.");
            Asset(MedianGrassReplacement.Systems.ApproachStopTool.YieldAssetName,"Two-way yields","Click to add yield control to this approach and its aligned opposite. Right-click removes those controls. Other junctions are unchanged.");
            Asset(MedianGrassReplacement.Systems.ApproachStopTool.SingleYieldAssetName,"Single-approach yield","Click to add yield control to this approach. Right-click removes it. Other approaches and junctions are unchanged.");
            Asset("MGR Visual Roadside Powerlines", "Roadside powerlines (visual only)", "Click near a curb to add native poles and wires on that side. Right-click to remove. Upgraded approaches connect at junctions. Does not carry electricity.");

            Group(Setting.kSurfaceGroup,"Road and sidewalk surfaces");
            Option(nameof(Setting.RoadSurfaceUv),"Road texture tiling","World-space UV scale for the selected global road texture. Higher values repeat the texture more often. Default 0.16. Updates cached materials without loading images again. Vanilla restores the native scale.");
            Option(nameof(Setting.SidewalkSurfaceTiling),"Sidewalk texture tiling","Multiplier for the native sidewalk texture tiling. Higher values repeat the texture more often. Default 1.00. Retained normal/mask maps follow the same multiplier. Applies only with a global sidewalk texture selected.");
            Option(nameof(Setting.GlobalRoadSurface),"Global road surface","Replace supported road/highway asphalt and asset pavement/parking surfaces throughout the city with slot A or B. Vanilla restores materials still owned by Curbside. Rendering only; networks and saves are unchanged.");
            Option(nameof(Setting.GlobalSidewalkSurface),"Global sidewalk surface","Replace supported network and asset sidewalk materials throughout the city. Building facades, curbs and grass are kept. Vanilla restores Curbside-owned changes.");
            Option(nameof(Setting.GlobalRoadTexture),"Global road surface","Choose a texture from the shared texture library. Vanilla restores the native road material. This selector is independent of sidewalk and grass textures.");
            Option(nameof(Setting.GlobalSidewalkTexture),"Global sidewalk surface","Choose a texture from the shared texture library. Vanilla restores the native sidewalk material. This selector is independent of the road and grass selections.");
            Option(nameof(Setting.SurfaceTexturesOnAssets),"Apply road and sidewalk textures to assets and surfaces","Also replace recognizable pavement, parking-area and sidewalk surface materials on assets. Disable to limit replacement to networks. Default on.");
            Option(nameof(Setting.ApplySurfaceTextures),"Apply surface textures","Refresh the shared texture list and reload the selected road and sidewalk images.");
            Option(nameof(Setting.SurfaceTextureStatus),"Surface status","Material counts, cache estimate and loading errors for global road and sidewalk textures.");
            Option(nameof(Setting.HatchAtBuildingEntrances),"Hatching at building entrances","When a Parking-end hatching upgrade is applied, include parking-strip space excluded by native commercial building access. Visual only; does not change access or parking. Default on.");
            Option(nameof(Setting.CloseParkingAtBuildingEntrances),"Close parking markings at building entrances","Split bay paint at native building-access exclusions and add terminal marks there. Does not change actual bay placement or availability. Default off; intersection terminals remain.");
            Option(nameof(Setting.PowerlineHeight), "Powerline height (m)", "Resize decorative poles vertically while keeping their bases grounded. Wire terminals scale with the pole. Range 5-100 m; default 8 m. Existing upgraded runs rebuild.");

            Option(nameof(Setting.PowerlinePoleSpacing), "Powerline pole spacing (m)", "Target spacing along each complete road section. Both geometry halves share one spacing calculation. Longer spacing uses fewer poles. Existing upgrades rebuild when changed.");

            Option(nameof(Setting.PowerlineMinimumGap), "Minimum pole gap along a run (m)", "Avoid extra poles on short sections and reuse close poles along the same roadside run. Opposite curbs and distinct corners remain separate. Default 10 m.");

            Option(nameof(Setting.PowerlineCornerSetback), "Powerline corner setback (m)", "Keep poles back along the roadside from junction mouths and crosswalks. Default 6 m. Very short approaches may be skipped.");

            Option(nameof(Setting.PowerlineStatus), "Powerline status", "Visual-only poles and wires. Saved road selections regenerate while this mod is enabled; the power grid is unchanged.");

            Option(nameof(Setting.OpenTextureFolder),"Open textures folder","Open the shared grass, road and sidewalk texture library. Add PNG files, then use Refresh texture list.");
            Tab(Setting.kTexturesTab,"Textures"); Tab(Setting.kMainTab,"Road markings"); Tab(Setting.kAdvancedTab,"Advanced"); Tab(Setting.kAboutTab,"About");
            Group(Setting.kMedianGroup, "Center and median lines");

            Option(nameof(Setting.CenterLineStyle), "Centerline style", "Automatic follows the marking theme. Choose Vanilla or a compatible installed line pattern. European theme maps identified yellow paint to a white counterpart. Same-direction dividers are unchanged.");

            Option(nameof(Setting.AlignMedianStopBar), "Align median line with stop bar", "End the median line at the approach-side edge of its stop bar, plus the end gap. Does not move or extend stop bars. Default on.");







            Option(nameof(Setting.MedianEdgeLines), "Show median edge lines", "Add separate lines on the pavement beside composed raised medians. Independent of white curb edge lines. No new paint in lanes marked No markings.");

            Option(nameof(Setting.MedianLineOffset), "Distance from median curb (m)", "Distance to the line centre, measured into the roadway. Default 0.30 m.");

            Option(nameof(Setting.MedianLineSetback), "Median line end gap (m)", "Extra clearance from the stop bar or segment end. Default 0.05 m. Does not extend stop bars.");

            Option(nameof(Setting.MedianLineStyle), "Median line style", "Automatic follows the marking theme. Choose a compatible native or installed pattern independently of curb lines. European theme maps identified yellow paint to a white counterpart.");

            Group(Setting.kDecalGroup, "Curbside decal - slot 1");

            Option(nameof(Setting.CurbDecalsEnabled), "Show curbside decals", "Enable PNG decals on sides selected with the Curbside decals upgrade. No decals are added to unselected roads.");

            Option(nameof(Setting.CurbDecalFolder), "Decal folder", "Folder containing transparent PNG road-wear decals. Subfolders are not scanned.");

            Option(nameof(Setting.CurbDecalTexture), "Decal texture", "Select the PNG for this slot. The original alpha is used; normal and gloss changes are not applied.");

            Option(nameof(Setting.RefreshDecals), "Refresh decal textures", "Rescan this folder and reload the selected PNG after editing it.");

            Option(nameof(Setting.CurbDecalOffset), "Gap from curb (m)", "Gap from the curb to the nearest projected edge. Negative values move the decal across the curb. Range -50 to 50 m.");

            Option(nameof(Setting.CurbDecalSetback), "Intersection setback (m)", "Extra clearance from each junction end or its stop bar. The entire decal remains inside the retained road length.");

            Option(nameof(Setting.CurbDecalOverlap), "Overlap (%)", "Overlap between repeats. Higher overlap blends seams but adds decal rendering cost.");

            Option(nameof(Setting.CurbDecalWidth), "Decal width (m)", "Decal width, 0.1 to 100 m, before rotation.");

            Option(nameof(Setting.CurbDecalLength), "Decal height / length (m)", "Decal height/length, 0.1 to 100 m, before rotation. Longer repeats usually use fewer decals.");

            Option(nameof(Setting.ApplyExactDecalValues), "Apply exact values", "Validate all typed numbers, then apply them together. Invalid text is kept for correction. Sliders still apply immediately.");

            Option(nameof(Setting.ShowExactDecalValues), "Show exact value inputs", "Show editable numbers alongside the sliders. Finish typing, then click Apply exact values. The same limits apply.");

            Option(nameof(Setting.ExactDecalWidth), "Exact: Width (m)", "Type an exact value, then click Apply exact values. You can clear and replace the entire number. The slider limits still apply.");

            Option(nameof(Setting.ExactDecalLength), "Exact: Height / length (m)", "Type an exact value, then click Apply exact values. You can clear and replace the entire number. The slider limits still apply.");

            Option(nameof(Setting.ExactDecalOffset), "Exact: Curb gap (m)", "Type an exact value, then click Apply exact values. You can clear and replace the entire number. The slider limits still apply.");

            Option(nameof(Setting.ExactDecalSetback), "Exact: Intersection setback (m)", "Type an exact value, then click Apply exact values. You can clear and replace the entire number. The slider limits still apply.");

            Option(nameof(Setting.ExactDecalRotation), "Exact: Rotation (degrees)", "Type an exact value, then click Apply exact values. You can clear and replace the entire number. The slider limits still apply.");

            Option(nameof(Setting.ExactDecalOverlap), "Exact: Overlap (%)", "Type an exact value, then click Apply exact values. You can clear and replace the entire number. The slider limits still apply.");


            Option(nameof(Setting.DecalsBothSides), "Apply decals to both sides", "The curbside decal upgrade selects both curbs of the clicked road section. Right-click removes both. Disable to select the side nearest the pointer. Existing selections change only when you apply the tool.");

            Option(nameof(Setting.OrientDecalsToCurb), "Flip opposite side", "Add exactly 180 degrees to the opposite curb, at every rotation setting. Disable to use the same road-relative rotation on both sides.");

            Option(nameof(Setting.CurbDecalRotation), "Rotation (degrees)", "Rotate around the road surface normal. Full corners are checked against segment end clearance.");




            Option(nameof(Setting.CurbDecalStatus), "Decal status", "Current build or texture status. Changes are applied gradually to limit frame spikes.");

            Asset(MedianGrassReplacement.Systems.CrosswalkUpgradeSystem.InvisibleName, "Crosswalk: invisible", "Click to add a functional crossing or hide its paint. Right-click restores the previous visible style. Pedestrian routes remain native.");

            Asset(MedianGrassReplacement.Systems.CenterlineUpgradePolicy.Single,"Single yellow centerline","Click adds or replaces the center marking. Right-click restores native markings.");
            Asset(MedianGrassReplacement.Systems.CenterlineUpgradePolicy.Double,"Double yellow centerline","Click adds or replaces double-yellow center markings. Right-click restores native markings.");
            Asset(MedianGrassReplacement.Systems.StreetLightingSystem.AssetName,"Street lights","Click enables authored street lights on a road section. Right-click removes lighting; traffic signals stay unchanged.");
            Asset(MedianGrassReplacement.Systems.FarSideLightUpgradeTool.AssetName, "Traffic lights: far side", "Click to add native traffic lights and move supported vehicle signals to the far side. Right-click removes traffic lights from the junction.");


            Group(Setting.kLibraryGroup, "Curbside grass"); Group(Setting.kAppearanceGroup, "Edge lines"); Group(Setting.kParkingGroup, "Parking bays"); Group(Setting.kCrosswalkGroup, "Two-line crosswalks"); Group(Setting.kStopGroup, "Stop-line alignment"); Group(Setting.kMaintenanceGroup, "Refresh"); Group(Setting.kAboutGroup, "The Curbside Mod");

            Option(nameof(Setting.TextureFolder), "Texture library folder", "Choose the folder containing your PNG textures. A bounded set of subfolders is scanned; refresh the list after adding files.");

            Option(nameof(Setting.SelectedTexture), "Grass texture", "Replace grass on grassy sidewalks and road medians. Choose Vanilla to restore the original texture.");

            Option(nameof(Setting.RefreshTextureList), "Refresh texture list", "Update the list after adding or removing PNG files.");

            Option(nameof(Setting.UvScale), "Texture scale", "Lower values make the pattern larger. Default: 0.20. Updates immediately.");

            Option(nameof(Setting.CurbWhiteLines), "Show curb edge lines", "Add lines along the road-facing curb. Applies across supported roads.");

            Option(nameof(Setting.CurbLinesBesideParking), "Include parking sides", "Show edge lines beside parking too. Requires Show curb edge lines.");

            Option(nameof(Setting.CurbLineOffset), "Distance from curb (m)", "Distance to the line's centre on roads and enabled paved alleys. Default: 0.30 m.");

            Option(nameof(Setting.CurbLineStyle), "Line style", "Choose a compatible line asset supplied by the game or an installed asset pack.");

            Option(nameof(Setting.FixedParkingBayLength), "Marked bay length (m)", "Applies to marked parking upgrades. Range: 7–8 m; default: 7.5 m. Cars already parked move when they leave.");

            Option(nameof(Setting.ParkingIntersectionSetback), "Parking setback (m)", "Keep upgraded parking away from intersections. Also controls the exact maximum end-hatch length, without rounding to whole bays. Hatches only occupy excluded native parking space. Default: 6.5 m.");

            Option(nameof(Setting.CrosswalkUpgradeWidth), "Crossing width (m)", "Overall width between the two outer line edges. Only affects this mod's two-line crossings. Default: 3 m.");

            Option(nameof(Setting.LockCurbAndStopLines), "Join edge and stop lines", "Keep their endpoints joined. While enabled, the two gap settings below are ignored.");

            Option(nameof(Setting.StopLineCurbGap), "Stop-line gap from curb (m)", "Clearance for added stop-line extensions. Original stop bars are unchanged. Default: 0.15 m.");

            Option(nameof(Setting.CurbLineStopGap), "Edge-line setback (m)", "Pull edge-line ends back from the stop bar. Default: 0.05 m.");

            Option(nameof(Setting.RefreshRoadLines), "Refresh road markings", "Recheck this mod's markings after a road or asset change. Large cities may pause briefly.");

            Option(nameof(Setting.RefreshCurbLineStyles), "Refresh line styles", "Update the style list after loading a new marking asset pack.");

            Option(nameof(Setting.Version), "Version", "Installed version."); Option(nameof(Setting.Function), "Features", "Open Curbside to access road upgrades.");

            Asset("MGR Line Crosswalk Upgrade", "Crosswalk: two lines", "Left-click to add or restyle a crossing. Right-click to remove it. Select the game's Crosswalk tool for zebra paint.");

            Asset("MGR Marked Parking Upgrade", "Parking: boundary and T-marks", "Click near a curb to add marked bays on that side. Right-click that side to restore normal parking.");

            Asset("MGR T Only Parking Upgrade", "Parking: T-marks", "Click near a curb to add T-marked bays on that side. Right-click that side to restore normal parking.");

            Asset("MGR Notched Parking Upgrade", "Parking: dividers to curb", "Click near a curb to add bay dividers on that side. Right-click that side to restore normal parking.");

            Asset("MGR Unmarked Parking Upgrade", "Parking: unmarked setback", "Click near a curb to apply the parking setback on that side. Right-click that side to restore normal parking.");

            Asset("MGR Curbside Decals", "Curbside decals", "Click a road section to apply road-wear decals; right-click to remove. Applies to both sides by default. Disable Apply decals to both sides in settings for single-side selection.");

            Asset("RDC Hide Arrows", "Hide lane arrows", "Click near a curb to hide painted arrows on that side. Right-click that side to restore. Lane directions stay unchanged.");

            Asset("RDC Hide Speed Decals", "Hide speed-limit decals", "Click near a curb to hide painted speed numbers on that side. Right-click that side to restore. Roadside signs and speed limits stay unchanged.");

            Asset("RDC Hide Both", "Hide arrows and speed decals", "Click near a curb to hide both decal types on that side. Right-click that side to restore.");

            Asset("MGR Hide Node Lines", "Hide junction lines", "Left-click a junction or node to hide lane lines across its connections. Right-click to restore normal markings. Crosswalks and stop bars stay visible.");

            entries[_setting.GetOptionLabelLocaleID(nameof(Setting.AlleyWhiteEdges))]="White alley edges";
            entries[_setting.GetOptionDescLocaleID(nameof(Setting.AlleyWhiteEdges))]="Add white edges to built-in paved alleys. Uses the main edge-line distance and intersection gap controls. Dirt and gravel roads are excluded.";
            entries[_setting.GetOptionLabelLocaleID(nameof(Setting.AlleyDecalOffset))]="Alley decal distance";
            entries[_setting.GetOptionDescLocaleID(nameof(Setting.AlleyDecalOffset))]="Distance from the pavement edge on narrow, curbless roads. Negative values move outward.";
            entries[_setting.GetOptionLabelLocaleID(nameof(Setting.HighwayDecalOffset))]="Highway decal distance";
            entries[_setting.GetOptionDescLocaleID(nameof(Setting.HighwayDecalOffset))]="Distance from the traffic pavement edge on highways. Independent of ordinary roads.";
            Asset(MedianGrassReplacement.Systems.ApproachStopTool.AssetName,"Two-way stops","Click a road approach to add stops on that approach and its aligned opposite approach. Crossing approaches keep right of way. Click the crossing road to make four-way stops. Right-click removes the selected pair. Traffic-light junctions are excluded.");
            Asset(MedianGrassReplacement.Systems.ApproachStopTool.SingleAssetName,"One-way stop","Click to add a stop only to the selected approach. Right-click removes only that approach. Opposite and crossing approaches keep their existing settings. Traffic-light junctions are excluded.");
            Group(Setting.kAlleyGroup,"Paved alley edges");
            foreach(var tool in MedianGrassReplacement.Systems.CurbsidePanelSystem.Entries)Asset(tool.Asset,tool.Title,tool.Hint);
            entries[_setting.GetOptionLabelLocaleID(nameof(Setting.FollowCityMarkingTheme))]="Follow city marking theme";
            entries[_setting.GetOptionDescLocaleID(nameof(Setting.FollowCityMarkingTheme))]="Use white center and median lines in European-themed cities and yellow in North American cities. Applies to added markings and their previews. Turn off to choose manually below.";
            entries[_setting.GetOptionLabelLocaleID(nameof(Setting.ThemeAssetEdgeColors))]="Adapt coloured edge assets to marking theme";
            entries[_setting.GetOptionDescLocaleID(nameof(Setting.ThemeAssetEdgeColors))]="Convert identified yellow curb-edge assets to equivalent white paint in the European theme. Other colours are retained. Does not hide edge lines.";
            entries[_setting.GetOptionLabelLocaleID(nameof(Setting.EuropeanMarkings))]="European markings (white)";
            entries[_setting.GetOptionDescLocaleID(nameof(Setting.EuropeanMarkings))]="Off: North American yellow center/median markings. On: European-themed white center/median markings. Single/double count, placement, joins, clipping and saved upgrades are preserved. Outer edges stay white. Identified yellow custom/Vanilla patterns use white counterparts, or an equivalent native white pattern when needed. Other custom colours are retained. This visual theme does not change traffic rules.";
            return entries;

        }

        public void Unload() { }

    }

}



