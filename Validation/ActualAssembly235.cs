using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;using System.Runtime.Serialization;using System.Web.Script.Serialization;
using Unity.Mathematics;using Colossal.Mathematics;using Game.Prefabs;
class Layout234 {public string node;public byte style;public int[] sources;public float[][] supports;public int[][] pairs;}
class Layouts234 {public Layout234[] layouts;}
class Bounds234 {public float[] min,max;}
class Catalog234 {public Dictionary<string,Bounds234> meshes;}
class ActualAssembly235
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static float3 V(float[] v)=>new float3(v[0],v[1],v[2]);static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Main()
 {
  var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var layouts=json.Deserialize<Layouts234>(File.ReadAllText("Captured-Corners235.json"));var capture=json.Deserialize<Capture234>(File.ReadAllText("CapturedCorners233.json"));var catalog=json.Deserialize<Catalog234>(File.ReadAllText("SignalLayoutCatalog225.json"));
  var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");var garden=asm.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);var standing=asm.GetType("MedianGrassReplacement.Systems.WireStandingGeometry",true);
  var pack=asm.GetType("GardenCitySignals.SignalPackPolicy",true);pack.GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
  var modules=(IDictionary)garden.GetField("modules",S).GetValue(null);var renders=new List<KeyValuePair<RenderPrefab,string>>();
  foreach(var alias in new[]{"WirePole","WireTimber","MetalWirePole","HeadWire","LowerHeadWire","SignPlate","PedHeadWire"})
  {
   string name="CSG"+alias+" Mesh";var b=catalog.meshes[name];var render=(RenderPrefab)FormatterServices.GetUninitializedObject(typeof(RenderPrefab));render.bounds=new Bounds3(V(b.min),V(b.max));
   var model=(StaticObjectPrefab)FormatterServices.GetUninitializedObject(typeof(StaticObjectPrefab));model.m_Meshes=new[]{new ObjectMeshInfo{m_Mesh=render}};modules[alias]=model;renders.Add(new KeyValuePair<RenderPrefab,string>(render,name));
  }
  var faceType=garden.GetNestedType("CrossingFace",BindingFlags.NonPublic);var output=new List<object>();
  foreach(var layout in layouts.layouts)
  {
   var scene=capture.nodes.Single(n=>n.node==layout.node);var supports=layout.supports.Select(V).ToArray();
   var supportsType=asm.GetType("MedianGrassReplacement.Systems.CornerSupportList",true);var nativeSupports=(IList<float3>)Activator.CreateInstance(supportsType);foreach(var p in supports)nativeSupports.Add(p);supportsType.GetField("UsesCornerRule",F).SetValue(nativeSupports,true);
   for(int slot=0;slot<layout.sources.Length;slot++)
   {
    int source=layout.sources[slot];var road=scene.roads[source];var position=supports[slot];
    var la=new object[]{road.lanes.Select(V).ToList(),new float2(road.outward[0],road.outward[1]),position,null,null};Check((bool)garden.GetMethod("TryLayout",S).Invoke(null,la),"Rebuilt assembly lane fit failed");
    var faces=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(faceType));
    // Every cardinal face is tested separately below. The layout uses the
    // full pedestrian envelope, so the result does not assume a safe facing.
    foreach(var facing in new[]{new float3(1,0,0),new float3(-1,0,0),new float3(0,0,1),new float3(0,0,-1)})
    {
     faces.Clear();var face=Activator.CreateInstance(faceType);faceType.GetField("Facing",F).SetValue(face,facing);faceType.GetField("Mask",F).SetValue(face,(ushort)1);faces.Add(face);
     var args=new object[]{position,new float2(road.outward[0],road.outward[1]),la[3],la[4],layout.style,nativeSupports,-1,true,true,faces,null,null,null};
     Check((bool)garden.GetMethod("ComposeWire",S).Invoke(null,args),"Compiled wire assembly rejected completed plan");
     var meshes=(List<ObjectMeshInfo>)args[10];var rotation=(quaternion)args[11];var bounds=new Bounds3(new float3(float.MaxValue),new float3(float.MinValue));var poles=new List<Bounds3>();var parts=new List<object>();
     foreach(var part in meshes)
     {
      var render=(RenderPrefab)part.m_Mesh;string name=renders.Single(r=>Object.ReferenceEquals(r.Key,render)).Value;
      var local=Game.Objects.ObjectUtils.CalculateBounds(part.m_Position,part.m_Rotation,render.bounds);bounds.min=math.min(bounds.min,local.min);bounds.max=math.max(bounds.max,local.max);
      bool pole=name.Contains("WirePole")||name.Contains("WireTimber");if(pole)poles.Add(local);
      parts.Add(new{mesh=name,pole,position=new[]{part.m_Position.x,part.m_Position.y,part.m_Position.z},rotation=new[]{part.m_Rotation.value.x,part.m_Rotation.value.y,part.m_Rotation.value.z,part.m_Rotation.value.w}});
     }
     var footprint=new object[]{poles,null};Check((bool)standing.GetMethod("Footprint",S).Invoke(null,footprint),"Missing standing footprint");var legs=(float3)footprint[1];
     Check(legs.x>.4f&&legs.x<.81f&&legs.y==1.7f,"Native pole leg not corrected from authored parts");
     output.Add(new{layout.node,source,layout.style,position=new[]{position.x,position.y,position.z},rotation=new[]{rotation.value.x,rotation.value.y,rotation.value.z,rotation.value.w},boundsMin=new[]{bounds.min.x,bounds.min.y,bounds.min.z},boundsMax=new[]{bounds.max.x,bounds.max.y,bounds.max.z},size=new[]{Game.Objects.ObjectUtils.GetSize(bounds).x,Game.Objects.ObjectUtils.GetSize(bounds).y,Game.Objects.ObjectUtils.GetSize(bounds).z},legSize=new[]{legs.x,legs.y,legs.z},parts});
    }
   }
  }
  File.WriteAllText("Rebuilt-Assemblies235.json",json.Serialize(new{checks,scope="Actual compiled ComposeWire and standing-footprint policy, native module bounds and captured lane inputs. Managed uninitialized model holders avoid Unity object creation. Four pedestrian-facing directions per actor; native World initialization/culling not executed.",assemblies=output}));Console.WriteLine("PASS: "+checks+" checks; "+output.Count+" compiled rebuilt assemblies with truthful full render bounds and corrected standing legs.");
 }
}
