using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    // Additive intent; existing aggregate divider selections and schemas are unchanged.
    // Geometry rather than a borrowed lane entity survives save/reload and regeneration.
    public struct MGRIndividualDivider : IBufferElementData, ISerializable
    {
        public Unity.Mathematics.float3 A,B,C,D;
        public byte Style;
        public void Serialize<T>(T writer)where T:IWriter{writer.Write(A);writer.Write(B);writer.Write(C);writer.Write(D);writer.Write(Style);}
        public void Deserialize<T>(T reader)where T:IReader{reader.Read(out A);reader.Read(out B);reader.Read(out C);reader.Read(out D);reader.Read(out Style);}
    }
    internal static class IndividualDividerPolicy
    {
        internal static Bezier4x3 Path(MGRIndividualDivider value)=>new Bezier4x3(value.A,value.B,value.C,value.D);
        internal static MGRIndividualDivider Intent(Bezier4x3 path,byte style)=>new MGRIndividualDivider{A=path.a,B=path.b,C=path.c,D=path.d,Style=style};
        // A changed road cannot silently send an old selection to the adjacent lane.
        internal static bool Matches(MGRIndividualDivider value,Bezier4x3 path)=>GranularHighwayLinePolicy.ValidStyle(value.Style)&&GranularHighwayLinePolicy.Matches(Path(value),path);
        internal static bool Covers(Bezier4x3 path,Bezier4x3 stroke)
        {
            foreach(float t in new[]{0f,.5f,1f})
            {
                var p=MathUtils.Position(stroke,t);float distance=MathUtils.Distance(path,p,out float u);
                if(!Unity.Mathematics.math.isfinite(distance)||distance>.45f)return false;
                var a=Unity.Mathematics.math.normalizesafe(MathUtils.Tangent(path,u));var b=Unity.Mathematics.math.normalizesafe(MathUtils.Tangent(stroke,t));
                if(Unity.Mathematics.math.abs(Unity.Mathematics.math.dot(a,b))<.95f)return false;
            }
            return true;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool IndividualDividerSource(Entity owner,Entity source)
        {
            if(!EntityManager.Exists(owner)||!EntityManager.Exists(source)||EntityManager.HasComponent<Deleted>(source)||EntityManager.HasComponent<Game.Tools.Temp>(source)||!EntityManager.HasComponent<Owner>(source)||EntityManager.GetComponentData<Owner>(source).m_Owner!=owner)return false;
            if(EntityManager.HasComponent<MGRCurbMarking>(source)||EntityManager.HasComponent<MGRIntersectionPaint>(source)||_nodeDividerExternal.Contains(EntityManager,source))return false;
            if(EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)||EntityManager.HasComponent<Game.Net.PedestrianLane>(source)||EntityManager.HasComponent<Game.Net.TrackLane>(source))return false;
            if(!EntityManager.HasComponent<Curve>(source)||!EntityManager.HasComponent<PrefabRef>(source))return false;
            if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab,out var prefab)||!NodeLinePolicy.IsLineGeometry(prefab))return false;
            var curve=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(curve))return false;
            if(EntityManager.HasComponent<Node>(owner))return NodeDividerSource(owner,source);
            return EntityManager.HasComponent<Road>(owner)&&!IsOuterEdgePaint(owner,curve)&&!IsOpposingBoundary(owner,curve)&&IsInteriorLanePaint(owner,curve);
        }
        private List<Bezier4x3> IndividualDividerPaths(Entity owner,Entity source,Bezier4x3 path,byte style)
        {
            if(EntityManager.HasComponent<Road>(owner)&&EntityManager.HasComponent<Edge>(owner))
            {
                GetRoadStopRange(owner,path,LaneForward(owner,source,path),out float lower,out float upper);
                // Trim the shared center path before offsetting/dashing so preview
                // and placed strokes have the same approach stop-bar clearance.
                if(!StopLinePlacement.TrimRange(path,lower,upper,.55f,out path)||!Valid(path))return new List<Bezier4x3>();
            }
            // Node connector markings are separate from approach lane dividers.
            return HighwayLinePaths(owner,path,style,3);
        }
        internal bool IndividualDividerChoices(Entity owner,byte style,bool visibility,List<HoverLineChoice> choices)
        {
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return false;
            int before=choices.Count,count=0;var seen=new List<Bezier4x3>();
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
            {
                if(++count>256)break;var source=entry.m_SubLane;if(!IndividualDividerSource(owner,source))continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                if(seen.Exists(p=>GranularHighwayLinePolicy.Matches(p,path)))continue;seen.Add(path);
                choices.Add(new HoverLineChoice{Role=4,Source=source,Individual=true,Guides=new List<Bezier4x3>{path},Paths=visibility?new List<Bezier4x3>{path}:IndividualDividerPaths(owner,source,path,style)});
            }
            return choices.Count>before;
        }
        private bool TryIndividualDivider(Entity owner,Entity source,out MGRIndividualDivider intent)
        {
            intent=default;if(!EntityManager.HasBuffer<MGRIndividualDivider>(owner)||!IndividualDividerSource(owner,source))return false;
            var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
            int inspected=0;foreach(var item in EntityManager.GetBuffer<MGRIndividualDivider>(owner,true))if(++inspected>256)break;else if(IndividualDividerPolicy.Matches(item,path)){intent=item;return true;}
            return false;
        }
        internal bool OwnsIndividualDivider(Entity owner,Entity source)
        {
            if(!TryIndividualDivider(owner,source,out var intent)||intent.Style==GranularHighwayLinePolicy.Native)return false;
            if(intent.Style==GranularHighwayLinePolicy.Removed)return true;
            var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
            return (_marks.TryGetValue(owner,out var marks)||_joins.TryGetValue(owner,out marks))&&marks.Curves.Exists(m=>m.DividerUpgrade&&(m.NativeSource==source||m.NativeSource!=Entity.Null&&EntityManager.HasComponent<Curve>(m.NativeSource)&&GranularHighwayLinePolicy.Matches(path,EntityManager.GetComponentData<Curve>(m.NativeSource).m_Bezier)));
        }
        internal bool KeepsIndividualDivider(Entity owner,Entity source)=>TryIndividualDivider(owner,source,out var value)&&value.Style==GranularHighwayLinePolicy.Native;
        internal void ApplyIndividualDivider(Entity owner,Entity source,byte style)
        {
            if(Mod.Saving||!GranularHighwayLinePolicy.ValidStyle(style)||!IndividualDividerSource(owner,source))return;
            var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
            World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(owner);
            Dependency.Complete();
            if(!EntityManager.HasBuffer<MGRIndividualDivider>(owner))EntityManager.AddBuffer<MGRIndividualDivider>(owner);
            var buffer=EntityManager.GetBuffer<MGRIndividualDivider>(owner);int index=-1;
            for(int i=0;i<buffer.Length&&i<256;i++)if(IndividualDividerPolicy.Matches(buffer[i],path)){index=i;break;}
            if(index<0&&buffer.Length>=256){UpgradeFeedback.Report("Selected divider","Individual line limit reached; nothing changed.");return;}
            var value=IndividualDividerPolicy.Intent(path,style);if(index>=0)buffer[index]=value;else buffer.Add(value);
            // Buffer is no longer used after these calls; no native view is retained.
            if(EntityManager.HasComponent<Node>(owner)){QueueNodeRefresh(owner);JunctionMarkingSystem.RequestNodeRefresh(owner);RefreshNodePaint(owner);}
            else {QueueRoad(owner);SegmentUpgradePolicy.QueuePaint(EntityManager,owner);}
            UpgradeFeedback.Report("Selected divider",style==GranularHighwayLinePolicy.Native?"Selected divider restored; neighboring lines kept.":style==GranularHighwayLinePolicy.Removed?"Selected divider hidden; neighboring lines kept.":"Selected divider applied; neighboring lines kept.");
        }
        private void ApplyIndividualDividers(Entity owner,List<MarkCurve> output)
        {
            if(!EntityManager.HasBuffer<MGRIndividualDivider>(owner)||!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return;
            // Copy IDs before any later renderer/entity work. No structural changes here.
            var sources=new List<Entity>();var seen=new List<Bezier4x3>();int count=0;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
            {
                if(++count>256)break;if(!IndividualDividerSource(owner,entry.m_SubLane))continue;
                var curve=EntityManager.GetComponentData<Curve>(entry.m_SubLane).m_Bezier;
                if(seen.Exists(p=>GranularHighwayLinePolicy.Matches(p,curve)))continue;seen.Add(curve);sources.Add(entry.m_SubLane);
            }
            foreach(var source in sources)
            {
                if(!TryIndividualDivider(owner,source,out var intent))continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                var asset=intent.Style<=12?World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,LineShapePolicy.Yellow(intent.Style)):Entity.Null;
                if(intent.Style<=12&&asset==Entity.Null)continue;
                // Never erase a neighboring fork's stroke merely because the paths
                // converge. Legacy strokes without provenance require unique coverage.
                output.RemoveAll(m=>m.DividerUpgrade&&(m.NativeSource==source||m.NativeSource==Entity.Null&&IndividualDividerPolicy.Covers(path,m.Curve)
                    &&!sources.Exists(other=>other!=source&&IndividualDividerPolicy.Covers(EntityManager.GetComponentData<Curve>(other).m_Bezier,m.Curve))));
                if(intent.Style==GranularHighwayLinePolicy.Native||intent.Style==GranularHighwayLinePolicy.Removed)continue;
                foreach(var stroke in IndividualDividerPaths(owner,source,path,intent.Style))output.Add(new MarkCurve(stroke,asset){DividerUpgrade=true,NativeSource=source});
            }
        }
    }
}
