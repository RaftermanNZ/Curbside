using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;
using Flags=Game.Objects.GeometryFlags;

class SignalWorkBudgetChecks
{
    const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Main()
    {
        var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var policy=a.GetType("MedianGrassReplacement.Systems.WireClearancePolicy",true);
        int limit=(int)policy.GetField("CoherentEvaluationLimit",S).GetRawConstantValue();Check(limit==96,"Review candidate budget changed");
        var core=new Core223();var points=new List<float3>{new float3(-5,0,-5),new float3(5,0,-5),new float3(5,0,5),new float3(-5,0,5)};var original=points.ToArray();
        var g=new ObjectGeometryData{m_Flags=Flags.Physical|Flags.Overridable,m_Bounds=new Bounds3(new float3(-.4f,0,-.4f),new float3(.4f,8,.4f)),m_Size=new float3(.8f,8,.8f)};
        var colliding=new CBody{G=g,P=new Transform(points[0],quaternion.identity),M=CollisionMask.Overground};
        var obstacles=new List<CBody>{colliding};var shapes=new List<CBody>{colliding};int site=0,reshape=0,describe=0;var seen=new HashSet<int>();
        Func<List<float3>,List<CBody>> build=p=>{describe++;return shapes;};
        Check(!core.SolveCoherent(points,obstacles,build,(i,p)=>{site++;seen.Add(i);return p;},(p,i,c)=>{reshape++;return null;},out var result,out var count),"Rejected reshape unexpectedly solved");
        Check(result==null&&count==95&&site==94&&reshape==94&&describe==1,"Rejected reshape work was not charged to the budget");Check(seen.Count==4,"First pole starved the other corners");
        int rejectedCalls=reshape;
        site=reshape=describe=0;
        Check(!core.SolveCoherent(points,obstacles,build,(i,p)=>{site++;return null;},(p,i,c)=>{reshape++;return null;},out result,out count),"Rejected site solved");
        Check(count==95&&site==94&&reshape==0&&describe==1,"Failed sites escaped the budget");
        site=reshape=describe=0;
        Check(core.SolveCoherent(points,new List<CBody>(),build,(i,p)=>{site++;return p;},(p,i,c)=>{reshape++;return null;},out result,out count),"Clear input rejected");
        Check(count==1&&site==0&&reshape==0&&describe==1,"Clear layout entered search");Check(original.SequenceEqual(points),"Input layout mutated");
        // The actual copied-obstacle solver must re-describe the complete
        // accepted candidate; returning stale geometry is never a success.
        int calls=0;
        Func<List<float3>,List<CBody>> repair=p=>{calls++;return calls==1?shapes:new List<CBody>{new CBody{G=g,P=new Transform(new float3(50,0,50),quaternion.identity),M=CollisionMask.Overground}};};
        Check(core.SolveCoherent(points,obstacles,repair,(i,p)=>p,(p,i,c)=>{var q=new List<float3>(p);q[i]=c;return q;},out result,out count),"Clear candidate not accepted");
        Check(calls==3&&count==3,"Independent final rebuild missing");
        calls=0;Check(!core.SolveCoherent(points,obstacles,p=>{calls++;return calls==3?null:repairWithoutCounter(p,calls,shapes,g);},(i,p)=>p,(p,i,c)=>{var q=new List<float3>(p);q[i]=c;return q;},out result,out count)&&result==null,"Invalid final rebuild accepted");
        Check(original.SequenceEqual(points),"Failed solve changed original layout");
        // Real production memo, with changed values, entity recycling, bounded
        // eviction, negative-result reuse and save/world clear semantics.
        var keyType=a.GetType("MedianGrassReplacement.Systems.SignalWorkKey",true);var memoType=a.GetType("MedianGrassReplacement.Systems.SignalWorkMemo`1",true).MakeGenericType(typeof(float3[]));
        var memo=Activator.CreateInstance(memoType,F,null,new object[]{4},null);var put=memoType.GetMethod("Put",F);var get=memoType.GetMethod("TryGet",F);var clear=memoType.GetMethod("Clear",F);
        Func<int,object> key=v=>{var k=Activator.CreateInstance(keyType,true);keyType.GetMethod("Add",F).Invoke(k,new object[]{v});return k;};
        var owner=new Entity{Index=1,Version=1};put.Invoke(memo,new object[]{owner,key(1),null});
        var args=new object[]{owner,key(1),null};Check((bool)get.Invoke(memo,args)&&args[2]==null,"Negative result did not reuse");
        args=new object[]{owner,key(2),null};Check(!(bool)get.Invoke(memo,args),"Changed source was ignored");
        args=new object[]{new Entity{Index=1,Version=2},key(1),null};Check(!(bool)get.Invoke(memo,args),"Recycled entity inherited cache");
        for(int i=0;i<20;i++){put.Invoke(memo,new object[]{new Entity{Index=i+2,Version=1},key(i),original});Check((int)memoType.GetProperty("Count",F).GetValue(memo)<=4,"Cache capacity grew");}
        args=new object[]{owner,key(1),null};Check(!(bool)get.Invoke(memo,args),"Eviction left stale owner");clear.Invoke(memo,null);Check((int)memoType.GetProperty("Count",F).GetValue(memo)==0,"World/save reset retained work");
        CheckProductionLayoutMemo(a,core);
        File.WriteAllText("SignalWorkBudgetChecks.json",new JavaScriptSerializer().Serialize(new{checks,limit,rejectedReshapeCalls=rejectedCalls,reportedRejectedEvaluations=95,oldRejectedReshapeCalls=448,scope="Actual compiled coherent solver and exact-value bounded memo. Copied native collision math in isolated Mono. No ECS World, jobs, render/save lifecycle or live frame-time execution."}));
        Console.WriteLine("PASS: "+checks+" budget/reuse assertions; rejected reshapes 448 -> "+rejectedCalls+"; failed sites counted, all corners visited, final geometry independently checked.");
    }
    class LayoutFixture { public CBank[] banks; public float[][] supports; public int[] sources; }
    static void CheckProductionLayoutMemo(Assembly a,Core223 core)
    {
        var fixture=new JavaScriptSerializer().Deserialize<LayoutFixture[]>(File.ReadAllText("WireCorners229.json"))[0];
        var layoutType=a.GetType("MedianGrassReplacement.Systems.WireLayoutWork",true);
        var keyType=a.GetType("MedianGrassReplacement.Systems.SignalWorkKey",true);
        var memoType=a.GetType("MedianGrassReplacement.Systems.SignalWorkMemo`1",true).MakeGenericType(typeof(float3[]));
        var layout=Activator.CreateInstance(layoutType,F,null,new object[]{4},null);
        var memo=layoutType.GetField("memo",F).GetValue(layout);
        var supports=fixture.supports.Select(CShape.V).ToList();
        var fit=layoutType.GetMethod("Fit",F);var roads=core.Banks(fixture.banks);
        var entries=(IDictionary)memoType.GetField("entries",F).GetValue(memo);
        Func<Entity,object> storedKey=node=>entries[node].GetType().GetField("Key",F).GetValue(entries[node]);
        for(int pass=0;pass<20;pass++)
        {
            var node=new Entity{Index=pass+100,Version=1};var args=new object[]{node,supports,roads,fixture.sources.ToList(),(byte)(pass%2==0?1:3),null};
            Check((bool)fit.Invoke(layout,args),"Captured layout failed before memo reuse");var first=((List<float3>)args[5]).ToArray();var key=storedKey(node);
            Check((bool)fit.Invoke(layout,args)&&ReferenceEquals(key,storedKey(node)),"Equal work was rebuilt rather than reused");
            ((List<float3>)args[5])[0]+=new float3(500,0,500);
            Check((bool)fit.Invoke(layout,args)&&first.SequenceEqual((List<float3>)args[5]),"Caller mutation corrupted remembered geometry");
            var original=supports[0];supports[0]+=new float3(.25f,0,0);
            fit.Invoke(layout,args);Check(!ReferenceEquals(key,storedKey(node)),"Changed native mount reused stale layout");supports[0]=original;
        }
        Check((int)memoType.GetProperty("Count",F).GetValue(memo)==4,"Production layout work exceeded capacity");
        var badNode=new Entity{Index=777,Version=1};supports.Clear();var invalid=new object[]{badNode,supports,roads,fixture.sources.ToList(),(byte)1,null};
        Check(!(bool)fit.Invoke(layout,invalid),"Missing supports accepted");var failureKey=storedKey(badNode);
        Check(!(bool)fit.Invoke(layout,invalid)&&ReferenceEquals(failureKey,storedKey(badNode)),"Repeated identical failed layout was rebuilt");
    }
    static List<CBody> repairWithoutCounter(List<float3> p,int call,List<CBody> shapes,ObjectGeometryData g)=>call==1?shapes:new List<CBody>{new CBody{G=g,P=new Transform(new float3(50,0,50),quaternion.identity),M=CollisionMask.Overground}};
}
