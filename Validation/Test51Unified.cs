using System;using System.Reflection;using Game.Tools;using Unity.Entities;using Unity.Mathematics;using Colossal.Mathematics;
class Test52Unified
{
 static int n;static Assembly a;static BindingFlags stat=BindingFlags.NonPublic|BindingFlags.Static,inst=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);n++;}
 static Type T(string name)=>a.GetType("MedianGrassReplacement.Systems."+name);
 static void Main()
 {
  a=Assembly.LoadFrom("MedianGrassReplacement.dll");
  var split=T("NodeSplitPolicy").GetMethod("IsSplitNode",stat);var road=new Entity{Index=12,Version=3};var other=new Entity{Index=13,Version=3};
  foreach(var original in new[]{Entity.Null,road,other})foreach(var flags in new[]{TempFlags.Create,TempFlags.Replace,TempFlags.Replace|TempFlags.Delete,TempFlags.Replace|TempFlags.Cancel,TempFlags.Modify})
   Check((bool)split.Invoke(null,new object[]{original,road,flags})==(original==road&&flags==TempFlags.Replace),"native edge replacement accepted; unrelated/new/deleted preview nodes rejected");
  var median=T("CenterlineUpgradePolicy").GetMethod("MedianCenter",stat);
  foreach(float curb in new[]{-3f,0f,3f})foreach(bool left in new[]{false,true})foreach(byte style in new byte[]{1,2})foreach(float inset in new[]{.15f,.35f,.8f})
  {
   float x=(float)median.Invoke(null,new object[]{curb,left,style,inset,.15f});
   float nearest=x+(left?1:-1)*(style==2?.15f:0);
   Check(math.abs(math.abs(nearest-curb)-inset)<.0001f,"single/double median replacement keeps nearest stripe at selected pavement offset");
   Check(left?x<curb:x>curb,"median paint remains on pavement side");
  }
  var markType=T("CurbLineEmissionSystem").GetNestedType("JoinCap",BindingFlags.NonPublic);
  var factory=T("CurbLineEmissionSystem").GetMethod("HighwayJoinMark",stat);
  foreach(int kind in new[]{0,2})
  {
   var cap=Activator.CreateInstance(markType);markType.GetField("Kind",inst).SetValue(cap,kind);
   var mark=factory.Invoke(null,new object[]{new Bezier4x3(),cap});var mt=mark.GetType();
   Check((bool)mt.GetField("Median",inst).GetValue(mark)==(kind==2),"white highway shoulders are never tagged as yellow median paint");
   Check((bool)mt.GetField("Curb",inst).GetValue(mark)==(kind==0),"white highway joins keep the edge role used by native-paint coverage");
  }
  var settingType=a.GetType("MedianGrassReplacement.Setting");var setting=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(settingType);
  var city=T("MarkingThemeSystem").GetField("CityEuropean",stat);
  foreach(bool european in new[]{false,true})foreach(bool follow in new[]{false,true})foreach(bool white in new[]{false,true})
  {
   city.SetValue(null,european);settingType.GetProperty("FollowCityMarkingTheme").SetValue(setting,follow);settingType.GetProperty("MarkingToolColor").SetValue(setting,white?"White":"Yellow");
   bool expected=follow?european:white;
   Check((bool)settingType.GetProperty("EuropeanMarkings").GetValue(setting)==expected,"native city theme and manual override resolve consistently");
   Check((bool)settingType.GetProperty("YellowToolPaint").GetValue(setting)==!expected,"explicit centerline and preview use resolved theme");
  }
  city.SetValue(null,false);
  Console.WriteLine(n+" native split, avenue stripe clearance, highway role and theme-resolution regressions passed.");
 }
}
