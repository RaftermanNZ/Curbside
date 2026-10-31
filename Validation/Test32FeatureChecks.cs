using System;using System.Reflection;using System.Collections;using Game.Prefabs;using Game.Rendering;using Unity.Mathematics;
class DummyMod:Game.Modding.IMod{public void OnLoad(Game.UpdateSystem update){}public void OnDispose(){}}
class Test32FeatureChecks
{
 static int n;static void Check(bool value,string reason){if(!value)throw new Exception(reason);n++;}
 static void Main()
 {
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var flags=BindingFlags.Static|BindingFlags.NonPublic;
  var height=a.GetType("MedianGrassReplacement.Systems.PoleHeightPolicy");var requested=height.GetMethod("Requested",flags);var scale=height.GetMethod("Scale",flags);var definition=height.GetMethod("Definition",flags);
  foreach(float h in new[]{5f,6.5f,8f,12f,20f,50f,100f})
  {
   float factor=(float)scale.Invoke(null,new object[]{h,8f});Check(math.abs(factor-h/8)<.0001f,"requested pole height must continuously scale native mesh");
   var def=(StackData)definition.Invoke(null,new object[]{0f,8f});var stack=new Game.Objects.Stack{m_Range=new Colossal.Mathematics.Bounds1(0,h)};
   BatchDataHelpers.CalculateStackSubMeshData(stack,def,out var counts,out var offsets,out var scales);
   float3 position=default,meshScale=new float3(1);BatchDataHelpers.CalculateStackSubMeshData(def,offsets,scales,0,SubMeshFlags.IsStackStart,ref position,ref meshScale);
   Check(counts.x==1&&counts.y==0&&counts.z==0,"one pole mesh; no tiled copies");Check(math.abs(position.y)<.0001f,"pole base remains grounded");Check(math.abs(meshScale.y*8-h)<.0001f,"native renderer scale reaches desired height");Check(meshScale.x==1&&meshScale.z==1,"pole width retained");
  }
  foreach(float invalid in new[]{float.NaN,float.NegativeInfinity,float.PositiveInfinity})Check((float)requested.Invoke(null,new object[]{invalid})==8,"non-finite heights safely default");
  Check((float)requested.Invoke(null,new object[]{-4f})==5&&(float)requested.Invoke(null,new object[]{101f})==100,"height supported bounds");
  var lights=a.GetType("MedianGrassReplacement.Systems.StreetLightingPolicy");var mask=lights.GetMethod("Flags",flags);var eligible=lights.GetMethod("Eligible",flags);
  foreach(uint bits in new uint[]{0,0xffffffff,0x400,0x8000,0x1000000,0x4000000})foreach(bool add in new[]{false,true})
  {uint after=(uint)(CompositionFlags.General)mask.Invoke(null,new object[]{(CompositionFlags.General)bits,add});Check((after&~0x10000000u)==(bits&~0x10000000u),"only street-lighting bit may change; traffic/stop/crosswalk flags preserved");Check(((after&0x10000000u)!=0)==add,"requested lighting mode");}
  foreach(bool street in new[]{false,true})foreach(bool traffic in new[]{false,true})Check((bool)eligible.Invoke(null,new object[]{street,traffic})==(street&&!traffic),"only standalone street lamps are eligible");
  var policy=a.GetType("MedianGrassReplacement.Systems.CenterlineUpgradePolicy");var boundary=policy.GetMethod("TryBoundary",flags);var trafficType=a.GetType("MedianGrassReplacement.Systems.NorthAmericanLinePolicy+Traffic");
  foreach(float lw in new[]{3f,4f})foreach(float rw in new[]{3f,4f})foreach(float shift in new[]{-3f,0f,4f})foreach(bool reverse in new[]{false,true})
  {
   var list=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(trafficType));
   list.Add(Activator.CreateInstance(trafficType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{shift-lw*.5f,lw,0f,reverse},null));list.Add(Activator.CreateInstance(trafficType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{shift+rw*.5f,rw,0f,!reverse},null));
   object[] args={list,0f,0f};Check((bool)boundary.Invoke(null,args),"3m/4m asymmetric opposing boundary supported");Check(math.abs((float)args[1]-shift)<.0001f,"center follows opposing lane edges rather than road midpoint");
  }
  var panel=a.GetType("MedianGrassReplacement.Systems.CurbsidePanelSystem");var entries=(Array)panel.GetField("Entries",flags).GetValue(null);var known=a.GetType("MedianGrassReplacement.Systems.UpgradePreviewPolicy").GetMethod("Known",flags);
  foreach(var entry in entries){string asset=(string)entry.GetType().GetField("Asset",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry);Check((bool)known.Invoke(null,new object[]{asset}),"all upgrade tools have preview coverage");}
  var setting=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(a.GetType("MedianGrassReplacement.Setting"));foreach(var field in setting.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic))if(field.FieldType.Name=="DecalNumberDraft")field.SetValue(setting,Activator.CreateInstance(field.FieldType,true));setting.GetType().GetMethod("SetDecalDefaults",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(setting,null);
  foreach(var expected in new[]{new object[]{"CurbDecalWidth",13.25f},new object[]{"CurbDecalLength",2.6f},new object[]{"CurbDecalSetback",.25f},new object[]{"CurbDecalOverlap",50f},new object[]{"CurbDecalRotation",-90f},new object[]{"CurbDecalOffset",-.95f}})
   Check(math.abs((float)setting.GetType().GetProperty((string)expected[0]).GetValue(setting)-(float)expected[1])<.0001f,"packaged decal placement default: "+expected[0]);
  Check((bool)setting.GetType().GetProperty("UnlimitedDecals").GetValue(setting),"unlimited decals follows user-captured Test52 defaults");
  Console.WriteLine(n+" native pole-scale, street-light isolation, centerline geometry, preview coverage and decal default checks passed.");
 }
}
