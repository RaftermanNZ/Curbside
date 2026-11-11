using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Web.Script.Serialization;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Mathematics;
using Curbside.CornerStudy;
using Transform=Game.Objects.Transform;
class RBank {public float[] left,right,outward;public float[][] lanes;public string road;}
class RActor {public string actor,prefab;public float[] position,rotation;public int source,vehicleMask;}
class RCurb {public string road;public int side;public float[] a,b,c,d;}
class RScene {public string node;public RBank[] banks;public RActor[] actors;public RCurb[] curbs;public byte style;}
class RFixture {public RScene[] scenes;}
class RBounds {public float[] min,max;}
class RCatalog {public Dictionary<string,RBounds> meshes;public string packageSha256;}
class ActualJunctionReplay232
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static float3 V(float[] p)=>new float3(p[0],p[1],p[2]);
 static object Call(Type t,string name,params object[] args)=>t.GetMethod(name,S).Invoke(null,args);
 static void Set(Type type,object a,string n,object v){var f=type.GetField(n,F);if(f!=null)f.SetValue(a,v);}
 static void Main()
 {
  var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");var far=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
  var garden=assembly.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);var approach=far.GetNestedType("Approach",BindingFlags.NonPublic);
  var data=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<RFixture>(File.ReadAllText("Actual-Junctions.json"));
  var catalog=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<RCatalog>(File.ReadAllText("SignalLayoutCatalog225.json"));
  float envelope=0;
  foreach(var item in catalog.meshes.Where(p=>p.Key.Contains("PedHead")))foreach(float x in new[]{item.Value.min[0],item.Value.max[0]})foreach(float z in new[]{item.Value.min[2],item.Value.max[2]})envelope=math.max(envelope,math.length(new float2(x,z+.44f)));
  Check(envelope<.9f,"Current pedestrian fixture exceeds fixed setback before corner fitting");
  var outcomes=new List<object>();int accepted=0,proposals=0;var positions=new List<string>();
  foreach(var scene in data.scenes)for(int pass=0;pass<20;pass++)
  {
   float theta=pass*math.PI/10;var offset=pass<10?float3.zero:new float3(9000,0,-7000);
   Func<float3,float3> P=p=>offset+new float3(p.x*math.cos(theta)-p.z*math.sin(theta),p.y,p.x*math.sin(theta)+p.z*math.cos(theta));
   Func<float2,float2> D=d=>new float2(d.x*math.cos(theta)-d.y*math.sin(theta),d.x*math.sin(theta)+d.y*math.cos(theta));
   var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));var surface=new List<float3>();
   foreach(var b in scene.banks)
   {
    var a=Activator.CreateInstance(approach);var left=P(V(b.left));var right=P(V(b.right));Set(approach,a,"Left",left);Set(approach,a,"Right",right);
    Set(approach,a,"Outward",D(new float2(b.outward[0],b.outward[1])));Set(approach,a,"Incoming",b.lanes.Select(p=>P(V(p))).ToList());Set(approach,a,"HasJunctionCentre",false);
    roads.Add(a);surface.Add(left);surface.Add(right);
   }
   var mounts=new List<float3>();var sources=new List<int>();
   foreach(var actor in scene.actors)
   {
    var source=new Transform(P(V(actor.position)),math.mul(quaternion.RotateY(-theta),new quaternion(actor.rotation[0],actor.rotation[1],actor.rotation[2],actor.rotation[3])));
    var args=new object[]{source,roads,actor.source,null};bool ok=(bool)Call(far,"TrySignalMountFrom",args);
    Check(ok,"Released mount rejected actual source "+scene.node+"/"+actor.actor+" pass="+pass);
    var placed=(Transform)args[3];Check((bool)Call(far,"IsFarSide",placed.m_Position,roads,actor.source),"Mount is not far side");
    mounts.Add(placed.m_Position);sources.Add(actor.source);accepted++;
   }
   var original=mounts.ToArray();var align=new object[]{mounts,roads,null};bool aligned=(bool)Call(garden,"TryAlignedWireCorners",align);
   if(aligned)
   {
    var fitted=(List<float3>)align[2];bool valid=true;
    for(int i=0;i<fitted.Count;i++)valid &=(bool)Call(far,"IsRoadside",fitted[i],roads)&&(bool)Call(far,"IsFarSide",fitted[i],roads,sources[i]);
    if(valid)mounts=fitted;
   }
   Check(mounts.Count==scene.actors.Length,"Optional repair discarded custom mounting plan");
   if(pass==0)foreach(var point in mounts)positions.Add(scene.node+" "+point);
   var slices=new List<CurbSlice>();
   foreach(var curb in scene.curbs)
   {
    int index=Array.FindIndex(scene.banks,b=>b.road==curb.road);var bank=scene.banks[index];var mouth=(P(V(bank.left))+P(V(bank.right)))*.5f;
    var curve=new Bezier4x3(P(V(curb.a)),P(V(curb.b)),P(V(curb.c)),P(V(curb.d)));
    slices.Add(new CurbSlice{Road=index,Side=curb.side,Curb=curve,Outside=curve.a-mouth,HasBack=false,Kind=BoundaryKind.Curb});
   }
   var corners=CornerRule.Calculate(slices,envelope);Check(corners.Count==4,"Actual native curb halves did not form four corners");
   foreach(var c in corners)
   {
    Check(math.abs(math.distance(c.Position.xz,c.CurbPoint.xz)-.9f)<.002f,"Fixed curb setback changed");
    Check(c.Clearance>envelope+.005f,"Pedestrian head envelope reaches the captured road curb");
    Check(!c.Fits&&!c.HasSidewalkBoundary,"Missing outer geometry was falsely certified");proposals++;
   }
   if(pass==0)outcomes.Add(new{scene.node,style=scene.style,mounts=mounts.Select(p=>new[]{p.x,p.y,p.z}).ToArray(),corners=corners.Select(c=>new{roads=new[]{c.RoadA,c.RoadB},point=new[]{c.Position.x,c.Position.y,c.Position.z},curbPoint=new[]{c.CurbPoint.x,c.CurbPoint.y,c.CurbPoint.z},c.Clearance,c.Reason}).ToArray()});
  }
  // Shoulder classification uses lane widths only, with the 0.9 m setback
  // applied once. These are constructed lane-width fixtures, not live highways.
  for(int pass=0;pass<20;pass++)
  {
   Check(CornerRule.DrivingEdges(new[]{new float2(-1.75f,3.5f),new float2(1.75f,3.5f)},13,out var low,out var high)&&math.abs(low+3.5f)<.00001f&&math.abs(high-3.5f)<.00001f,"Shoulder edge includes hidden padding");
   Check(!CornerRule.DrivingEdges(new[]{new float2(0,20)},13,out low,out high),"Impossible lane widths accepted");
  }
  var result=new{checks,acceptedNativeMounts=accepted,realCornerProposals=proposals,scenes=data.scenes.Length,transformsPerScene=20,pedestrianEnvelopeRadius=envelope,outcomes,mountPositions=positions,scope="Compiled released mounting decisions and native logged curb halves from four actual failed nodes, with 20 transforms. Corner target and complete pedestrian envelope tested against captured road curbs. Outer sidewalk and full pedestrian paths absent; not certified as full sidewalk fit. No ECS World, native rendering/physics/save or FPS execution."};
  File.WriteAllText("ActualJunctionReplay232.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(result));
  Console.WriteLine("PASS: "+checks+" assertions; "+accepted+" actual-source mount replays, "+proposals+" real-curb corner targets; pedestrian envelope="+envelope+" m. Outer edge remains explicitly unverified.");
 }
}
