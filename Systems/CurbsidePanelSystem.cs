using Colossal.UI.Binding;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbsidePanelSystem : UISystemBase
    {
        internal sealed class Entry
        {
            internal readonly string Asset,Title,Group,Icon,Hint;
            internal Entry(string asset,string title,string group,string icon,string hint){Asset=asset;Title=title;Group=group;Icon=icon;Hint=hint;}
        }
        internal static readonly Entry[] Entries={
            new Entry(CrosswalkUpgradeSystem.LineName,"Lined crosswalk","Crosswalks","LineCrosswalk.svg","One crossing. Click creates or converts; right-click removes the crossing."),
            new Entry(CrosswalkUpgradeSystem.InvisibleName,"Invisible crosswalk","Crosswalks","InvisibleCrosswalk.svg","One crossing. Click keeps pedestrian function without paint; right-click restores previous paint."),
            new Entry(CrosswalkUpgradeSystem.MarkedName,"Marked parking bays","Parking","ParkingMarked.svg","One roadside. Fixed bays and boundary/T paint; right-click restores native layout."),
            new Entry(CrosswalkUpgradeSystem.UnmarkedName,"Unmarked parking bays","Parking","ParkingUnmarked.svg","One roadside. Spacing and junction setback without bay paint; right-click restores native layout."),
            new Entry(CrosswalkUpgradeSystem.TOnlyName,"T-mark parking bays","Parking","ParkingTs.svg","One roadside. Fixed bays with T markers; right-click restores native layout."),
            new Entry(CrosswalkUpgradeSystem.NotchedName,"Parking dividers to curb","Parking","ParkingNotched.svg","One roadside. Fixed bays with dividers; right-click restores native layout."),
            new Entry(ParkingBanPolicy.AssetName,"No parking","Parking","NoParking.svg","One roadside. Bans parking and ensures a curb line; right-click restores parking and its prior bay style."),
            new Entry(CenterlineUpgradePolicy.Single,"Single centerline","Markings","SingleYellow.svg","Section or supported node. Adds or replaces the center marking, or median pavement edges, in the resolved theme; right-click removes the centerline, including native paint."),
            new Entry(CenterlineUpgradePolicy.Double,"Double centerline","Markings","DoubleYellow.svg","Section or supported node. Adds or replaces paired center/median pavement markings in the resolved theme; right-click removes the centerline, including native paint."),
            new Entry(StreetLightingSystem.AssetName,"Street lights","Decoration","StreetLights.svg","Hover a segment or junction to target its lamps independently. Click enables authored lighting; right-click hides only lamps owned by that target. Traffic signals are unchanged."),
            new Entry(CenterlineVisibility.AssetName,"Hide centerline","Visibility","HideCenterline.svg","Section or node. Hides native and existing Curbside center paint; later additions can replace it. Right-click restores underlying center markings."),
            new Entry(NodeLineUpgradeTool.AssetName,"Hide junction lines","Visibility","HideNodeLines.svg","One junction and its connections. Keeps crossings and stop bars; right-click restores lines."),
            new Entry("RDC Hide Arrows","Hide lane arrows","Visibility","HideArrows.svg","One roadside. Right-click restores arrows; other markings remain."),
            new Entry("RDC Hide Speed Decals","Hide speed decals","Visibility","HideSpeed.svg","One roadside. Right-click restores speed decals."),
            new Entry("RDC Hide Both","Hide arrows and speed decals","Visibility","HideBoth.svg","One roadside. Right-click restores these decals."),
            new Entry(ApproachStopTool.SingleAssetName,"One-way stop","Traffic","OneWayStop.svg","One junction approach. Right-click clears that approach; signal junctions are excluded."),
            new Entry(ApproachStopTool.AssetName,"Two-way stops","Traffic","TwoWayStop.svg","Selected approach and aligned opposite. Right-click clears that pair; signal junctions are excluded."),
            new Entry(ApproachStopTool.SingleYieldAssetName,"One-way yield","Traffic","OneWayYield.svg","One junction approach. Right-click clears that approach; signal junctions are excluded."),
            new Entry(ApproachStopTool.YieldAssetName,"Two-way yields","Traffic","TwoWayYield.svg","Selected approach and aligned opposite. Right-click clears that pair; signal junctions are excluded."),
            new Entry(GardenCityLightPolicy.AssetName,"Garden City traffic lights","Traffic","FarSideLights.svg","One junction. Fits Garden City signals to approaching lanes and places them far side. Right-click removes traffic lights."),
            new Entry(GardenCityLightPolicy.WireAsset,"Span-wire traffic lights","Traffic","FarSideLights.svg","One junction. Suspended vertical signals over approaching lanes. Right-click removes traffic lights."),
            new Entry(GardenCityLightPolicy.MastAsset,"Rigid mast-arm traffic lights","Traffic","FarSideLights.svg","One junction. Galvanized mast arms and yellow vertical signals, fitted to approaching lanes. Right-click removes traffic lights."),
            new Entry(FarSideLightUpgradeTool.AssetName,"Far-side traffic lights","Traffic","FarSideLights.svg","One junction. Adds lights if needed and moves them far side; right-click removes traffic lights."),
            new Entry(RoadSideUpgrades.DecalAsset,"Road-wear decals","Decoration","CurbsideDecals.svg","Selected section, using the both-sides setting. Right-click removes road wear."),
            new Entry(VisualPowerlineSystem.AssetName,"Decorative powerlines","Decoration","VisualPowerlines.svg","One roadside. Decorative poles and wires only; right-click removes the selected run."),
            new Entry(SegmentUpgradePolicy.All,"Hide all road lines","Visibility","HideAllLines.svg","Selected section or node: native and existing Curbside lane, center and edge paint. Keeps crossings and stop bars. Right-click restores visibility."),
            new Entry(SegmentUpgradePolicy.Lanes,"Hide lane and edge lines","Visibility","HideLaneLines.svg","Selected section or node: native and existing Curbside divider/edge paint hidden; center and median kept. Right-click restores visibility."),
            new Entry(SegmentUpgradePolicy.Edges,"Hide edge lines","Visibility","HideEdgeLines.svg","Native and existing Curbside section or node edge paint hidden; later additions can replace it. Right-click restores visibility."),
            new Entry(SegmentUpgradePolicy.ResetLines,"Restore line defaults","Restore","ResetLines.svg","Click clears selected section or node line style and visibility overrides. Keeps parking behavior and global settings. Right-click does nothing."),
            new Entry(SegmentUpgradePolicy.ResetParking,"Restore parking defaults","Restore","ResetParking.svg","Click restores native parking on both sides and removes Curbside bay/no-parking upgrades. Right-click does nothing."),
            new Entry(SegmentUpgradePolicy.ResetAll,"Reset Curbside upgrades","Restore","ResetSection.svg","Click clears this section's Curbside upgrades and its approach choices. Shared junction hide/reverse upgrades also reset. Keeps native crossings and global settings. Right-click does nothing."),
            new Entry(RoadFeatureInspector.AssetName,"Inspect road","Markings","InspectRoad.svg","Hover a road to read applied Curbside tools. Click and right-click do not change it."),
            new Entry(NodeLineUpgradeTool.EdgeAssetName,"Junction edge lines","Markings","JunctionEdges.svg","One junction. Connects road edge lines at their curb offset, including over hidden native junction paint. Right-click removes junction edge paint, including native lines."),
            new Entry(SegmentUpgradePolicy.Native,"Hide vanilla lines","Visibility","HideNativeLines.svg","Section or selected node. Hides native road-line paint while keeping Curbside additions, crossings and stop bars. Right-click restores native paint."),
            new Entry(CenterlineUpgradePolicy.WhiteSingle,"White line","Markings","CenterSingleWhite.svg","Hover the center, either edge or lane dividers to select it. Works on sections and supported junction/transition paths. Click adds; right-click removes only the highlighted line category, including native paint."),
            new Entry(CenterlineUpgradePolicy.WhiteDouble,"Double white line","Markings","CenterDoubleWhite.svg","Hover the center, either edge or lane dividers to select it. Works on sections and supported junction/transition paths. Click adds; right-click removes only the highlighted line category, including native paint."),
            new Entry(CenterlineUpgradePolicy.YellowSingle,"Yellow line","Markings","CenterSingle.svg","Hover the center, either edge or lane dividers to select it. Works on sections and supported junction/transition paths. Click adds; right-click removes only the highlighted line category, including native paint."),
            new Entry(CenterlineUpgradePolicy.YellowDouble,"Double yellow line","Markings","CenterDouble.svg","Hover the center, either edge or lane dividers to select it. Works on sections and supported junction/transition paths. Click adds; right-click removes only the highlighted line category, including native paint."),
            new Entry(LaneDividerPolicy.Short,"White short-dash line","Markings","WhiteDashShort.svg","Hover center, edges or dividers on a section or supported node. Click adds short white dashes; right-click removes highlighted category."),
            new Entry(LaneDividerPolicy.Long,"White long-dash line","Markings","WhiteDashLong.svg","Hover center, edges or dividers on a section or supported node. Click adds long white dashes; right-click removes highlighted category."),
            new Entry(LaneDividerPolicy.AssetName,"White standard-dash line","Markings","WhiteDashStandard.svg","Hover center, edges or dividers on a section or supported node. Click adds white standard dashes; right-click removes highlighted category."),
            new Entry(SectionEdgePolicy.AssetName,"White edge line","Markings","SectionEdges.svg","Hover one section side or a whole intersection/transition node. Click applies white edge paint; right-click removes its edge category, including native paint."),
            new Entry(EdgeUpgradePolicy.Yellow,"Yellow edge line","Markings","YellowEdges.svg","One section side or a whole intersection/transition node. Click applies; right-click removes edge paint."),
            new Entry(EdgeUpgradePolicy.DoubleWhite,"Double white edge line","Markings","DoubleWhiteEdges.svg","One section side or a whole intersection/transition node. Click applies; right-click removes edge paint."),
            new Entry(EdgeUpgradePolicy.DoubleYellow,"Double yellow edge line","Markings","DoubleYellowEdges.svg","One section side or a whole intersection/transition node. Click applies; right-click removes edge paint."),
            new Entry(CrosswalkUpgradeSystem.FullTName,"Full-depth T parking bays","Parking","ParkingTs.svg","One roadside. Full-depth T dividers without a continuous lane line. Right-click restores native bays."),
            new Entry(CrosswalkUpgradeSystem.DividerOnlyName,"Parking dividers only","Parking","ParkingNotched.svg","One roadside. Transverse bay dividers without a continuous lane line. Right-click restores native bays."),
            new Entry(ParkingHatchPolicy.AssetName,"Parking-end hatching","Parking","ParkingHatch.svg","One roadside. End length uses Parking setback in settings, without whole-bay rounding. Paints excluded space only; optional commercial entrances. Right-click removes hatching only. Does not change parking/access.")
            ,new Entry(LaneDividerPolicy.Yellow,"Yellow standard-dash line","Markings","YellowDashStandard.svg","Hover center, edges or dividers on a section or supported node. Click adds yellow standard dashes; right-click removes highlighted category."),
            new Entry(LaneDividerPolicy.YellowShort,"Yellow short-dash line","Markings","YellowDashShort.svg","Hover center, edges or dividers on a section or supported node. Click adds yellow short dashes; right-click removes highlighted category."),
            new Entry(LaneDividerPolicy.YellowLong,"Yellow long-dash line","Markings","YellowDashLong.svg","Hover center, edges or dividers on a section or supported node. Click adds yellow long dashes; right-click removes highlighted category.")
            ,new Entry(LineShapePolicy.Visibility,"Hide selected line","Visibility","HideAllLines.svg","Hover center, either edge or lane dividers on a section or supported node. Click hides highlighted category including Curbside paint; right-click restores its default paint."),
            new Entry(ParkingHatchPolicy.Yellow,"Yellow parking-end hatching","Parking","ParkingHatchYellow.svg","One roadside. Yellow end hatching uses the same setback and access rules. Right-click removes hatching of either color on this side; parking kept.")
            ,new Entry(PassingLinePolicy.White,"White passing line","Markings","PassingWhite.svg","Hover center, edges or dividers on roads or supported nodes. Click adds solid/dashed pair with dashes toward the road or node center. Right-click removes highlighted category."),
            new Entry(PassingLinePolicy.Yellow,"Yellow passing line","Markings","PassingYellow.svg","Hover center, edges or dividers on roads or supported nodes. Click adds solid/dashed pair with dashes toward the road or node center. Right-click removes highlighted category.")
        };
        private ToolSystem _tools;private PrefabSystem _prefabs;
        private ValueBinding<string> _features;private float _nextInspect;
        private ValueBinding<bool> _diagnostics;private RawValueBinding _catalog;private ValueBinding<string> _status;private ValueBinding<int> _active;
        private RawValueBinding _translations;
        private int _translationRevision = -1;
        private bool _open;private float _nextRefresh,_noticeUntil;
        private bool _signalMenuReadyLogged;
        public override GameMode gameMode=>GameMode.GameOrEditor;
        protected override void OnCreate()
        {
            base.OnCreate();_tools=World.GetOrCreateSystemManaged<ToolSystem>();_prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
            AddBinding(_features=new ValueBinding<string>("curbside","roadFeatures","Choose Inspect road and hover a section."));
            AddBinding(_diagnostics=new ValueBinding<bool>("curbside","diagnosticsEnabled",ModDiagnostics.TraceBuffer.Enabled));
            AddBinding(_translations=new RawValueBinding("curbside","translations",WriteTranslations));
            AddBinding(_catalog=new RawValueBinding("curbside","tools",WriteCatalog));
            AddBinding(_status=new ValueBinding<string>("curbside","status","Choose a tool. Click applies; right-click removes or restores."));
            AddBinding(_active=new ValueBinding<int>("curbside","active",-1));
            AddBinding(new TriggerBinding<bool>("curbside","setOpen",SetOpen));
            AddBinding(new TriggerBinding<int>("curbside","select",Select));
            AddBinding(new TriggerBinding("curbside","leaveTool",Leave));
            AddBinding(new TriggerBinding("curbside","saveWithoutPreview",()=>DiagnosticSave.Request(World,message=>{_noticeUntil=UnityEngine.Time.realtimeSinceStartup+30;_status.Update(message);})));
            AddBinding(new TriggerBinding<bool>("curbside","diagnostics",v=>{ModDiagnostics.TraceBuffer.SetEnabled(v);ModDiagnostics.RuntimeCosts.Reset();_diagnostics.Update(v);_noticeUntil=UnityEngine.Time.realtimeSinceStartup+4;_status.Update(v?"Diagnostics enabled. Reproduce, then write a snapshot.":"Diagnostics off.");}));
            AddBinding(new TriggerBinding("curbside","snapshot",()=>{World.GetOrCreateSystemManaged<FarSideLightSystem>().RecordSnapshot();World.GetOrCreateSystemManaged<SignalStreetNameSystem>().ExportDiagnostics();var crossings=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();ModDiagnostics.TraceBuffer.Record("snapshot.state","saving="+Mod.Saving+" tool="+_tools.activeTool?.toolID+" crossing="+crossings.CrossingStatus);var paint=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();ModDiagnostics.TraceBuffer.Record("snapshot.paint","singleYellow="+(paint.Explicit(1,true)!=Entity.Null)+" doubleYellow="+(paint.Explicit(2,true)!=Entity.Null)+" singleWhite="+(paint.Explicit(1,false)!=Entity.Null)+" doubleWhite="+(paint.Explicit(2,false)!=Entity.Null));ModDiagnostics.TraceBuffer.Record("snapshot.surface",GlobalSurfaceMaterialSystem.Status);ModDiagnostics.TraceBuffer.Record("snapshot.discovery",paint.DiscoveryStatus+" divider="+(paint.Divider!=Entity.Null));World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RecordPaintSnapshot();Mod.Log.Info(GardenCityLightPolicy.CacheStatus);Mod.Log.Info(World.GetOrCreateSystemManaged<SignalStreetNameSystem>().CostStatus);Mod.Log.Info(ModDiagnostics.RuntimeCosts.Snapshot());Mod.Log.Info(ModDiagnostics.TraceBuffer.Snapshot("The Curbside Mod "+BuildIdentity.Label));_noticeUntil=UnityEngine.Time.realtimeSinceStartup+4;_status.Update("Diagnostic snapshot written to the existing MedianGrassReplacement log.");}));
        }
        private PrefabBase Find(string name)=>World.GetOrCreateSystemManaged<UpgradeAssetsSystem>().FindAsset(name)??World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().FindAsset(name);
        private bool Available(PrefabBase prefab)
        {
            return prefab!=null&&_prefabs.TryGetEntity(prefab,out var entity)&&EntityManager.Exists(entity)&&!EntityManager.HasComponent<Deleted>(entity)&&(!EntityManager.HasComponent<Locked>(entity)||!EntityManager.IsComponentEnabled<Locked>(entity));
        }
        internal static bool CatalogVisible(string asset)=>asset!=NodeLineUpgradeTool.EdgeAssetName&&asset!=NodeLineUpgradeTool.AssetName&&SurfaceUpgradePolicy.Slot(asset)==0&&asset!=CenterlineUpgradePolicy.Single&&asset!=CenterlineUpgradePolicy.Double&&EdgeUpgradePolicy.Style(asset)==0&&asset!=CenterlineVisibility.AssetName&&asset!=SegmentUpgradePolicy.Edges&&asset!=SegmentUpgradePolicy.Lanes;
        internal static int CatalogCount(){int count=0;foreach(var entry in Entries)if(CatalogVisible(entry.Asset))count++;return count;}
        private void WriteCatalog(IJsonWriter writer)
        {
            bool optional=GardenCityLightPolicy.Ready(_prefabs,EntityManager);
            bool present=optional||GardenCityLightPolicy.PackPresent();
            if(optional&&!_signalMenuReadyLogged){_signalMenuReadyLogged=true;Mod.Log.Info("Signal menu ready: game uptime seconds="+UnityEngine.Time.realtimeSinceStartup);}
            int count=0;foreach(var e in Entries)if(CatalogVisible(e.Asset)&&(!GardenCityLightPolicy.IsAsset(e.Asset)||present))count++;
            writer.ArrayBegin(count);
            for(int i=0;i<Entries.Length;i++){var e=Entries[i];bool signal=GardenCityLightPolicy.IsAsset(e.Asset);if(!CatalogVisible(e.Asset)||(signal&&!present))continue;writer.TypeBegin("curbside.Tool");writer.PropertyName("id");writer.Write(i);writer.PropertyName("title");writer.Write(e.Title+(signal&&!optional?" (loading�)":""));writer.PropertyName("group");writer.Write(e.Group);writer.PropertyName("icon");writer.Write("coui://mgr-icons/"+e.Icon);writer.PropertyName("hint");writer.Write(signal&&!optional?"Signal pack is loading. This upgrade will become available automatically.":e.Hint);writer.PropertyName("available");writer.Write(e.Asset!=NodeSplitTool.AssetName&&(!signal||optional)&&Available(Find(e.Asset)));writer.TypeEnd();}writer.ArrayEnd();
        }
        private static void WriteCatalogEntries(IJsonWriter writer,System.Func<Entry,bool> available)
        {
            writer.ArrayBegin(CatalogCount());
            for(int i=0;i<Entries.Length;i++)
            {
                var e=Entries[i];if(!CatalogVisible(e.Asset))continue;writer.TypeBegin("curbside.Tool");writer.PropertyName("id");writer.Write(i);writer.PropertyName("title");writer.Write(e.Title);writer.PropertyName("group");writer.Write(e.Group);writer.PropertyName("icon");writer.Write("coui://mgr-icons/"+e.Icon);writer.PropertyName("hint");writer.Write(e.Hint);writer.PropertyName("available");writer.Write(available(e));writer.TypeEnd();
            }
            writer.ArrayEnd();
        }
        private void WriteTranslations(IJsonWriter writer)
        {
            var table = Localization.LocalizationService.Selected == "en-US" ? null : Localization.LocalizationService.Table("en-US");
            writer.MapBegin(table?.Count ?? 0);
            if(table != null) foreach(var pair in table) { writer.Write(pair.Key); writer.Write(Localization.LocalizationService.NativeText(pair.Key)); }
            writer.MapEnd();
        }
        private void SetOpen(bool open){_open=open;if(open){_catalog.Update();_nextRefresh=UnityEngine.Time.realtimeSinceStartup+2;}}
        private void Select(int index)
        {
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("tool.select","index="+index+" saving="+Mod.Saving);
            if(Mod.Saving||(uint)index>=(uint)Entries.Length)return;
            var entry=Entries[index];if(entry.Asset==NodeSplitTool.AssetName){_status.Update("Split road section is temporarily unavailable while building access is repaired.");return;}var asset=Find(entry.Asset);
            if(!Available(asset)){_status.Update("Tool unavailable: the native road-service asset is still loading or locked.");_catalog.Update();return;}
            var crossings=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            bool activated=entry.Asset==CrosswalkUpgradeSystem.LineName?crossings.LaunchNativeCrosswalk()
                :entry.Asset==CrosswalkUpgradeSystem.InvisibleName?crossings.LaunchNativeInvisibleCrosswalk()
                :_tools.ActivatePrefabTool(asset);
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("tool.activation",entry.Asset+" activated="+activated);
            if(activated){_status.Update(entry.Hint);_active.Update(index);}else _status.Update("This tool could not be activated. Try again after the city finishes loading.");
        }
        private void Leave(){if(Mod.Saving)return;_tools.ActivatePrefabTool(null);_active.Update(-1);_status.Update("Tool closed. Existing upgrades are unchanged.");}
        protected override void OnGamePreload(Purpose purpose,GameMode mode){base.OnGamePreload(purpose,mode);_open=false;_active.Update(-1);_nextInspect=0;_features.Update("Choose Inspect road and hover a section.");}
        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("CurbsidePanelSystem",started);}
        }
        private void UpdateMeasured()
        {
            base.OnUpdate();
            if(_translationRevision != Localization.LocalizationService.Revision) { _translationRevision = Localization.LocalizationService.Revision; _translations.Update(); }
            if(!Mod.Saving&&UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)
            {
                string asset=_tools.activeTool?.GetPrefab()?.name;
                bool own=false;foreach(var entry in Entries)if(entry.Asset==asset){own=true;break;}
                if(asset=="Crosswalk"){var crossings=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();own=crossings.NativeLineMode||crossings.NativeInvisibleMode;}
                if(own){Leave();return;}
            }
            if(!_open)return;
            if(!Mod.Saving&&!UpgradeInputPolicy.PointerBlocked&&UnityEngine.Time.realtimeSinceStartup>=_nextInspect){_nextInspect=UnityEngine.Time.realtimeSinceStartup+.25f;var ray=World.GetOrCreateSystemManaged<ToolRaycastSystem>();if(ray.GetRaycastResult(out var hit)&&SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var road))_features.Update(RoadFeatureInspector.Describe(EntityManager,road));else _features.Update("No road highlighted. Choose Inspect road and hover a section.");}
            var current=_tools.activeTool?.GetPrefab()?.name;int selected=-1;
            for(int i=0;i<Entries.Length;i++)if(Entries[i].Asset==current){selected=i;break;}
            if(current=="Crosswalk") {var crossing=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();if(crossing.NativeLineMode)selected=0;else if(crossing.NativeInvisibleMode)selected=1;if(UnityEngine.Time.realtimeSinceStartup>=_noticeUntil)_status.Update(crossing.CrossingStatus);}
            if(current==UpgradeFeedback.Asset&&UpgradeFeedback.Text!=null&&UnityEngine.Time.realtimeSinceStartup>=_noticeUntil)_status.Update(UpgradeFeedback.Text);
            _active.Update(selected);
            if(UnityEngine.Time.realtimeSinceStartup>=_nextRefresh){_nextRefresh=UnityEngine.Time.realtimeSinceStartup+2;_catalog.Update();}
        }
    }
}


