using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;
using System.Web.Script.Serialization;using Unity.Mathematics;using Colossal.Mathematics;using Curbside.CornerStudy;
class WireSpanLaneChecks239
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static float3 V(float[] p)=>new float3(p[0],p[1],p[2]);
 static Bezier4x3 Line(float3 a,float3 b)=>new Bezier4x3(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b);
 static void Main(string[] args)
 {
  var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");var policy=asm.GetType("MedianGrassReplacement.Systems.WireSpanLanePolicy",true);
  var garden=asm.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);var far=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
  var at=far.GetNestedType("Approach",BindingFlags.NonPublic);var planner=asm.GetType("MedianGrassReplacement.Systems.WireLayoutPlanner",true);
  var pack=asm.GetType("GardenCitySignals.SignalPackPolicy",true);foreach(string n in new[]{"UseRigidSignLayout","UseLowerRigidSignLayout"})pack.GetProperty(n,S).GetSetMethod(true).Invoke(null,new object[]{true});
  object Call(Type t,string n,params object[] a)=>t.GetMethod(n,S).Invoke(null,a);
  void Set(object a,string n,object value)=>at.GetField(n,F).SetValue(a,value);
  var curbs=new List<Bezier4x3>{Line(new float3(2,0,-4),new float3(2,0,4)),Line(new float3(18,0,-4),new float3(18,0,4)),Line(new float3(-4,0,5),new float3(-4,0,6)),Line(new float3(24,0,5),new float3(24,0,6))};
  var lanes=new List<float3>{new float3(5,0,-10),new float3(10,0,-10),new float3(15,0,-10)};
  object[] Input()=>new object[]{lanes,new float3(0,0,-10),new float3(20,0,-10),float3.zero,new float3(20,0,0),new float3(1,0,0),curbs,null};
  bool Map(object[] a)=>(bool)Call(policy,"TryMap",a);
  var normal=Input();Check(Map(normal),"Simple single road band rejected");var heads=(float[])normal[7];
  Check(heads.Length==3&&math.abs(heads[0]-6.1f)<.002f&&math.abs(heads[2]-13.9f)<.002f,"Relative lane fractions changed");
  foreach(string scenario in new[]{"nonfinite-inward","nonfinite-lane","outside-bank","zero-bank","too-long","reverse-projection","no-curbs","multiple-bands","coincident-curb","collapsed-lanes"})
  {
   var a=Input();
   if(scenario=="nonfinite-inward")a[5]=new float3(float.NaN,0,0);
   if(scenario=="nonfinite-lane")a[0]=new List<float3>{new float3(float.NaN,0,0)};
   if(scenario=="outside-bank")a[0]=new List<float3>{new float3(21,0,-10)};
   if(scenario=="zero-bank")a[2]=a[1];
   if(scenario=="too-long")a[4]=new float3(61,0,0);
   if(scenario=="reverse-projection")a[5]=new float3(-1,0,0);
   if(scenario=="no-curbs")a[6]=null;
   if(scenario=="multiple-bands")a[6]=new List<Bezier4x3>(curbs){Line(new float3(8,0,-2),new float3(8,0,2)),Line(new float3(12,0,-2),new float3(12,0,2))};
   if(scenario=="coincident-curb")a[6]=new List<Bezier4x3>(curbs){Line(new float3(4,0,0),new float3(8,0,0))};
   if(scenario=="collapsed-lanes")a[0]=new List<float3>{new float3(5,0,-10),new float3(5.01f,0,-10)};
   Check(!Map(a),"Malformed geometry was silently forced into validity: "+scenario);
  }
  var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var capture=json.Deserialize<Capture>(File.ReadAllText(args[0]));
  int plans=0,banks=0,phaseBanks=0,retainedStraight=0,cachedBanks=0;var examples=new List<object>();
  foreach(var scene in capture.nodes.Where(n=>n.slices.Length==n.roads.Length*2&&n.slices.Length>0))for(int pass=0;pass<20;pass++)
  {
   float theta=pass*math.PI/10;var shift=pass<10?float3.zero:new float3(6000,0,-7000);
   Func<float3,float3> P=p=>shift+new float3(p.x*math.cos(theta)-p.z*math.sin(theta),p.y,p.x*math.sin(theta)+p.z*math.cos(theta));
   Func<float2,float2> D=p=>new float2(p.x*math.cos(theta)-p.y*math.sin(theta),p.x*math.sin(theta)+p.y*math.cos(theta));
   Func<float[][],Bezier4x3> B=c=>new Bezier4x3(P(V(c[0])),P(V(c[1])),P(V(c[2])),P(V(c[3])));
   var slices=scene.slices.Select(s=>new CurbSlice{Road=s.roadIndex,Side=s.sideIndex,Curb=B(s.curb),Back=B(s.back),HasBack=true,Outside=P(V(s.Outside))-shift}).ToList();
   var corners=CornerRule.Calculate(slices,.7238954f);var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(at));
   foreach(var r in scene.roads)
   {
    var road=Activator.CreateInstance(at);var incoming=r.lanes.Select(p=>P(V(p))).ToList();Set(road,"Left",P(V(r.left)));Set(road,"Right",P(V(r.right)));Set(road,"Outward",D(new float2(r.outward[0],r.outward[1])));
    Set(road,"Incoming",incoming);Set(road,"WireBank",incoming);Set(road,"WireCurbs",slices.Select(s=>s.Curb).ToList());Set(road,"LaneMasks",Enumerable.Range(0,incoming.Count).Select(i=>(ushort)(1<<i)).ToList());Set(road,"HasJunctionCentre",true);Set(road,"JunctionCentre",P(V(scene.centre)));roads.Add(road);
   }
   foreach(byte style in new byte[]{1,3})
   {
    var call=new object[]{corners,roads,Enumerable.Range(0,roads.Count).ToList(),scene.roads.Select(r=>P(V(r.left))).ToList(),style,null,null,null};
    Check((bool)Call(planner,"Build",call),"Captured complete layout failed "+scene.node);plans++;
    var supports=(float3[])call[5];
    Check((bool)Call(garden,"CacheWireLayouts",roads,supports,Enumerable.Range(0,roads.Count).ToList(),style),"Head plan did not bake atomically");
    for(int bank=0;bank<roads.Count;bank++)
    {
     var road=roads[bank];var layout=new object[]{road,supports[bank],supports,style,null,null,0};
     Check((bool)Call(garden,"TryWireLayout",layout),"Renderer/planner eligibility diverged");banks++;
     var inward=(float3)layout[4];var distances=(float[])layout[5];int target=(int)layout[6];float projection=math.dot(supports[target]-supports[bank],inward);
     var cached=at.GetField("WireHeads",F).GetValue(road);Check(cached!=null,"Cached head plan missing");
     // Remove expensive geometry inputs from this controlled copy. The baked
     // layout must still serve new native actors/phase subsets without fitting.
     Set(road,"WireCurbs",null);var reused=new object[]{road,supports[bank],supports,style,null,null,0};
     Check((bool)Call(garden,"TryWireLayout",reused)&&((float[])reused[5]).SequenceEqual(distances),"Controller rebind refitted or changed the head plan");cachedBanks++;
     var changedPose=new object[]{road,supports[bank]+new float3(.1f,0,0),supports,style,null,null,0};
     Check(!(bool)cached.GetType().GetMethod("TryRead",F).Invoke(cached,changedPose),"Changed placement reused stale head cache");
     if(pass==0)examples.Add(new{scene.node,style,bank,inward=new[]{inward.x,inward.y,inward.z},target,supports=supports.Select(p=>new[]{p.x,p.y,p.z}).ToArray(),distances,
      headPositions=distances.Select(d=>supports[bank]+(supports[target]-supports[bank])*(d/projection)).Select(p=>new[]{p.x,p.y,p.z}).ToArray()});
     Check(distances.Length==scene.roads[bank].lanes.Length,"Native lane head count changed");
     Check(distances.All(d=>math.isfinite(d)&&d>=.349f&&d<=projection-.299f),"Head extends beyond its cable");
     var old=new object[]{at.GetField("Incoming",F).GetValue(road),at.GetField("Outward",F).GetValue(road),supports[bank],null,null};
     if((bool)Call(garden,"TryLayout",old)&&((float[])old[4]).Last()+.3f<=projection)
     {retainedStraight++;Check(((float[])old[4]).SequenceEqual(distances),"A successful existing straight layout changed");}
     foreach(ushort mask in Enumerable.Range(1,(1<<distances.Length)-1).Select(i=>(ushort)i))
     {
      var phase=Call(garden,"PhaseApproach",road,mask);var phaseCall=new object[]{phase,supports[bank],supports,style,null,null,0};
      Check((bool)Call(garden,"TryWireLayout",phaseCall),"Protected phase lost the complete-bank mapping");phaseBanks++;
      var subset=(float[])phaseCall[5];Check(subset.Length==math.countbits((uint)mask),"Protected phase head count mismatch");
      foreach(float d in subset)Check(distances.Any(v=>math.abs(v-d)<.001f),"Protected phase moved a shared lane head");
     }
     var unknown=Call(garden,"PhaseApproach",road,(ushort)32768);var invalid=new object[]{unknown,supports[bank],supports,style,null,null,0};
     Check(!(bool)Call(garden,"TryWireLayout",invalid),"Unmapped traffic phase gained a guessed head");
    }
   }
  }
  File.WriteAllText(args[1],json.Serialize(new{checks,plans,banks,phaseBanks,retainedStraight,cachedBanks,examples,
   scope="Compiled candidate policies on captured curb geometry and twenty transforms. Tests rendered-head layout inputs, phase subsets, native head counts, exact preservation of successful straight fits, and malformed input rejection. No ECS World, actual mesh composition, native collision, save writer, TTE execution or FPS measured."}));
  Console.WriteLine("PASS: "+checks+" assertions; "+plans+" complete plans, "+banks+" head banks, "+phaseBanks+" protected subsets, "+retainedStraight+" unchanged successful straight banks.");
 }
}
