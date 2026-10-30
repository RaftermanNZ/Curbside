using Colossal.Mathematics;using Game.Common;using Game.Net;using Game.Prefabs;using Game.Tools;
using Unity.Collections;using Unity.Entities;using Unity.Jobs;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRNodeSplitDefinition:IComponentData{}
    internal static class NodeSplitPolicy
    {
        internal static bool Available=>false; // Persistent house-access regression: keep preview/apply gated.
        internal static bool IsSplitNode(Entity original,Entity road,TempFlags flags)=>original==road&&(flags&TempFlags.Replace)!=0&&(flags&(TempFlags.Delete|TempFlags.Cancel))==0;
        internal static bool TryPosition(Bezier4x3 curve,float3 hit,float width,out float t,out float3 point)
        {
            t=0;point=default;
            if(!math.all(math.isfinite(hit))||!math.isfinite(width)||width<=0)return false;
            MathUtils.Distance(curve,hit,out t);if(!math.isfinite(t)||t<=0||t>=1)return false;
            float clearance=.5f; // Only prevent coincident/end-point nodes; native validation decides the rest.
            if(MathUtils.Length(MathUtils.Cut(curve,new float2(0,t)))<clearance||MathUtils.Length(MathUtils.Cut(curve,new float2(t,1)))<clearance)return false;
            point=MathUtils.Position(curve,t);return math.all(math.isfinite(point));
        }
    }
    // Native creation definitions produce a disposable preview. Only the game's
    // ApplyNetSystem commits nodes/edges; this tool never rewrites their topology.
    public partial class NodeSplitTool:ToolBaseSystem
    {
        internal const string AssetName="MGR Split Road Section";
        private PrefabBase selected;private ToolOutputBarrier output;private EntityQuery definitions,tempNodes,tempEdges;
        private string previewReason;
        private Entity target;private float3 location;private float readyAfter,clickDeadline;private bool clickPending;
        public override string toolID=>"MGRSplitRoad";
        public override PrefabBase GetPrefab()=>selected;
        public override bool TrySetPrefab(PrefabBase prefab){if(prefab==null||prefab.name!=AssetName)return false;selected=prefab;m_ToolSystem.EventPrefabChanged?.Invoke(prefab);return true;}
        protected override void OnCreate()
        {
            base.OnCreate();output=World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            definitions=GetEntityQuery(ComponentType.ReadOnly<MGRNodeSplitDefinition>());
            tempNodes=GetEntityQuery(ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<Temp>(),ComponentType.Exclude<Deleted>());
            tempEdges=GetEntityQuery(ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Curve>(),ComponentType.ReadOnly<Temp>(),ComponentType.Exclude<Deleted>());
        }
        protected override void OnStartRunning(){base.OnStartRunning();target=Entity.Null;clickPending=false;applyAction.shouldBeEnabled=true;secondaryApplyAction.shouldBeEnabled=true;cancelAction.shouldBeEnabled=true;}
        private void ClearDefinitions()
        {
            var commands=output.CreateCommandBuffer();using(var items=definitions.ToEntityArray(Allocator.Temp))foreach(var item in items)commands.DestroyEntity(item);
        }
        private void CancelPreview(){if(target!=Entity.Null){ClearDefinitions();applyMode=ApplyMode.Clear;target=Entity.Null;}clickPending=false;}
        protected override void OnStopRunning()
        {
            // ToolSystem may stop us outside the ToolOutputBarrier recording phase.
            // Retire only our owned definitions synchronously, never native temp roads.
            try{Dependency.Complete();if(!definitions.IsEmptyIgnoreFilter)EntityManager.DestroyEntity(definitions);}
            finally{applyMode=ApplyMode.Clear;target=Entity.Null;clickPending=false;base.OnStopRunning();}
        }
        public override void InitializeRaycast(){base.InitializeRaycast();m_ToolRaycastSystem.typeMask=TypeMask.Net;m_ToolRaycastSystem.netLayerMask=Layer.Road;m_ToolRaycastSystem.collisionMask=CollisionMask.OnGround|CollisionMask.Overground;}
        private void Define(Entity road,float t,float3 point)
        {
            ClearDefinitions();var commands=output.CreateCommandBuffer();var definition=commands.CreateEntity();
            commands.AddComponent<MGRNodeSplitDefinition>(definition);commands.AddComponent<Updated>(definition);
            commands.AddComponent(definition,new CreationDefinition{m_Prefab=EntityManager.GetComponentData<PrefabRef>(road).m_Prefab,m_Flags=CreationFlags.SubElevation,m_RandomSeed=road.Index^UnityEngine.Time.frameCount});
            var rotation=NetUtils.GetNodeRotation(float3.zero);
            var pos=new CoursePos{m_Entity=road,m_Position=point,m_Rotation=rotation,m_SplitPosition=t,m_ParentMesh=-1,m_Flags=CoursePosFlags.IsFirst|CoursePosFlags.IsLast|CoursePosFlags.IsLeft|CoursePosFlags.IsRight};
            if(EntityManager.HasComponent<Game.Net.Elevation>(road))pos.m_Elevation=EntityManager.GetComponentData<Game.Net.Elevation>(road).m_Elevation;
            var end=pos;end.m_CourseDelta=1;
            commands.AddComponent(definition,new NetCourse{m_Curve=new Bezier4x3(point,point,point,point),m_StartPosition=pos,m_EndPosition=end,m_FixedIndex=-1});
            target=road;location=point;applyMode=ApplyMode.Clear;readyAfter=UnityEngine.Time.realtimeSinceStartup+.1f;
        }
        private bool ValidSplitPreview()
        {
            previewReason="Native preview reports a placement error.";if(!GetAllowApply())return false;previewReason="Waiting for the native edge-replacement split node.";Entity middle=Entity.Null;
            using(var nodes=tempNodes.ToEntityArray(Allocator.Temp))foreach(var node in nodes)
            {
                var temp=EntityManager.GetComponentData<Temp>(node);
                if(NodeSplitPolicy.IsSplitNode(temp.m_Original,target,temp.m_Flags)&&math.distancesq(EntityManager.GetComponentData<Node>(node).m_Position,location)<.04f){if(middle!=Entity.Null)return false;middle=node;}
            }
            if(middle==Entity.Null)return false;previewReason="Waiting for two native replacement sections.";int count=0;
            using(var edges=tempEdges.ToEntityArray(Allocator.Temp))foreach(var road in edges)
            {
                var temp=EntityManager.GetComponentData<Temp>(road);if((temp.m_Flags&(TempFlags.Delete|TempFlags.Cancel))!=0)continue;
                var edge=EntityManager.GetComponentData<Edge>(road);if(edge.m_Start!=middle&&edge.m_End!=middle)continue;
                if(temp.m_Original!=Entity.Null&&temp.m_Original!=target)return false;
                if(EntityManager.GetComponentData<Curve>(road).m_Length<.5f)return false;count++;
            }return count==2;
        }
        protected override JobHandle OnUpdate(JobHandle deps)
        {
            applyMode=ApplyMode.None;if(m_ToolSystem.activeTool!=this||Mod.Saving)return deps;
            if(!NodeSplitPolicy.Available){if(applyAction.WasPressedThisFrame())UpgradeFeedback.Report(AssetName,"Splitting is temporarily unavailable: building-access transfer is under repair. No road changed.");
            m_ToolSystem.activeTool=m_DefaultToolSystem;return deps;}

            bool cancel=UpgradeInputPolicy.RemovePressed(secondaryApplyAction.WasPressedThisFrame())||UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame());
            if(cancel){m_ToolSystem.activeTool=m_DefaultToolSystem;return deps;}
            if(!m_HasFocus||UpgradeInputPolicy.PointerBlocked){clickPending=false;return deps;}
            deps.Complete();
            if(!m_ToolRaycastSystem.GetRaycastResult(out var hit)){CancelPreview();return default;}
            var owner=hit.m_Owner;
            if(EntityManager.HasComponent<Temp>(owner))
            {
                var original=EntityManager.GetComponentData<Temp>(owner).m_Original;
                if(original!=Entity.Null)owner=original;
                else if(target!=Entity.Null&&math.distancesq(hit.m_Hit.m_HitPosition,location)<4)owner=target;
            }
            if(!SectionUpgradeTargeting.TryRoad(EntityManager,owner,hit.m_Hit.m_HitPosition,out var road)||!EntityManager.HasComponent<PrefabRef>(road)||!EntityManager.HasComponent<Composition>(road)||EntityManager.HasComponent<Owner>(road)){CancelPreview();return default;}
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasComponent<NetCompositionData>(composition)||!NodeSplitPolicy.TryPosition(EntityManager.GetComponentData<Curve>(road).m_Bezier,hit.m_Hit.m_HitPosition,EntityManager.GetComponentData<NetCompositionData>(composition).m_Width,out float t,out var point))
            {CancelPreview();if(applyAction.WasPressedThisFrame())UpgradeFeedback.Report(AssetName,"Choose a point at least 0.5 m inside the existing segment. Native geometry restrictions still apply.");return default;}
            bool pressed=applyAction.WasPressedThisFrame();if(pressed){clickPending=true;clickDeadline=UnityEngine.Time.realtimeSinceStartup+2;}
            if(target!=road||(pressed||!clickPending)&&math.distancesq(point,location)>.01f){Define(road,t,point);return default;}
            var buffer=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var pending);pending.Complete();buffer.DrawCircle(new UnityEngine.Color(.3f,.8f,1,.9f),location,1f);
            if(!clickPending||UnityEngine.Time.realtimeSinceStartup<readyAfter)return default;
            if(!ValidSplitPreview()){if(UnityEngine.Time.realtimeSinceStartup<clickDeadline)return default;clickPending=false;UpgradeFeedback.Report(AssetName,previewReason+" No split was applied.");return default;}
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("split.apply","Native preview validated for "+target);
            applyMode=ApplyMode.Apply;ClearDefinitions();target=Entity.Null;clickPending=false;
            UpgradeFeedback.Report(AssetName,"Road split submitted to the game. Existing section upgrades follow its replacement roads.");return default;
        }
    }
}
