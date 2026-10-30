using System.Collections.Generic;
using Colossal.Serialization.Entities;using Game;using Game.Common;using Game.Net;using Game.Prefabs;using Game.Serialization;using Unity.Collections;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public struct MGRStreetLighting:IComponentData,ISerializable
    {
        public byte Value;
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Value);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Value);
    }
    public struct MGRStreetLightingPrevious:IComponentData,ISerializable
    {
        public bool Enabled;
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Enabled);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Enabled);
    }
    public struct MGRHiddenStreetLight:IComponentData,IEmptySerializable{}
    public struct MGRStreetLightingOwnedFlag:IComponentData,IEmptySerializable{}
    public struct MGRNodeStreetLightingPrevious:IComponentData,ISerializable
    {
        public bool Enabled,Changed;
        public void Serialize<T>(T writer)where T:IWriter{writer.Write(Enabled);writer.Write(Changed);}
        public void Deserialize<T>(T reader)where T:IReader{reader.Read(out Enabled);reader.Read(out Changed);}
    }
    public struct MGRNodeStreetLighting:IComponentData,ISerializable
    {
        public byte Value;
        public void Serialize<T>(T writer)where T:IWriter=>writer.Write(Value);
        public void Deserialize<T>(T reader)where T:IReader=>reader.Read(out Value);
    }
    internal static class UpgradeFeedback
    {
        internal static string Asset,Text;
        internal static void Report(string asset,string text){Asset=asset;Text=text;if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("upgrade.result",asset+" "+text);}
    }
    internal static class StreetLightingPolicy
    {
        internal static CompositionFlags.General Flags(CompositionFlags.General flags,bool add)=>add?flags|CompositionFlags.General.Lighting:flags&~CompositionFlags.General.Lighting;
        internal static bool Eligible(bool streetLight,bool trafficLight)=>streetLight&&!trafficLight;
    }
    public partial class StreetLightingSystem:GameSystemBase,IPreSerialize
    {
        internal const string AssetName="MGR Street Lighting";
        private EntityQuery roads,hidden,changedNodes,changedRoadProps,selectedNodes;private bool initial=true;
        private static readonly HashSet<Entity> requested=new HashSet<Entity>();
        private static readonly HashSet<Entity> requestedNodes=new HashSet<Entity>();
        private readonly Dictionary<Entity,Entity> owned=new Dictionary<Entity,Entity>();
        private readonly Dictionary<Entity,byte> modeCache=new Dictionary<Entity,byte>();
        private readonly List<Entity> audit=new List<Entity>();
        private readonly HashSet<Entity> nodeWork=new HashSet<Entity>(),ownedNodes=new HashSet<Entity>();
        private readonly Dictionary<Entity,Game.Objects.StreetLightState> priorOff=new Dictionary<Entity,Game.Objects.StreetLightState>();
        internal static byte Mode(EntityManager em,Entity road)=>em.HasComponent<MGRStreetLighting>(road)?em.GetComponentData<MGRStreetLighting>(road).Value:(byte)0;
        internal static void QueueRoad(Entity road)=>requested.Add(road);
        protected override void OnCreate()
        {
            base.OnCreate();roads=GetEntityQuery(ComponentType.ReadOnly<MGRStreetLighting>(),ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            changedNodes=GetEntityQuery(ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<Game.Objects.SubObject>(),ComponentType.ReadOnly<ConnectedEdge>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            changedNodes.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<Game.Objects.SubObject>(),ComponentType.ReadOnly<ConnectedEdge>()});
            changedRoadProps=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<MGRStreetLighting>(),ComponentType.ReadOnly<Game.Objects.SubObject>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());changedRoadProps.SetChangedVersionFilter(ComponentType.ReadOnly<Game.Objects.SubObject>());
            hidden=GetEntityQuery(ComponentType.ReadOnly<MGRHiddenStreetLight>());
            selectedNodes=GetEntityQuery(ComponentType.ReadOnly<MGRNodeStreetLighting>(),ComponentType.ReadOnly<Node>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        internal static bool TryLightTarget(EntityManager em,Entity hit,out Entity target)
        {
            target=hit;
            for(int i=0;i<8;i++)
            {
                if(!em.Exists(target)||em.HasComponent<Deleted>(target)||em.HasComponent<Game.Tools.Temp>(target))return false;
                if(em.HasComponent<Node>(target)||em.HasComponent<Road>(target)&&em.HasComponent<Edge>(target))return true;
                if(!em.HasComponent<Owner>(target))return false;var owner=em.GetComponentData<Owner>(target).m_Owner;if(owner==target)return false;target=owner;
            }return false;
        }
        internal void ApplyNode(Entity node,bool add)
        {
            if(Mod.Saving||!EntityManager.HasComponent<Node>(node)||EntityManager.HasComponent<Deleted>(node))return;
            if(!EntityManager.HasComponent<MGRNodeStreetLighting>(node))EntityManager.AddComponent<MGRNodeStreetLighting>(node);
            bool enabled=EntityManager.HasComponent<Upgraded>(node)&&(EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General&CompositionFlags.General.Lighting)!=0;
            var previous=EntityManager.HasComponent<MGRNodeStreetLightingPrevious>(node)?EntityManager.GetComponentData<MGRNodeStreetLightingPrevious>(node):new MGRNodeStreetLightingPrevious{Enabled=enabled};
            if(add&&!enabled){previous.Changed=true;NativeFlag(node,true);}
            if(!EntityManager.HasComponent<MGRNodeStreetLightingPrevious>(node))EntityManager.AddComponent<MGRNodeStreetLightingPrevious>(node);EntityManager.SetComponentData(node,previous);
            EntityManager.SetComponentData(node,new MGRNodeStreetLighting{Value=add?(byte)1:(byte)2});requestedNodes.Add(node);
            UpgradeFeedback.Report(AssetName,add?"Junction street lighting enabled where supported; segment choices unchanged.":"Junction street lamps hidden; segment lamps unchanged.");
        }
        internal void Apply(Entity road,bool add)
        {
            if(Mod.Saving||!EntityManager.Exists(road)||EntityManager.HasComponent<Deleted>(road))return;
            if(!EntityManager.HasComponent<MGRStreetLighting>(road)){
                if(!EntityManager.HasComponent<MGRStreetLightingPrevious>(road)){bool enabled=EntityManager.HasComponent<Upgraded>(road)&&(EntityManager.GetComponentData<Upgraded>(road).m_Flags.m_General&CompositionFlags.General.Lighting)!=0;EntityManager.AddComponentData(road,new MGRStreetLightingPrevious{Enabled=enabled});}
                EntityManager.AddComponent<MGRStreetLighting>(road);
            }
            EntityManager.SetComponentData(road,new MGRStreetLighting{Value=add?(byte)1:(byte)2});
            // Removing the native section lighting flag also removes node lamps.
            // Hide section-owned lamps instead; only an add enables authored assets.
            if(add){bool before=EntityManager.HasComponent<Upgraded>(road)&&(EntityManager.GetComponentData<Upgraded>(road).m_Flags.m_General&CompositionFlags.General.Lighting)!=0;if(!before&&!EntityManager.HasComponent<MGRStreetLightingOwnedFlag>(road))EntityManager.AddComponent<MGRStreetLightingOwnedFlag>(road);NativeFlag(road,true);}QueueRoad(road);
            UpgradeFeedback.Report(AssetName,add?"Segment street lighting enabled; junction lamp choices unchanged.":"Segment street lighting removed; junction lamp choices unchanged.");
        }
        internal void ResetRoad(Entity road)
        {
            if(EntityManager.HasComponent<MGRStreetLightingPrevious>(road)){bool previous=EntityManager.GetComponentData<MGRStreetLightingPrevious>(road).Enabled;byte ownedMode=Mode(EntityManager,road);bool current=EntityManager.HasComponent<Upgraded>(road)&&(EntityManager.GetComponentData<Upgraded>(road).m_Flags.m_General&CompositionFlags.General.Lighting)!=0;EntityManager.RemoveComponent<MGRStreetLightingPrevious>(road);if(ownedMode!=0&&(current==(ownedMode==1)||current&&EntityManager.HasComponent<MGRStreetLightingOwnedFlag>(road)))NativeFlag(road,previous);}
            if(EntityManager.HasComponent<MGRStreetLightingOwnedFlag>(road))EntityManager.RemoveComponent<MGRStreetLightingOwnedFlag>(road);
            // Older saves lack a baseline; do not guess away a native lighting flag.
            if(EntityManager.HasComponent<MGRStreetLighting>(road))EntityManager.RemoveComponent<MGRStreetLighting>(road);QueueRoad(road);
        }
        private void NativeFlag(Entity road,bool add)
        {
            var upgrade=EntityManager.HasComponent<Upgraded>(road)?EntityManager.GetComponentData<Upgraded>(road):default;
            var before=upgrade.m_Flags.m_General;
            upgrade.m_Flags.m_General=StreetLightingPolicy.Flags(upgrade.m_Flags.m_General,add);
            if(before==upgrade.m_Flags.m_General)return;
            if(!EntityManager.HasComponent<Upgraded>(road))EntityManager.AddComponent<Upgraded>(road);
            EntityManager.SetComponentData(road,upgrade);
            if(!EntityManager.HasComponent<Updated>(road))EntityManager.AddComponent<Updated>(road);
            if(EntityManager.HasComponent<Edge>(road))
            {
                var edge=EntityManager.GetComponentData<Edge>(road);
                if(EntityManager.Exists(edge.m_Start)&&!EntityManager.HasComponent<Updated>(edge.m_Start))EntityManager.AddComponent<Updated>(edge.m_Start);
                if(EntityManager.Exists(edge.m_End)&&!EntityManager.HasComponent<Updated>(edge.m_End))EntityManager.AddComponent<Updated>(edge.m_End);
            }
        }
        internal void ResetNode(Entity node)
        {
            if(EntityManager.HasComponent<MGRNodeStreetLightingPrevious>(node)){var previous=EntityManager.GetComponentData<MGRNodeStreetLightingPrevious>(node);if(previous.Changed&&EntityManager.HasComponent<Upgraded>(node)&&(EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General&CompositionFlags.General.Lighting)!=0)NativeFlag(node,previous.Enabled);EntityManager.RemoveComponent<MGRNodeStreetLightingPrevious>(node);}
            if(EntityManager.HasComponent<MGRNodeStreetLighting>(node))EntityManager.RemoveComponent<MGRNodeStreetLighting>(node);requestedNodes.Add(node);
        }
        private void RenderChanged(Entity e){if(!EntityManager.HasComponent<BatchesUpdated>(e))EntityManager.AddComponent<BatchesUpdated>(e);}
        private void Release(Entity e)
        {
            if(!EntityManager.Exists(e)){owned.Remove(e);priorOff.Remove(e);return;}
            if(owned.TryGetValue(e,out var road)&&EntityManager.HasComponent<Game.Objects.StreetLight>(e)&&EntityManager.HasComponent<Road>(road))
            {var light=EntityManager.GetComponentData<Game.Objects.StreetLight>(e);Game.Simulation.StreetLightSystem.UpdateStreetLightState(ref light,EntityManager.GetComponentData<Road>(road));EntityManager.SetComponentData(e,light);}
            if(EntityManager.HasComponent<Game.Objects.StreetLight>(e)&&priorOff.TryGetValue(e,out var before)&&!EntityManager.HasComponent<Road>(road)){var light=EntityManager.GetComponentData<Game.Objects.StreetLight>(e);light.m_State=(light.m_State&~Game.Objects.StreetLightState.TurnedOff)|before;EntityManager.SetComponentData(e,light);}priorOff.Remove(e);
            bool parentHidden=EntityManager.HasComponent<Owner>(e)&&EntityManager.HasComponent<Game.Tools.Hidden>(EntityManager.GetComponentData<Owner>(e).m_Owner);
            if(EntityManager.HasComponent<Game.Tools.Hidden>(e)&&!parentHidden)EntityManager.RemoveComponent<Game.Tools.Hidden>(e);
            if(EntityManager.HasComponent<MGRHiddenStreetLight>(e))EntityManager.RemoveComponent<MGRHiddenStreetLight>(e);
            RenderChanged(e);owned.Remove(e);
        }
        private void Inspect(Entity obj,Entity road,bool off,int depth)
        {
            if(depth>4||!EntityManager.Exists(obj)||EntityManager.HasComponent<Deleted>(obj)||EntityManager.HasComponent<Game.Tools.Temp>(obj))return;
            // Runtime street-lamp identity only. Traffic signals and unrelated props are excluded.
            if(StreetLightingPolicy.Eligible(EntityManager.HasComponent<Game.Objects.StreetLight>(obj),EntityManager.HasComponent<Game.Objects.TrafficLight>(obj)))
            {
                if(!off){if(owned.ContainsKey(obj))Release(obj);}
                else if(owned.ContainsKey(obj)||!EntityManager.HasComponent<Game.Tools.Hidden>(obj))
                {
                    if(!EntityManager.HasComponent<MGRHiddenStreetLight>(obj))EntityManager.AddComponent<MGRHiddenStreetLight>(obj);
                    owned[obj]=road;if(!EntityManager.HasComponent<Game.Tools.Hidden>(obj))EntityManager.AddComponent<Game.Tools.Hidden>(obj);
                    var light=EntityManager.GetComponentData<Game.Objects.StreetLight>(obj);if(!priorOff.ContainsKey(obj))priorOff[obj]=light.m_State&Game.Objects.StreetLightState.TurnedOff;light.m_State|=Game.Objects.StreetLightState.TurnedOff;EntityManager.SetComponentData(obj,light);RenderChanged(obj);
                }
            }
            if(EntityManager.HasBuffer<Game.Objects.SubObject>(obj))
            {using(var children=EntityManager.GetBuffer<Game.Objects.SubObject>(obj,true).ToNativeArray(Allocator.Temp))foreach(var child in children)Inspect(child.m_SubObject,road,off,depth+1);}
        }
        private bool IsOwnedNode(Entity node)=>ownedNodes.Contains(node);
        private byte NodeMode(Entity node)
        {
            if(EntityManager.HasComponent<Deleted>(node))return 0;
            return EntityManager.HasComponent<MGRNodeStreetLighting>(node)?EntityManager.GetComponentData<MGRNodeStreetLighting>(node).Value:(byte)0;
        }
        private byte EffectiveMode(Entity owner){if(modeCache.TryGetValue(owner,out byte result))return result;result=EntityManager.HasComponent<Node>(owner)?NodeMode(owner):Mode(EntityManager,owner);modeCache[owner]=result;return result;}
        internal void Restore()
        {
            Dependency.Complete();using(var lights=hidden.ToEntityArray(Allocator.Temp))foreach(var e in lights)Release(e);
            owned.Clear();modeCache.Clear();priorOff.Clear();nodeWork.Clear();ownedNodes.Clear();initial=true;
        }
        public void PreSerialize(Context context)=>Restore();
        protected override void OnGamePreload(Purpose p,GameMode mode){base.OnGamePreload(p,mode);Restore();requested.Clear();requestedNodes.Clear();}
        protected override void OnGameLoadingComplete(Purpose p,GameMode mode){base.OnGameLoadingComplete(p,mode);initial=true;}
        protected override void OnUpdate()
        {long started=ModDiagnostics.RuntimeCosts.Begin();try{UpdateMeasuredCore();}finally{ModDiagnostics.RuntimeCosts.End("StreetLightingSystem",started);}}
        private void UpdateMeasuredCore()
        {
            if(Mod.Saving)return;Dependency.Complete();
            try
            {
                if(requested.Count==0&&requestedNodes.Count==0&&owned.Count==0&&roads.IsEmptyIgnoreFilter&&selectedNodes.IsEmptyIgnoreFilter)return;
                nodeWork.Clear();
                foreach(var node in requestedNodes)nodeWork.Add(node);requestedNodes.Clear();
                if(initial){using(var list=roads.ToEntityArray(Allocator.Temp))foreach(var road in list)requested.Add(road);using(var nodes=selectedNodes.ToEntityArray(Allocator.Temp))foreach(var node in nodes)nodeWork.Add(node);initial=false;}
                else {roads.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<MGRStreetLighting>(),ComponentType.ReadOnly<Edge>()});try{using(var list=roads.ToEntityArray(Allocator.Temp))foreach(var road in list)requested.Add(road);}finally{roads.ResetFilter();}}
                using(var props=changedRoadProps.ToEntityArray(Allocator.Temp))foreach(var road in props)requested.Add(road);
                using(var list=changedNodes.ToEntityArray(Allocator.Temp))foreach(var node in list)if(NodeMode(node)!=0||IsOwnedNode(node))nodeWork.Add(node);
                foreach(var road in requested)if(EntityManager.Exists(road)&&!EntityManager.HasComponent<Deleted>(road)){byte mode=Mode(EntityManager,road);if(mode!=0){if(mode==1)NativeFlag(road,true);Inspect(road,road,mode==2,0);}if(EntityManager.HasComponent<Edge>(road)){var edge=EntityManager.GetComponentData<Edge>(road);nodeWork.Add(edge.m_Start);nodeWork.Add(edge.m_End);}}
                foreach(var node in nodeWork){byte mode=NodeMode(node);if(mode!=0||IsOwnedNode(node))Inspect(node,node,mode==2,0);}
                if(ModDiagnostics.TraceBuffer.Enabled&&requested.Count>0)ModDiagnostics.TraceBuffer.Record("street.reconcile","roads="+requested.Count+" nodes="+nodeWork.Count+" hiddenLamps="+owned.Count);
                requested.Clear();
                // Only explicitly hidden lamps are checked; no default city lamp scan.
                modeCache.Clear();audit.Clear();audit.AddRange(owned.Keys);
                foreach(var e in audit)
                {
                    var road=owned[e];if(!EntityManager.Exists(e)||EntityManager.HasComponent<Deleted>(e)||!EntityManager.HasComponent<Game.Objects.StreetLight>(e)){owned.Remove(e);priorOff.Remove(e);continue;}
                    if(EffectiveMode(road)!=2){Release(e);continue;}
                    if(!EntityManager.HasComponent<Game.Tools.Hidden>(e)){EntityManager.AddComponent<Game.Tools.Hidden>(e);RenderChanged(e);}
                    var light=EntityManager.GetComponentData<Game.Objects.StreetLight>(e);if((light.m_State&Game.Objects.StreetLightState.TurnedOff)==0){light.m_State|=Game.Objects.StreetLightState.TurnedOff;EntityManager.SetComponentData(e,light);}
                }
                ownedNodes.Clear();foreach(var owner in owned.Values)if(EntityManager.HasComponent<Node>(owner))ownedNodes.Add(owner);
            }
            catch(System.Exception error){Restore();Enabled=false;Mod.Log.Error("Street lighting stopped and restored its owned changes: "+error);}
        }
    }
}
