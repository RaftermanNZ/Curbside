using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Additive intent only: no prefab, entity or borrowed native-memory references
    // are serialized. Existing whole-node upgrades and their layouts are unchanged.
    public struct MGRHighwayLineIntent : IBufferElementData, ISerializable
    {
        public float3 A,B,C,D;
        public byte Style,Role;
        public void Serialize<T>(T writer)where T:IWriter{writer.Write(A);writer.Write(B);writer.Write(C);writer.Write(D);writer.Write(Style);writer.Write(Role);}
        public void Deserialize<T>(T reader)where T:IReader{reader.Read(out A);reader.Read(out B);reader.Read(out C);reader.Read(out D);reader.Read(out Style);reader.Read(out Role);}
    }
    internal static class GranularHighwayLinePolicy
    {
        internal const byte Native=254,Removed=255;
        internal static byte LegacyStyle(int role,byte value,bool themeYellow)
        {
            if(value==0)return 0;
            if(role==1){if(value==3)return Removed;if(value>=8&&value<=15)return (byte)(value-3);if(value==1)return themeYellow?(byte)2:(byte)1;if(value==2)return themeYellow?(byte)4:(byte)3;return value==4?(byte)1:value==5?(byte)3:value==6?(byte)2:(byte)4;}
            if(role==2)return value==5?Removed:value>=6?(byte)(value-1):value;
            return value==2?Removed:value==1?(byte)5:value==3?(byte)6:value==4?(byte)7:value==5?(byte)8:value==6?(byte)9:value==7?(byte)10:value==8?(byte)1:value==9?(byte)2:value==10?(byte)3:value==11?(byte)4:(byte)(value-1);
        }
        internal static bool ValidStyle(byte style)=>style>=1&&style<=12||style==Native||style==Removed;
        internal static Bezier4x3 Path(MGRHighwayLineIntent value)=>new Bezier4x3(value.A,value.B,value.C,value.D);
        internal static MGRHighwayLineIntent Intent(Bezier4x3 path,byte style,byte role)=>new MGRHighwayLineIntent{A=path.a,B=path.b,C=path.c,D=path.d,Style=style,Role=role};
        internal static bool Matches(Bezier4x3 a,Bezier4x3 b)
        {
            if(!Finite(a)||!Finite(b))return false;
            // A strict shape key tolerates roundoff and reversed native curves,
            // but cannot jump to a parallel neighboring edge or a changed fork.
            float forward=math.max(math.max(math.distance(a.a,b.a),math.distance(a.b,b.b)),math.max(math.distance(a.c,b.c),math.distance(a.d,b.d)));
            float reverse=math.max(math.max(math.distance(a.a,b.d),math.distance(a.b,b.c)),math.max(math.distance(a.c,b.b),math.distance(a.d,b.a)));
            return math.min(forward,reverse)<.04f;
        }
        private static bool Finite(Bezier4x3 p)=>math.all(math.isfinite(p.a))&&math.all(math.isfinite(p.b))&&math.all(math.isfinite(p.c))&&math.all(math.isfinite(p.d));
    }
    public partial class CurbLineEmissionSystem
    {
        internal bool HighwayLineChoices(Entity node,byte style,bool visibility,List<HoverLineChoice> choices)
        {
            if(!HighwayNode(node)||!EntityManager.HasBuffer<SubLane>(node))return false;
            var anchors=HighwayPaintAnchors(node);var seen=new List<Bezier4x3>();int count=0;
            foreach(var entry in EntityManager.GetBuffer<SubLane>(node,true))
            {
                if(++count>256)break;var source=entry.m_SubLane;int role=HighwayNodeSourceRole(node,source);if(role==0)continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(path))continue;
                if(seen.Exists(p=>GranularHighwayLinePolicy.Matches(p,path)))continue;seen.Add(path);
                if(role==2&&HighwayPaintAnchorPolicy.Yellow(path,anchors))role=1;
                var joined=HighwayPaintAnchorPolicy.Join(path,anchors);
                var paths=visibility?new List<Bezier4x3>{joined}:HighwayLinePaths(node,joined,style,role);
                choices.Add(new HoverLineChoice{Role=(byte)(role==1?0:role==2?3:4),Source=source,Guides=new List<Bezier4x3>{joined},Paths=paths});
            }
            return choices.Count>0;
        }
        private List<Bezier4x3> HighwayLinePaths(Entity node,Bezier4x3 path,byte style,int role)
        {
            var paths=new List<Bezier4x3>();
            if(style==3||style==4||LineShapePolicy.Passing(style))
            {
                float spacing=EdgeSpacing(LineShapePolicy.Edge(style));
                paths.Add(NetUtils.OffsetCurveLeftSmooth(path,-spacing));paths.Add(NetUtils.OffsetCurveLeftSmooth(path,spacing));
            }
            else paths.Add(path);
            return LineShapePolicy.Passing(style)?PassingPreview(node,true,(byte)(role==1?0:role==2?3:4),paths):LineShapePolicy.Strokes(paths,style);
        }
        internal void ApplyHighwayLine(Entity node,Entity source,byte style)
        {
            if(Mod.Saving||!GranularHighwayLinePolicy.ValidStyle(style)||HighwayNodeSourceRole(node,source)==0)return;
            var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;if(!Valid(path))return;
            // Complete the system's scheduled readers before adding an intent buffer.
            Dependency.Complete();
            if(!EntityManager.HasBuffer<MGRHighwayLineIntent>(node))EntityManager.AddBuffer<MGRHighwayLineIntent>(node);
            var intents=EntityManager.GetBuffer<MGRHighwayLineIntent>(node);int index=-1;
            for(int i=0;i<intents.Length;i++)if(GranularHighwayLinePolicy.Matches(GranularHighwayLinePolicy.Path(intents[i]),path)){index=i;break;}
            var value=GranularHighwayLinePolicy.Intent(path,style,(byte)HighwayNodeSourceRole(node,source));
            if(index>=0)intents[index]=value;
            else if(intents.Length<256)intents.Add(value);
            else {UpgradeFeedback.Report("Highway line","This node has reached its individual line limit. Nothing changed.");return;}
            // No native view survives the queued refresh/structural operations.
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("highway.line.apply","node="+node+" source="+source+" role="+value.Role+" style="+style+" start="+path.a+" end="+path.d);
            QueueNodeRefresh(node);RefreshNodePaint(node);
            UpgradeFeedback.Report("Highway line",style==GranularHighwayLinePolicy.Native?"Selected highway line restored.":style==GranularHighwayLinePolicy.Removed?"Selected highway line removed.":"Selected highway line applied. Other paths kept.");
        }
        internal bool KeepsNativeHighwayLine(Entity node,Entity source)=>HighwayNode(node)&&TryHighwayLineIntent(node,source,out var intent)&&intent.Style==GranularHighwayLinePolicy.Native;
        private bool TryHighwayLineIntent(Entity node,Entity source,out MGRHighwayLineIntent intent)
        {
            intent=default;if(!EntityManager.HasBuffer<MGRHighwayLineIntent>(node)||!EntityManager.HasComponent<Curve>(source))return false;
            var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
            foreach(var entry in EntityManager.GetBuffer<MGRHighwayLineIntent>(node,true))if(GranularHighwayLinePolicy.ValidStyle(entry.Style)&&GranularHighwayLinePolicy.Matches(GranularHighwayLinePolicy.Path(entry),path)){intent=entry;return true;}
            return false;
        }
        private void ApplyHighwayLineIntents(Entity node,List<MarkCurve> output)
        {
            if(!HighwayNode(node)||!EntityManager.HasBuffer<MGRHighwayLineIntent>(node)||!EntityManager.HasBuffer<SubLane>(node))return;
            var anchors=HighwayPaintAnchors(node);int count=0;var seen=new List<Bezier4x3>();
            foreach(var entry in EntityManager.GetBuffer<SubLane>(node,true))
            {
                if(++count>256)break;var source=entry.m_SubLane;int role=HighwayNodeSourceRole(node,source);if(role==0||!TryHighwayLineIntent(node,source,out var intent))continue;
                var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                if(seen.Exists(p=>GranularHighwayLinePolicy.Matches(p,path)))continue;seen.Add(path);
                var prefab=intent.Style==GranularHighwayLinePolicy.Native||intent.Style==GranularHighwayLinePolicy.Removed?Entity.Null:World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,LineShapePolicy.Yellow(intent.Style));
                if(intent.Style<=12&&prefab==Entity.Null)continue;
                // Remove only the selected native path's automatic copy.
                output.RemoveAll(m=>!m.Protected&&(m.NativeSource==source||GranularHighwayLinePolicy.Matches(m.Curve,path)));
                if(intent.Style==GranularHighwayLinePolicy.Native||intent.Style==GranularHighwayLinePolicy.Removed)continue;
                foreach(var stroke in HighwayLinePaths(node,HighwayPaintAnchorPolicy.Join(path,anchors),intent.Style,role))
                {
                    var mark=new MarkCurve(stroke,prefab,0,role==1,role==2){DividerUpgrade=role==3,NativeSource=source};output.Add(mark);
                }
            }
        }
    }
}
