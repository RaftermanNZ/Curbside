using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;
using Unity.Entities;
using MedianGrassReplacement.Systems;

namespace Game.Common { public struct Deleted {} }
namespace Game.Tools { public struct Temp {} }
namespace Game.Net
{
    public struct ConnectedEdge { public int m_Edge; }
    public struct SubLane { public int m_SubLane; }
    public struct Edge { public int m_Start,m_End; }
    public struct Curve { public Bezier4x3 m_Bezier; }
}
namespace Unity.Entities
{
    public class EntityQuery
    {
        public readonly HashSet<int> Excluded=new HashSet<int>();
        public bool MatchesIgnoreFilter(int entity)=>!Excluded.Contains(entity);
    }
    public class Buffer<T>:List<T> { public int Length=>Count; }
    public class EntityManager
    {
        private readonly Dictionary<int,Dictionary<Type,object>> data=new Dictionary<int,Dictionary<Type,object>>();
        public readonly Dictionary<Type,int> Writes=new Dictionary<Type,int>();
        public void Add(int id){if(!data.ContainsKey(id))data[id]=new Dictionary<Type,object>();}
        public bool Exists(int id)=>data.ContainsKey(id);
        public void Destroy(int id)=>data.Remove(id);
        public void Put<T>(int id,T value){Add(id);data[id][typeof(T)]=value;}
        public void Remove<T>(int id){if(Exists(id))data[id].Remove(typeof(T));}
        public bool HasComponent<T>(int id)=>Exists(id)&&data[id].ContainsKey(typeof(T));
        public T GetComponentData<T>(int id)=>(T)data[id][typeof(T)];
        public bool HasBuffer<T>(int id)=>Exists(id)&&data[id].ContainsKey(typeof(Buffer<T>));
        public Buffer<T> AddBuffer<T>(int id){Add(id);var value=new Buffer<T>();data[id][typeof(Buffer<T>)]=value;return value;}
        public Buffer<T> GetBuffer<T>(int id,bool readOnly=false)
        {
            if(!readOnly){Writes.TryGetValue(typeof(T),out var count);Writes[typeof(T)]=count+1;}
            return (Buffer<T>)data[id][typeof(Buffer<T>)];
        }
        public int WriteCount<T>()=>Writes.TryGetValue(typeof(T),out var count)?count:0;
    }
}
namespace MedianGrassReplacement.Systems
{
    public struct MGRCrosswalkApproachIntent { public float3 Direction;public byte Appearance; }
    public partial class CrosswalkUpgradeSystem { public EntityManager EntityManager=new EntityManager(); }
    public partial class IntersectionPaintSystem
    {
        public EntityManager EntityManager=new EntityManager();
        private readonly EntityQuery _sources=new EntityQuery();
        private static readonly HashSet<int> _requestedSources=new HashSet<int>();
        private readonly Dictionary<int,int> _clipRetries=new Dictionary<int,int>();
        public static HashSet<int> Requests=>_requestedSources;
        public void RejectSource(int source)=>_sources.Excluded.Add(source);
        internal static void RequestSource(int source)=>_requestedSources.Add(source);
    }
}
class ChoiceRefreshFixture243
{
    static int assertions,cases;
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    static float3 Dir(float angle)=>new float3(math.cos(math.radians(angle)),0,math.sin(math.radians(angle)));
    static void Road(EntityManager em,int road,int node,float angle)
    {
        var d=Dir(angle);em.Put(road,new Game.Net.Edge{m_Start=node,m_End=road+1000});
        em.Put(road,new Game.Net.Curve{m_Bezier=new Bezier4x3(default(float3),d,d*2,d*3)});
    }
    static CrosswalkUpgradeSystem Crossing(byte appearance,int legs=4)
    {
        var s=new CrosswalkUpgradeSystem();s.EntityManager.Add(1);
        var connections=s.EntityManager.AddBuffer<Game.Net.ConnectedEdge>(1);
        var intents=s.EntityManager.AddBuffer<MGRCrosswalkApproachIntent>(1);
        for(int i=0;i<legs;i++)
        {
            Road(s.EntityManager,10+i,1,i*360f/legs);connections.Add(new Game.Net.ConnectedEdge{m_Edge=10+i});
            intents.Add(new MGRCrosswalkApproachIntent{Direction=Dir(i*360f/legs),Appearance=appearance});
        }
        return s;
    }
    static void Pruning()
    {
        foreach(byte style in new byte[]{0,1,2})foreach(int legs in new[]{3,4,5,6})
        {
            for(int fault=0;fault<12;fault++)
            {
                cases++;var s=Crossing(style,legs);var em=s.EntityManager;
                if(fault==1)em.Remove<Game.Net.Curve>(10);
                if(fault==2)em.Remove<Game.Net.Edge>(10);
                if(fault==3)em.Put(10,new Game.Tools.Temp());
                if(fault==4)em.Destroy(10);
                if(fault==5){var e=em.GetComponentData<Game.Net.Edge>(10);e.m_Start=500;em.Put(10,e);}
                if(fault==6){var c=em.GetComponentData<Game.Net.Curve>(10);c.m_Bezier.b.x=float.NaN;em.Put(10,c);}
                if(fault==7)em.Put(10,new Game.Net.Curve{m_Bezier=new Bezier4x3(default(float3),default(float3),default(float3),default(float3))});
                if(fault==8)em.GetBuffer<Game.Net.ConnectedEdge>(1,true).Clear();
                if(fault==9)em.Remove<Buffer<Game.Net.ConnectedEdge>>(1);
                if(fault==10)em.Put(10,new Game.Common.Deleted());
                if(fault==11){em.Put(10,new Game.Common.Deleted());em.Put(10,new Game.Tools.Temp());}
                s.PruneNodeIntents(1);
                var intents=em.GetBuffer<MGRCrosswalkApproachIntent>(1,true);
                Check(intents.Count==legs,"Settling/unchanged topology discarded intent: "+fault);
                Check(em.WriteCount<MGRCrosswalkApproachIntent>()==0,"No-op pruning dirtied intent buffer");
                foreach(var intent in intents)Check(intent.Appearance==style,"Saved appearance changed");
                if(fault==1){Road(em,10,1,0);s.PruneNodeIntents(1);Check(intents.Count==legs,"Choice lost after geometry recovered");}
            }
            cases++;
            var deleted=Crossing(style,legs);deleted.EntityManager.Put(10,new Game.Common.Deleted());
            deleted.EntityManager.GetBuffer<Game.Net.ConnectedEdge>(1,true).RemoveAt(0);
            deleted.PruneNodeIntents(1);
            Check(deleted.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==legs-1,"Confirmed removed approach retained");
            Check(deleted.EntityManager.WriteCount<MGRCrosswalkApproachIntent>()==1,"Real pruning must use one writable buffer");
            cases++;
            var rotated=Crossing(style,legs);Road(rotated.EntityManager,10,1,19);rotated.PruneNodeIntents(1);
            Check(rotated.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==legs-1,"Unmatched complete direction not pruned");
            cases++;
            var near=Crossing(style,legs);Road(near.EntityManager,10,1,17);near.PruneNodeIntents(1);
            Check(near.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==legs,"Existing angular tolerance changed");
        }
        cases++;
        var all=Crossing(2);foreach(var c in all.EntityManager.GetBuffer<Game.Net.ConnectedEdge>(1,true))all.EntityManager.Put(c.m_Edge,new Game.Common.Deleted());
        all.PruneNodeIntents(1);Check(all.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==4,"Unsettled deleted bank pruned durable choices");
        cases++;
        var mixed=Crossing(1);mixed.EntityManager.Put(10,new Game.Common.Deleted());mixed.EntityManager.Remove<Game.Net.Curve>(11);
        mixed.PruneNodeIntents(1);Check(mixed.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==4,"Partial readiness caused partial destructive pruning");
        cases++;
        var reverse=Crossing(1);var edge=reverse.EntityManager.GetComponentData<Game.Net.Edge>(10);edge.m_End=1;edge.m_Start=1000;
        reverse.EntityManager.Put(10,edge);reverse.EntityManager.Put(10,new Game.Net.Curve{m_Bezier=new Bezier4x3(new float3(3,0,0),new float3(2,0,0),new float3(1,0,0),default(float3))});
        reverse.PruneNodeIntents(1);Check(reverse.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==4,"Reversed road lost served direction");
        cases++;
        var empty=Crossing(1);empty.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Clear();empty.PruneNodeIntents(1);
        Check(empty.EntityManager.WriteCount<MGRCrosswalkApproachIntent>()==0,"Empty intent buffer was made writable");
        cases++;
        var cancelled=Crossing(2);cancelled.EntityManager.Put(10,new Game.Common.Deleted());cancelled.PruneNodeIntents(1);
        cancelled.EntityManager.Remove<Game.Common.Deleted>(10);cancelled.PruneNodeIntents(1);
        Check(cancelled.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==4,"Cancelled replacement lost hidden approach");
        cases++;
        var replacement=Crossing(1);replacement.EntityManager.Put(10,new Game.Common.Deleted());Road(replacement.EntityManager,99,1,0);
        replacement.EntityManager.GetBuffer<Game.Net.ConnectedEdge>(1,true).Add(new Game.Net.ConnectedEdge{m_Edge=99});replacement.PruneNodeIntents(1);
        replacement.EntityManager.GetBuffer<Game.Net.ConnectedEdge>(1,true).RemoveAt(0);replacement.PruneNodeIntents(1);
        Check(replacement.EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(1,true).Count==4,"Settled replacement failed to preserve approach direction");
    }
    static void AddPaint(EntityManager em,int owner,params int[] lanes)
    {
        em.Add(owner);var b=em.AddBuffer<Game.Net.SubLane>(owner);foreach(var lane in lanes){em.Add(lane);b.Add(new Game.Net.SubLane{m_SubLane=lane});}
    }
    static void Refresh()
    {
        cases++;var s=new IntersectionPaintSystem();var em=s.EntityManager;
        em.Put(20,new Game.Net.Edge{m_Start=1,m_End=2});AddPaint(em,20,100,101);AddPaint(em,1,101,102);AddPaint(em,2,103);AddPaint(em,99,999);
        IntersectionPaintSystem.Requests.Clear();s.RequestCommittedRoadPaint(20);
        Check(IntersectionPaintSystem.Requests.SetEquals(new[]{100,101,102,103}),"Targeted refresh missed local paint or included unrelated owner");
        s.RequestCommittedRoadPaint(20);Check(IntersectionPaintSystem.Requests.Count==4,"Repeated road request duplicated source work");
        em.Put(21,new Game.Net.Edge{m_Start=1,m_End=3});AddPaint(em,21,104);AddPaint(em,3,105);
        s.RequestCommittedRoadPaint(21);Check(IntersectionPaintSystem.Requests.SetEquals(new[]{100,101,102,103,104,105}),"Old/new road endpoint union incomplete");
        foreach(int fault in new[]{0,1,2,3})
        {
            cases++;IntersectionPaintSystem.Requests.Clear();em.Put(30,new Game.Net.Edge{m_Start=1,m_End=2});
            if(fault==0)em.Destroy(30);if(fault==1)em.Put(30,new Game.Common.Deleted());if(fault==2)em.Put(30,new Game.Tools.Temp());if(fault==3)em.Remove<Game.Net.Edge>(30);
            s.RequestCommittedRoadPaint(30);Check(IntersectionPaintSystem.Requests.Count==0,"Invalid committed road requested paint");
            em.Destroy(30);
        }
        cases++;IntersectionPaintSystem.Requests.Clear();em.Put(40,new Game.Net.Edge{m_Start=1,m_End=1});AddPaint(em,40,106);s.RequestCommittedRoadPaint(40);
        Check(IntersectionPaintSystem.Requests.SetEquals(new[]{101,102,106}),"Self endpoint was missed or overexpanded");
        Check(em.WriteCount<Game.Net.SubLane>()==0,"Refresh discovery obtained a writable lane buffer");
        cases++;IntersectionPaintSystem.Requests.Clear();em.Put(50,new Game.Net.Edge{m_Start=500,m_End=501});s.RequestCommittedRoadPaint(50);
        Check(IntersectionPaintSystem.Requests.Count==0,"Not-ready children guessed an owner");
        AddPaint(em,50,107);s.RequestCommittedRoadPaint(50);Check(IntersectionPaintSystem.Requests.SetEquals(new[]{107}),"Later available lane failed targeted request");
        cases++;IntersectionPaintSystem.Requests.Clear();em.Put(60,new Game.Net.Edge{m_Start=61,m_End=62});AddPaint(em,60,108);AddPaint(em,61,109);AddPaint(em,62,110);em.Put(61,new Game.Tools.Temp());em.Put(62,new Game.Common.Deleted());
        s.RequestCommittedRoadPaint(60);Check(IntersectionPaintSystem.Requests.SetEquals(new[]{108}),"Temporary/deleted endpoints requested paint");
        cases++;IntersectionPaintSystem.Requests.Clear();em.Put(70,new Game.Net.Edge{m_Start=71,m_End=72});AddPaint(em,70,111,112,113);AddPaint(em,71,114);AddPaint(em,72,115);
        // Archetype exclusions are supplied by the unchanged production source query.
        // Model generated/vehicle/temporary sources and one stale child reference.
        s.RejectSource(112);s.RejectSource(113);s.RejectSource(114);em.Destroy(115);
        s.RequestCommittedRoadPaint(70);Check(IntersectionPaintSystem.Requests.SetEquals(new[]{111}),"Commit refresh admitted excluded or stale sources");
    }
    static void Membership()
    {
        foreach(int count in new[]{0,1,8,128})foreach(int duplicates in new[]{0,1,3})
        {
            cases++;var em=new EntityManager();var lanes=em.AddBuffer<Game.Net.SubLane>(1);
            for(int i=0;i<count;i++)lanes.Add(new Game.Net.SubLane{m_SubLane=100+i});
            for(int i=0;i<duplicates;i++)lanes.Add(new Game.Net.SubLane{m_SubLane=5});
            GeneratedVisualLifecycle.DetachLaneMembership(em,1,5);
            Check(lanes.Count==count,"Membership cleanup removed unrelated lanes or kept duplicates");
            Check(em.WriteCount<Game.Net.SubLane>()==(duplicates==0?0:1),"No-op membership cleanup dirtied buffer");
            for(int i=0;i<count;i++)Check(lanes[i].m_SubLane==100+i,"Unrelated lane order changed");
            GeneratedVisualLifecycle.DetachLaneMembership(em,1,5);
            Check(em.WriteCount<Game.Net.SubLane>()==(duplicates==0?0:1),"Repeated retirement obtained another writable buffer");
        }
    }
    static void Main()
    {
        Pruning();Refresh();Membership();
        Console.WriteLine("PASS: "+cases+" choice/refresh/membership scenarios; "+assertions+" assertions using production methods and managed ECS adapters. Native World, writer, GPU and FPS remain untested.");
    }
}
