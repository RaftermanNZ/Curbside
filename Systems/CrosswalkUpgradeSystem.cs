using System;

using System.Collections.Generic;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    public struct MGRCrosswalkChoices : IComponentData, IEmptySerializable { }

    public struct MGRCrosswalkChoice : IBufferElementData, ISerializable

    {

        public float3 Center, Axis;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Center); writer.Write(Axis); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Center); reader.Read(out Axis); }

    }

    public partial class CrosswalkUpgradeSystem : GameSystemBase

    {

        internal const string InvisibleName = "MGR Invisible Crosswalk Upgrade";

        internal PrefabBase InvisiblePrefab;

        internal bool NativeInvisibleMode { get; private set; }

        internal const string LineName = "MGR Line Crosswalk Upgrade", ZebraName = "MGR Zebra Crosswalk Upgrade";

        internal PrefabBase LinePrefab, MarkedParkingPrefab, UnmarkedParkingPrefab, TOnlyPrefab, NotchedPrefab;

        internal const string MarkedName = "MGR Marked Parking Upgrade", UnmarkedName = "MGR Unmarked Parking Upgrade";

        internal const string TOnlyName = "MGR T Only Parking Upgrade", NotchedName = "MGR Notched Parking Upgrade";

        internal const string FullTName="MGR Full Depth T Parking Upgrade",DividerOnlyName="MGR Divider Only Parking Upgrade";
        internal static bool IsParkingName(string name) => name == MarkedName || name == UnmarkedName || name == TOnlyName || name == NotchedName || name==FullTName || name==DividerOnlyName;

        internal PrefabBase FindAsset(string name) { foreach(var asset in Assets())if(asset!=null&&asset.name==name)return asset;return null; }

        private PrefabBase[] Assets() => new[] { LinePrefab, MarkedParkingPrefab, UnmarkedParkingPrefab, TOnlyPrefab, NotchedPrefab, InvisiblePrefab };

        internal PrefabBase NativeCrosswalkPrefab;

        internal bool NativeLineMode { get; private set; }
        internal string CrossingStatus { get; private set; }="Choose a crossing to change its appearance.";
        private Entity _statusRoad,_statusNode;
        internal void NoteRequest(Entity road,Entity node,string text){_statusRoad=road;_statusNode=node;CrossingStatus=text;if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("crosswalk.request",road+" node="+node+" "+text);}
        internal void NoteSource(Entity source,string text){
            if(_statusRoad==Entity.Null||_statusNode==Entity.Null||!EntityManager.HasComponent<Owner>(source))return;
            var owner=EntityManager.GetComponentData<Owner>(source).m_Owner;if(owner!=_statusRoad&&owner!=_statusNode)return;
            if(TryResolveApproach(source,out var road,out var node)&&road==_statusRoad&&node==_statusNode){if(ModDiagnostics.TraceBuffer.Enabled&&CrossingStatus!=text)ModDiagnostics.TraceBuffer.Record("crosswalk.result",source+" "+text);CrossingStatus=text;}
        }


        private bool _launchingNative;
        private byte _pendingNativeMode;
        internal void ApplyPendingNativeMode()
        {
            byte mode=_pendingNativeMode;_pendingNativeMode=0;
            if(mode==1)LaunchNativeCrosswalk();else if(mode==2)LaunchNativeInvisibleCrosswalk();
        }

        // Leaving the crossing tools must not carry custom paint into another tool.

        private void ToolChanged(ToolBaseSystem tool) { if(!_launchingNative && !(tool is NetToolSystem) && !(tool is LineCrosswalkLaunchTool)){_pendingNativeMode=0;NativeLineMode=false;NativeInvisibleMode=false;} }

        private PrefabSystem _prefabs; private ToolSystem _tools;

        private EntityQuery _prefabQuery, _choicesQuery;

        private Entity _choices; private bool _ready;

        private UIGroupPrefab _toolbarGroup; private Entity _nativeSource;

        internal bool HasChoices => _choices != Entity.Null && EntityManager.Exists(_choices) && EntityManager.GetBuffer<MGRCrosswalkChoice>(_choices).Length > 0;

        protected override void OnCreate()

        {

            base.OnCreate(); _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>(); _tools = World.GetOrCreateSystemManaged<ToolSystem>();

            _prefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<PlaceableNetData>());

            _choicesQuery = GetEntityQuery(ComponentType.ReadOnly<MGRCrosswalkChoices>(), ComponentType.ReadOnly<MGRCrosswalkChoice>());

            _tools.EventPrefabChanged += Selected;
            _tools.EventToolChanged += ToolChanged;

        }

        internal void DetachInput() { if (_tools != null){ _tools.EventPrefabChanged -= Selected;_tools.EventToolChanged -= ToolChanged;} NativeLineMode = false; NativeInvisibleMode = false; _pendingNativeMode=0; Enabled = false; }

        protected override void OnDestroy() { DetachInput(); base.OnDestroy(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            if ((mode & GameMode.GameOrEditor) == GameMode.None) return;

            Enabled = true;

            _pendingNativeMode=0;_statusRoad=_statusNode=Entity.Null;CrossingStatus="Choose a crossing to change its appearance.";

            SeedNodeIntents();

            if (_choicesQuery.IsEmpty) { _choices = EntityManager.CreateEntity(typeof(MGRCrosswalkChoices), typeof(MGRCrosswalkChoice)); }

            else _choices = _choicesQuery.GetSingletonEntity();

        }

        protected override void OnUpdate()

        {

            if (_ready) { if (EnsureToolbar()) Enabled = false; return; }

            using (var entities = _prefabQuery.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)

                {

                    if (!_prefabs.TryGetPrefab<NetPrefab>(entity, out var source) || source == null || source.name != "Crosswalk") continue;

                    NativeCrosswalkPrefab = source;

                    InvisiblePrefab = source.Clone(InvisibleName);

                    LinePrefab = source.Clone(LineName); MarkedParkingPrefab = source.Clone(MarkedName); UnmarkedParkingPrefab = source.Clone(UnmarkedName); TOnlyPrefab = source.Clone(TOnlyName); NotchedPrefab = source.Clone(NotchedName);

                    var original = source.GetComponent<UIObject>();

                    if (original != null)

                    {

                        _toolbarGroup = original.m_Group; _nativeSource = entity;

                        int offset = 1; foreach (var asset in Assets()) { var ui = asset.GetComponent<UIObject>(); ui.m_Group = null; ui.m_Priority = original.m_Priority + offset++; }

                        InvisiblePrefab.GetComponent<UIObject>().m_Icon = "coui://mgr-icons/InvisibleCrosswalk.svg";

                        LinePrefab.GetComponent<UIObject>().m_Icon = "coui://mgr-icons/LineCrosswalk.svg";

                        MarkedParkingPrefab.GetComponent<UIObject>().m_Icon = "coui://mgr-icons/ParkingMarked.svg";

                        UnmarkedParkingPrefab.GetComponent<UIObject>().m_Icon = "coui://mgr-icons/ParkingUnmarked.svg";

                        TOnlyPrefab.GetComponent<UIObject>().m_Icon = "coui://mgr-icons/ParkingTs.svg";

                        NotchedPrefab.GetComponent<UIObject>().m_Icon = "coui://mgr-icons/ParkingNotched.svg";

                    }

                    foreach (var asset in Assets()) _prefabs.AddPrefab(asset); _ready = true;

                    Mod.Debug("Registered Line Crosswalk / Marked Parking / Unmarked Parking upgrades for the Curbside panel."); break;

                }

        }

        private bool EnsureToolbar()

        {

            if (_toolbarGroup == null || !_prefabs.TryGetEntity(_toolbarGroup, out var group) || !EntityManager.HasBuffer<UIGroupElement>(group)) return false;

            bool sourceLocked = EntityManager.HasComponent<Locked>(_nativeSource) && EntityManager.IsComponentEnabled<Locked>(_nativeSource);

            foreach (var prefab in Assets())

            {

                if (!_prefabs.TryGetEntity(prefab, out var entity) || !EntityManager.HasComponent<UIObjectData>(entity)) return false;


                var elements=EntityManager.GetBuffer<UIGroupElement>(group);for(int index=elements.Length-1;index>=0;index--)if(elements[index].m_Prefab==entity)elements.RemoveAt(index);

                var data = EntityManager.GetComponentData<UIObjectData>(entity); data.m_Group = Entity.Null; data.m_Priority = prefab.GetComponent<UIObject>().m_Priority; EntityManager.SetComponentData(entity, data);

                if (EntityManager.HasComponent<Locked>(entity)) EntityManager.SetComponentEnabled<Locked>(entity, sourceLocked);

                if (!sourceLocked && !EntityManager.HasComponent<Unlock>(entity)) EntityManager.AddComponent<Unlock>(entity);

            }

            Mod.Debug("Crosswalk toolbar entries verified in " + _toolbarGroup.name + "; native lock=" + sourceLocked + "; toolbar refresh signaled."); return true;

        }

        internal void Activate(bool line)

        {

            var prefab = LinePrefab;

            if (!line) return;

            if (prefab == null) { Mod.Log.Warn("Crosswalk upgrade assets are not ready."); return; }

            _tools.ActivatePrefabTool(prefab);

        }

        internal void ActivateParking(bool marked) { var prefab = marked ? MarkedParkingPrefab : UnmarkedParkingPrefab; if (prefab != null) _tools.ActivatePrefabTool(prefab); }

        internal void ActivateParkingStyle(bool notched) { var prefab = notched ? NotchedPrefab : TOnlyPrefab; if (prefab != null) _tools.ActivatePrefabTool(prefab); }

        private void Selected(PrefabBase prefab)

        {

            if (prefab == null) return;

            if(!_launchingNative)
            {
                if(prefab.name==LineName||prefab.name==InvisibleName)
                {
                    // NetToolSystem accepts any NetPrefab, including these launch assets.
                    // Capture intent independently of tool-list ordering; hand off only after
                    // ActivatePrefabTool has finished its selection loop.
                    _pendingNativeMode=prefab.name==LineName?(byte)1:(byte)2;
                    CrossingStatus="Crosswalk appearance selected; preparing native crossing tool.";
                    return;
                }
                _pendingNativeMode=0;
                NativeLineMode=false;NativeInvisibleMode=false;
            }

            if (IsParkingName(prefab.name)) { var parking = World.GetOrCreateSystemManaged<ParkingUpgradeTool>(); parking.Select(prefab); _tools.activeTool = parking; return; }

            if (prefab != LinePrefab) return;

            Mod.Debug("Line crosswalk launcher selected; native handoff deferred until tool update.");

        }

        internal bool LaunchNativeCrosswalk() => LaunchCrosswalkMode(false);
        internal bool LaunchNativeZebraCrosswalk() => LaunchCrosswalkMode(false,false);

        internal bool LaunchNativeInvisibleCrosswalk() => LaunchCrosswalkMode(true);

        private bool LaunchCrosswalkMode(bool invisible,bool line=true)

        {

            if (NativeCrosswalkPrefab == null) return false;

            _launchingNative = true;

            try

            {

                NativeLineMode = line && !invisible; NativeInvisibleMode = invisible; CrossingStatus=invisible?"Invisible crossing tool ready.":line?"Lined crossing tool ready.":"Native zebra tool ready.";

                var native=World.GetOrCreateSystemManaged<NetToolSystem>();
                var activated = native.TrySetPrefab(NativeCrosswalkPrefab);
                if(activated)_tools.activeTool=native;

                if (!activated) { NativeLineMode = false; NativeInvisibleMode = false; }

                Mod.Debug("Original native Crosswalk handoff completed; success=" + activated);

                return activated;

            }

            finally { _launchingNative = false; }

        }

        internal static bool Matches(MGRCrosswalkChoice choice, Bezier4x3 curve)

        {

            var center = MathUtils.Position(curve, .5f); var axis = math.normalizesafe(MathUtils.Tangent(curve, .5f));

            return math.distance(choice.Center, center) < 2f && math.abs(math.dot(choice.Axis, axis)) > .95f;

        }

        internal bool IsSelected(Bezier4x3 curve) { if (!HasChoices) return false; foreach (var c in EntityManager.GetBuffer<MGRCrosswalkChoice>(_choices, true)) if (Matches(c, curve)) return true; return false; }

        internal void SetChoice(Bezier4x3 curve, bool line, Entity source = default(Entity))

        {

            if (_choices == Entity.Null || !EntityManager.Exists(_choices)) _choices = _choicesQuery.IsEmpty ? EntityManager.CreateEntity(typeof(MGRCrosswalkChoices), typeof(MGRCrosswalkChoice)) : _choicesQuery.GetSingletonEntity();

            var choices = EntityManager.GetBuffer<MGRCrosswalkChoice>(_choices);

            for (int i = choices.Length - 1; i >= 0; i--) if (Matches(choices[i], curve)) choices.RemoveAt(i);

            if (line) choices.Add(new MGRCrosswalkChoice { Center = MathUtils.Position(curve, .5f), Axis = math.normalizesafe(MathUtils.Tangent(curve, .5f)) });

            if (source != Entity.Null) IntersectionPaintSystem.RequestSource(source); else IntersectionPaintSystem.RequestRefresh(); Mod.Debug(line ? "Selected crossing upgraded to two lines." : "Selected crossing restored to native zebra paint.");

        }

    }

}


