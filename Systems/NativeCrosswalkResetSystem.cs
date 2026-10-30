using System;



using System.Reflection;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Input;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Entities;

using Unity.Mathematics;

using UnityEngine.InputSystem;

namespace MedianGrassReplacement.Systems

{

    // Native NetToolSystem owns all creation, validation, removal and pedestrian routes.

    // This bridge only records the desired paint after native geometry is available.

    public partial class NativeCrosswalkResetSystem : GameSystemBase

    {

        // Temporarily suppress only the native downgrade action while this

        // paint-only tool owns right click. The original proxy still receives

        // its bindings; the bridge reads it and restores paint after native UI.

        private sealed class RestoreAction : IProxyAction

        {

            internal readonly IProxyAction Inner;internal RestoreAction(IProxyAction inner){Inner=inner;}

            public bool shouldBeEnabled {get=>Inner.shouldBeEnabled;set=>Inner.shouldBeEnabled=value;}

            public bool enabled=>Inner.enabled;

            public event Action<ProxyAction,InputActionPhase> onInteraction {add{Inner.onInteraction+=value;}remove{Inner.onInteraction-=value;}}

            public bool WasPressedThisFrame()=>false;public bool WasReleasedThisFrame()=>false;public bool IsPressed()=>false;public bool IsInProgress()=>false;public float GetMagnitude()=>0;public T ReadValue<T>() where T:struct=>default;

        }

        private FieldInfo _downgradeField;private NetToolSystem _downgradeTool;private RestoreAction _restoreAction;

        internal void PrepareInvisibleRestore()
        {
            if(!_bridgeAvailable){ReleaseRestoreAction();return;}
            try{PrepareInvisibleRestoreCore();}
            catch(System.Exception error){_bridgeAvailable=false;ReleaseRestoreAction();Mod.Log.Warn("Crosswalk input bridge disabled after capability failure: "+error.Message);}
        }
        private void PrepareInvisibleRestoreCore()

        {

            var choices=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();

            bool active=!Mod.Saving && _tools.activeTool is NetToolSystem && choices.NativeInvisibleMode && _tools.activeTool.GetPrefab()?.name=="Crosswalk";

            if(!active) {ReleaseRestoreAction();return;}

            var tool=(NetToolSystem)_tools.activeTool;if(_downgradeTool==tool) return;

            ReleaseRestoreAction();_downgradeField=typeof(NetToolSystem).GetField("m_DowngradeNetEdge",BindingFlags.Instance|BindingFlags.NonPublic);

            var original=_downgradeField?.GetValue(tool) as IProxyAction;if(original==null) return;

            _restoreAction=new RestoreAction(original);_downgradeTool=tool;_downgradeField.SetValue(tool,_restoreAction);

        }

        private void ReleaseRestoreAction()

        {

            try{if(_downgradeTool!=null && _downgradeField!=null && ReferenceEquals(_downgradeField.GetValue(_downgradeTool),_restoreAction)) _downgradeField.SetValue(_downgradeTool,_restoreAction.Inner);}
            catch(System.Exception error){_bridgeAvailable=false;Mod.Log.Warn("Crosswalk action release failed; bridge disabled: "+error.Message);}

            _downgradeTool=null;_restoreAction=null;

        }

        private ToolSystem _tools; private ToolRaycastSystem _raycast; private PrefabSystem _prefabs;

        private bool _bridgeAvailable;
        private PropertyInfo _apply, _secondary;private FieldInfo _nativeRemoval;
        private bool NativeRemovalPressed()=>_tools.activeTool is NetToolSystem&&(_nativeRemoval?.GetValue(_tools.activeTool) as IProxyAction)?.WasPressedThisFrame()==true; private IProxyAction _styleApply;
        private bool _capturedAdd,_capturedRemove,_capturedStyle,_handledExisting;
        internal void CaptureCrosswalkInput()
        {
            _capturedAdd=_capturedRemove=_capturedStyle=_handledExisting=false;
            if(!_bridgeAvailable)return;
            if(!Mod.Saving)World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().ApplyPendingNativeMode();
            PrepareInvisibleRestore();
            if(Mod.Saving||!(_tools.activeTool is NetToolSystem)||_tools.activeTool.GetPrefab()?.name!="Crosswalk"||InputManager.instance.mouseOverUI||InputManager.instance.hasInputFieldFocus)return;
            // Native tool updates can disable/replace action proxies after a click.
            _capturedAdd=Pressed(_apply)||(Mouse.current?.leftButton.wasPressedThisFrame??false);_capturedStyle=_styleApply?.WasPressedThisFrame()??false;
            _capturedRemove=Pressed(_secondary)||NativeRemovalPressed()||(_restoreAction?.Inner.WasPressedThisFrame()??false)||(Mouse.current?.rightButton.wasPressedThisFrame??false);
            if(!_capturedAdd&&!_capturedStyle&&!_capturedRemove)return;
            if(!_raycast.GetRaycastResult(out var hit)||!Target(hit.m_Owner,hit.m_Hit.m_HitPosition,out var target))
            {World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().NoteRequest(Entity.Null,Entity.Null,"Click received, but no committed road approach was resolved.");return;}
            Dependency.Complete();
            var source=Nearest(target.Node,hit.m_Hit.m_HitPosition,target.Axis,8);
            if(source==Entity.Null)source=Nearest(target.Road,hit.m_Hit.m_HitPosition,target.Axis,8);
            var choices=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            if(source==Entity.Null)
            {
                // Persist paint preference on the original approach before the native
                // tool replaces its preview/raycast. Creation still belongs to NetToolSystem.
                if(!_capturedRemove&&(_capturedAdd||_capturedStyle)&&(choices.NativeLineMode||choices.NativeInvisibleMode))
                    ApplyApproach(target,Entity.Null,choices.NativeLineMode,choices.NativeInvisibleMode);
                return;
            }
            if(_capturedRemove&&choices.NativeInvisibleMode)
            {if(choices.TryResolveApproach(source,out var road,out var node))choices.RestoreHiddenAppearance(road,node);}
            else ApplyApproach(target,source,!_capturedRemove&&choices.NativeLineMode,!_capturedRemove&&choices.NativeInvisibleMode);
            _handledExisting=true;
        }

        private sealed class ApproachTarget { internal Entity Road, Node; internal float3 Point; internal float2 Axis; }



        protected override void OnCreate()

        {

            base.OnCreate(); _tools = World.GetOrCreateSystemManaged<ToolSystem>(); _raycast = World.GetOrCreateSystemManaged<ToolRaycastSystem>(); _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();

            _apply = typeof(ToolBaseSystem).GetProperty("applyAction", BindingFlags.Instance | BindingFlags.NonPublic);

            _nativeRemoval=typeof(NetToolSystem).GetField("m_DowngradeNetEdge",BindingFlags.Instance|BindingFlags.NonPublic);
            _secondary = typeof(ToolBaseSystem).GetProperty("secondaryApplyAction", BindingFlags.Instance | BindingFlags.NonPublic);

            var property = typeof(InputManager).GetProperty("toolActionCollection", BindingFlags.Instance | BindingFlags.NonPublic);

            _bridgeAvailable=_apply!=null&&_secondary!=null&&_nativeRemoval!=null&&property!=null;
            if(!_bridgeAvailable){Mod.Log.Warn("Existing-crosswalk input bridge unavailable on this game build; native placement remains enabled.");return;}
            var collection = property?.GetValue(InputManager.instance) as UIInputActionCollection;

            _styleApply = collection?.GetActionState("Upgrade Net Edge", "MGR existing crossing paint");

            if (_styleApply == null) Mod.Log.Warn("Native crossing bridge: existing-paint action unavailable; native placement remains enabled.");

        }

        internal void ReleaseInput()

        {

            ReleaseRestoreAction();Enabled = false;

            var action = _styleApply; _styleApply = null;

            if (action == null) return;

            action.shouldBeEnabled = false;

            action.GetType().GetMethod("Dispose", Type.EmptyTypes)?.Invoke(action, null);

        }

        protected override void OnDestroy() { ReleaseInput(); base.OnDestroy(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode) { base.OnGameLoadingComplete(purpose, mode); if (_styleApply != null) _styleApply.shouldBeEnabled = false; }

        private bool Live(Entity entity) => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity) && !EntityManager.HasComponent<Temp>(entity);

        private bool Pressed(PropertyInfo property) => (property?.GetValue(_tools.activeTool) as IProxyAction)?.WasPressedThisFrame() ?? false;

        private bool Crossing(Entity lane)

        {

            if (!Live(lane) || !EntityManager.HasComponent<Game.Net.LaneGeometry>(lane) || !EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<PrefabRef>(lane)) return false;

            // Native upgrade previews can hide the committed owner and its lanes while

            // the pointer is over them. Those originals are still valid paint targets.

            if (EntityManager.HasComponent<Hidden>(lane) && !EntityManager.HasComponent<MGRReplacedPaint>(lane))

            {

                if (!EntityManager.HasComponent<Owner>(lane) || !EntityManager.HasComponent<Hidden>(EntityManager.GetComponentData<Owner>(lane).m_Owner)) return false;

            }

            return _prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab, out var prefab) && prefab != null && CrosswalkPaintPolicy.IsPaint(true,prefab.name);

        }

        private Entity Nearest(Entity node, float3 point, float2 axis, float limit)

        {

            if (!Live(node) || !EntityManager.HasBuffer<Game.Net.SubLane>(node)) return Entity.Null;

            Entity best = Entity.Null;

            foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node, true))

            {

                var lane = entry.m_SubLane; if (!Crossing(lane)) continue; var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;

                if (math.lengthsq(axis) > .5f && math.abs(math.dot(axis, math.normalizesafe(MathUtils.Tangent(curve, .5f).xz))) < .7f) continue;

                var probe = point; probe.y = MathUtils.Position(curve, .5f).y; if (math.abs(point.y - probe.y) > 3f) continue;

                float distance = MathUtils.Distance(curve, probe, out float t); if (distance < limit) { limit = distance; best = lane; }

            }

            return best;

        }

        private bool Target(Entity owner, float3 hit, out ApproachTarget target)

        {

            target = null;

            if (EntityManager.HasComponent<Temp>(owner)) { var original = EntityManager.GetComponentData<Temp>(owner).m_Original; if (original != Entity.Null) owner = original; }

            for (int i = 0; i < 4 && !EntityManager.HasComponent<Edge>(owner) && !EntityManager.HasComponent<Node>(owner) && EntityManager.HasComponent<Owner>(owner); i++) owner = EntityManager.GetComponentData<Owner>(owner).m_Owner;

            if (EntityManager.HasComponent<Node>(owner) && EntityManager.HasBuffer<ConnectedEdge>(owner))

            {

                float best = float.MaxValue; Entity road = Entity.Null;

                foreach (var e in EntityManager.GetBuffer<ConnectedEdge>(owner, true)) if (Live(e.m_Edge) && EntityManager.HasComponent<Road>(e.m_Edge) && EntityManager.HasComponent<Curve>(e.m_Edge))

                    {

                        float d = MathUtils.Distance(EntityManager.GetComponentData<Curve>(e.m_Edge).m_Bezier, hit, out float t); if (d < best) { best = d; road = e.m_Edge; }

                    }

                owner = road;

            }

            if (!Live(owner) || !EntityManager.HasComponent<Road>(owner) || !EntityManager.HasComponent<Edge>(owner) || !EntityManager.HasComponent<EdgeGeometry>(owner)) return false;

            var edge = EntityManager.GetComponentData<Edge>(owner); var g = EntityManager.GetComponentData<EdgeGeometry>(owner);

            var a = (g.m_Start.m_Left.a + g.m_Start.m_Right.a) * .5f; var d2 = (g.m_End.m_Left.d + g.m_End.m_Right.d) * .5f;

            bool start = !NativeCrosswalkTargeting.NearEnd(g, hit); var segment = start ? g.m_Start : g.m_End;

            var l = start ? segment.m_Left.a : segment.m_Left.d; var r = start ? segment.m_Right.a : segment.m_Right.d;

            target = new ApproachTarget { Road = owner, Node = start ? edge.m_Start : edge.m_End, Point = (l + r) * .5f, Axis = math.normalizesafe((r - l).xz) }; return true;

        }

        internal bool TryPreviewTarget(out Entity road,out Entity node,out Entity source,out float3 point,out float2 axis)
        {
            road=node=source=Entity.Null;point=default;axis=default;
            if(!_raycast.GetRaycastResult(out var hit)||!Target(hit.m_Owner,hit.m_Hit.m_HitPosition,out var target))return false;
            road=target.Road;node=target.Node;point=target.Point;axis=target.Axis;
            source=Nearest(node,hit.m_Hit.m_HitPosition,axis,8f);
            var other=Nearest(road,hit.m_Hit.m_HitPosition,axis,8f);
            if(other!=Entity.Null&&(source==Entity.Null||MathUtils.Distance(EntityManager.GetComponentData<Curve>(other).m_Bezier,point,out _) < MathUtils.Distance(EntityManager.GetComponentData<Curve>(source).m_Bezier,point,out _)))source=other;
            return true;
        }

        private void ApplyApproach(ApproachTarget target, Entity selected, bool line, bool invisible)

        {

            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();

            if (selected != Entity.Null && choices.TryResolveApproach(selected, out var road, out var node)) { target.Road = road; target.Node = node; }

            choices.SetApproachAppearance(target.Road, target.Node, line, invisible);
            choices.NoteRequest(target.Road,target.Node,invisible?"Invisible appearance recorded; waiting for crossing paint.":line?"Line appearance recorded; waiting for crossing paint.":"Native appearance recorded.");

        }

        private void StyleEnabled(bool enabled) { if (_styleApply != null && _styleApply.shouldBeEnabled != enabled) _styleApply.shouldBeEnabled = enabled; }

        protected override void OnUpdate()

        {

            if(Mod.Saving) { StyleEnabled(false); return; }

            bool styleClick = _capturedStyle || (_styleApply?.WasPressedThisFrame() ?? false);

            if (!(_tools.activeTool is NetToolSystem) || InputManager.instance.mouseOverUI || InputManager.instance.hasInputFieldFocus) { StyleEnabled(false); return; }

            string name = _tools.activeTool.GetPrefab()?.name; bool line = name == "Crosswalk" && World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().NativeLineMode;

            bool invisible = name == "Crosswalk" && World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().NativeInvisibleMode;

            if (!line && !invisible && name != "Crosswalk") { StyleEnabled(false); return; }

            if (!_raycast.GetRaycastResult(out var hit)) { StyleEnabled(false); return; }

            Dependency.Complete();

            if (!Target(hit.m_Owner, hit.m_Hit.m_HitPosition, out var target)) { StyleEnabled(false); return; }

            var source = Nearest(target.Node, hit.m_Hit.m_HitPosition, target.Axis, 8f);

            var roadSource = Nearest(target.Road, hit.m_Hit.m_HitPosition, target.Axis, 8f);

            if (roadSource != Entity.Null && (source == Entity.Null || MathUtils.Distance(EntityManager.GetComponentData<Curve>(roadSource).m_Bezier, hit.m_Hit.m_HitPosition, out float rt) < MathUtils.Distance(EntityManager.GetComponentData<Curve>(source).m_Bezier, hit.m_Hit.m_HitPosition, out float nt))) source = roadSource;

            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();

            // Native placement can be a no-op on an existing crossing. Enable only a paint

            // action there; this does not create geometry or override native placement checks.

            StyleEnabled(line || invisible || source != Entity.Null && (choices.IsSourceSelected(source) || choices.IsSourceInvisible(source)));

            if(_handledExisting)return;
            bool add = _capturedAdd || Pressed(_apply), remove = _capturedRemove || Pressed(_secondary) || NativeRemovalPressed() || (invisible && (_restoreAction?.Inner.WasPressedThisFrame()??false));

            if (remove) { if(invisible) {if(source!=Unity.Entities.Entity.Null && choices.TryResolveApproach(source,out var restoreRoad,out var restoreNode)) choices.RestoreHiddenAppearance(restoreRoad,restoreNode);else choices.RestoreHiddenAppearance(target.Road,target.Node);}else ApplyApproach(target, source, false, false); return; }

            if (!add && !styleClick) return;

            ApplyApproach(target, source, line, invisible);



        }

    }

}

