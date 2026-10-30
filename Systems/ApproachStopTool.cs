using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public partial class ApproachStopTool:ToolBaseSystem
    {
        internal const string AssetName="MGR Two Way Stop", SingleAssetName="MGR Single Approach Stop", YieldAssetName="MGR Two Way Yield", SingleYieldAssetName="MGR Single Approach Yield";
        private bool Single=>_selected?.name==SingleAssetName||_selected?.name==SingleYieldAssetName;
        private bool Yield=>_selected?.name==YieldAssetName||_selected?.name==SingleYieldAssetName;
        private PrefabBase _selected;
        protected override void OnCreate(){base.OnCreate();m_ToolSystem.tools.Remove(this);m_ToolSystem.tools.Insert(0,this);}
        public override string toolID=>"MGRApproachStop";
        public override PrefabBase GetPrefab()=>_selected;
        public override bool TrySetPrefab(PrefabBase prefab){if(prefab==null||prefab.name!=AssetName&&prefab.name!=SingleAssetName&&prefab.name!=YieldAssetName&&prefab.name!=SingleYieldAssetName)return false;_selected=prefab;m_ToolSystem.EventPrefabChanged?.Invoke(prefab);return true;}
        protected override void OnStartRunning(){base.OnStartRunning();applyAction.shouldBeEnabled=true;secondaryApplyAction.shouldBeEnabled=true;cancelAction.shouldBeEnabled=true;}
        public override void InitializeRaycast(){base.InitializeRaycast();m_ToolRaycastSystem.typeMask=TypeMask.Net;m_ToolRaycastSystem.netLayerMask=Layer.Road;m_ToolRaycastSystem.collisionMask=CollisionMask.OnGround|CollisionMask.Overground;}
        private void RefreshEndpoint(Entity node)
        {
            if(!EntityManager.Exists(node)||EntityManager.HasComponent<Deleted>(node)||EntityManager.HasComponent<Temp>(node))return;
            if(EntityManager.HasBuffer<Game.Net.SubLane>(node))
            {
                using(var lanes=EntityManager.GetBuffer<Game.Net.SubLane>(node,true).ToNativeArray(Allocator.Temp))foreach(var entry in lanes)
                {
                    var lane=entry.m_SubLane;
                    if(!EntityManager.Exists(lane)||EntityManager.HasComponent<Deleted>(lane)||EntityManager.HasComponent<Temp>(lane))continue;
                    if(!EntityManager.HasComponent<Updated>(lane))EntityManager.AddComponent<Updated>(lane);
                }
            }
            if(!EntityManager.HasComponent<Updated>(node))EntityManager.AddComponent<Updated>(node);
            World.GetOrCreateSystemManaged<ApproachStopSystem>().Queue(node);
        }
        protected override JobHandle OnUpdate(JobHandle deps)
        {
            if(m_ToolSystem.activeTool!=this || Mod.Saving)return deps;
            bool remove=UpgradeInputPolicy.RemovePressed(secondaryApplyAction.WasPressedThisFrame());
            if(UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame())){m_ToolSystem.activeTool=m_DefaultToolSystem;return deps;}
            if(!m_HasFocus||UpgradeInputPolicy.PointerBlocked||!m_ToolRaycastSystem.GetRaycastResult(out var hit))return deps;
            deps.Complete();Entity road=hit.m_Owner;
            if(EntityManager.HasComponent<Temp>(road))road=EntityManager.GetComponentData<Temp>(road).m_Original;
            for(int i=0;i<8&&!EntityManager.HasComponent<Edge>(road)&&EntityManager.HasComponent<Owner>(road);i++)road=EntityManager.GetComponentData<Owner>(road).m_Owner;
            if(!EntityManager.HasComponent<Road>(road)||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return default;
            var edge=EntityManager.GetComponentData<Edge>(road);bool end=NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(road),hit.m_Hit.m_HitPosition);var node=end?edge.m_End:edge.m_Start;
            if(!EntityManager.HasBuffer<ConnectedEdge>(node)||EntityManager.HasComponent<Deleted>(node)||EntityManager.HasComponent<TrafficLights>(node))return default;
            if(EntityManager.GetBuffer<ConnectedEdge>(node,true).Length<3)return default;
            var selected=ApproachStopSystem.Away(EntityManager,road,node);
            Entity opposite=Entity.Null;float best=-.85f;
            foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(node,true))
            {
                if(entry.m_Edge==road||!EntityManager.HasComponent<Road>(entry.m_Edge))continue;
                var direction=ApproachStopSystem.Away(EntityManager,entry.m_Edge,node);
                float score=math.dot(math.normalizesafe(selected.xz),math.normalizesafe(direction.xz));
                if(!Single&&ApproachStopPolicy.Opposite(selected,direction)&&score<best){best=score;opposite=entry.m_Edge;}
            }
            var overlay=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>();var buffer=overlay.GetBuffer(out var pending);pending.Complete();
            using(var roads=EntityManager.GetBuffer<ConnectedEdge>(node,true).ToNativeArray(Allocator.Temp))
            {
                foreach(var entry in roads)if(EntityManager.HasComponent<Road>(entry.m_Edge)&&EntityManager.HasComponent<Curve>(entry.m_Edge)&&(entry.m_Edge==road||entry.m_Edge==opposite))
                {
                    var connection=EntityManager.GetComponentData<Edge>(entry.m_Edge);bool start=connection.m_Start==node;
                    buffer.DrawCurve(new UnityEngine.Color(.2f,1,.3f,.8f),MathUtils.Cut(EntityManager.GetComponentData<Curve>(entry.m_Edge).m_Bezier,start?new float2(0,.15f):new float2(.85f,1)),2);
                }
                if(!applyAction.WasPressedThisFrame()&&!remove)return default;
                bool allWay=EntityManager.HasComponent<Upgraded>(node)&&(EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General&CompositionFlags.General.AllWayStop)!=0;
                if(!EntityManager.HasBuffer<MGRStopApproach>(node))EntityManager.AddBuffer<MGRStopApproach>(node);
                // Finish structural changes before borrowing a dynamic buffer.
                if(allWay){var upgrade=EntityManager.GetComponentData<Upgraded>(node);upgrade.m_Flags.m_General&=~CompositionFlags.General.AllWayStop;EntityManager.SetComponentData(node,upgrade);}
                foreach(var entry in roads)
                {
                    if(!EntityManager.HasComponent<Road>(entry.m_Edge)||!EntityManager.HasComponent<Curve>(entry.m_Edge))continue;
                    var direction=ApproachStopSystem.Away(EntityManager,entry.m_Edge,node);bool target=entry.m_Edge==road||entry.m_Edge==opposite;
                    var choices=EntityManager.GetBuffer<MGRStopApproach>(node);int found=-1;
                    for(int i=0;i<choices.Length;i++)if(math.dot(math.normalizesafe(direction.xz),math.normalizesafe(choices[i].Outward.xz))>.85f){found=i;break;}
                    if(Single&&!target&&!allWay&&found<0)continue;
                    var choice=new MGRStopApproach{Outward=direction,Stop=(byte)(target?(remove?0:Yield?2:1):allWay?1:found>=0?choices[found].Stop:0)};
                    if(found<0)choices.Add(choice);else choices[found]=choice;
                    if(!EntityManager.HasComponent<Updated>(entry.m_Edge))EntityManager.AddComponent<Updated>(entry.m_Edge);
                    var endpoints=EntityManager.GetComponentData<Edge>(entry.m_Edge);
                    RefreshEndpoint(endpoints.m_Start);
                    RefreshEndpoint(endpoints.m_End);
                }
            }
            if(!EntityManager.HasComponent<Updated>(node))EntityManager.AddComponent<Updated>(node);
            World.GetOrCreateSystemManaged<ApproachStopSystem>().Queue(node);return default;
        }
    }
}
