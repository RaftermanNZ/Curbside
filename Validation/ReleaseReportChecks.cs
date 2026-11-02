using System;using System.Reflection;using System.Runtime.Serialization;
class ReleaseReportChecks
{
 static int n;static void Check(bool ok,string text){n++;if(!ok)throw new Exception(text);}
 static void Main()
 {
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var t=a.GetType("MedianGrassReplacement.Setting",true);var s=FormatterServices.GetUninitializedObject(t);
  foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.NonPublic))if(f.FieldType.Name=="DecalNumberDraft")f.SetValue(s,Activator.CreateInstance(f.FieldType,true));
  t.GetMethod("SetDefaultValues",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s,new object[]{"C:/Fixture"});Check((bool)t.GetProperty("FollowCityMarkingTheme").GetValue(s),"New installs must follow city theme");
  t.GetProperty("FollowCityMarkingTheme").SetValue(s,false);t.GetProperty("MarkingToolColor").SetValue(s,"White");Check((bool)t.GetProperty("EuropeanMarkings").GetValue(s),"Persisted white manual choice lost");
  t.GetProperty("MarkingToolColor").SetValue(s,"Yellow");Check(!(bool)t.GetProperty("EuropeanMarkings").GetValue(s),"Persisted yellow manual choice lost");
  var city=a.GetType("MedianGrassReplacement.Systems.MarkingThemeSystem",true).GetField("CityEuropean",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(bool eu in new[]{false,true})foreach(string saved in new[]{"White","Yellow"}){
   city.SetValue(null,eu);t.GetProperty("MarkingToolColor").SetValue(s,saved);t.GetProperty("FollowCityMarkingTheme").SetValue(s,true);
   Check((bool)t.GetProperty("EuropeanMarkings").GetValue(s)==eu,"City theme must win over hidden saved tool color when following theme");
  }
  t.GetProperty("FollowCityMarkingTheme").SetValue(s,false);t.GetProperty("MarkingToolColor").SetValue(s,"Yellow");
  Check(!(bool)t.GetProperty("EuropeanMarkings").GetValue(s),"Deliberate existing manual theme override was silently migrated");
  var themePolicy=a.GetType("MedianGrassReplacement.Systems.NativeJunctionColorPolicy",true).GetMethod("ThemedTarget",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(bool opposing in new[]{false,true})foreach(bool edge in new[]{false,true})foreach(bool adapt in new[]{false,true}){
   Check((byte)themePolicy.Invoke(null,new object[]{opposing,edge,true,adapt,true})==1,"EU city-following must request white for every automatic line role");
   Check((byte)themePolicy.Invoke(null,new object[]{opposing,edge,false,adapt,true})==(opposing?2:1),"NA city-following must retain white dividers/outer edges and yellow opposing boundaries");
  }
  t.GetProperty("ThemeAssetEdgeColors").SetValue(s,false);t.GetProperty("FollowCityMarkingTheme").SetValue(s,true);
  var automaticEdges=t.GetProperty("AutomaticThemeEdges",BindingFlags.Instance|BindingFlags.NonPublic);
  Check((bool)automaticEdges.GetValue(s),"Legacy manual edge toggle bypasses city-following");
  Check(!(bool)t.GetProperty("ThemeAssetEdgeColors").GetValue(s),"City-following overwrote stored manual edge preference");
  t.GetProperty("FollowCityMarkingTheme").SetValue(s,false);Check(!(bool)automaticEdges.GetValue(s),"Opting out did not retain manual edge preference");
  var c=a.GetType("MedianGrassReplacement.Systems.CenterlineUpgradePolicy",true).GetMethod("YellowPaint",BindingFlags.Static|BindingFlags.NonPublic);
  Check(!(bool)c.Invoke(null,new object[]{(byte)4,true}),"Explicit white center must override yellow theme");Check((bool)c.Invoke(null,new object[]{(byte)6,false}),"Explicit yellow center must override white theme");
  var surface=a.GetType("MedianGrassReplacement.Systems.GlobalSurfacePolicy",true).GetMethod("Kind",BindingFlags.Static|BindingFlags.NonPublic);
  Check((int)surface.Invoke(null,new object[]{"Batch (ParkingRoad_01)","BH/NetCompositionMeshLitShader"})==3,"Parking-road composition was omitted from role detection");
  Check((int)surface.Invoke(null,new object[]{"Batch (ParkingRoad_Grass)","BH/NetCompositionMeshLitShader"})==0,"Grass material classified as pavement");
  Check((int)surface.Invoke(null,new object[]{"Batch (ParkingRoad_01)","UnrelatedShader"})==0,"Unrelated shader classified as pavement");
  foreach(var name in new[]{"MGRParkingSides","MGRCenterlineStyle","MGREdgePaint","MGRLaneDivider","MGRCrosswalkChoice","MGRHighwayLineIntent","MGRParkingHatching","MGRCurbDecalSides"})
  {var k=a.GetType("MedianGrassReplacement.Systems."+name,true);Check(Array.Exists(k.GetInterfaces(),i=>i.FullName=="Colossal.Serialization.Entities.ISerializable"),name+" missing serialization interface");Check(k.GetMethod("Serialize")!=null&&k.GetMethod("Deserialize")!=null,name+" missing writer/reader");}
  Console.WriteLine("PASS: "+n+" released-report checks: new defaults, retained choices, explicit color precedence, parking role guards and intent serializer contracts. Native save round trips unverified.");
 }
}
