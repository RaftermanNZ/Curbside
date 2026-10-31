using System;using System.Reflection;
class Test31VisualChecks
{
 static int checks;static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;}
 static void Main(){
 var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var policy=a.GetType("MedianGrassReplacement.Systems.CrosswalkVisualPolicy");var m=policy.GetMethod("WithoutPaint",BindingFlags.Static|BindingFlags.NonPublic);var t=m.GetParameters()[0].ParameterType;
 foreach(int lod in new[]{0,4,255})foreach(int layer in new[]{1,3,255,65535}){
  object input=Activator.CreateInstance(t);var size=t.GetField("m_Size");object extent=Activator.CreateInstance(size.FieldType,new object[]{7f,2f,9f});size.SetValue(input,extent);
  t.GetField("m_MinLod").SetValue(input,lod);
  foreach(string f in new[]{"m_GameLayers","m_EditorLayers"})t.GetField(f).SetValue(input,Enum.ToObject(t.GetField(f).FieldType,layer));
  object output=m.Invoke(null,new[]{input});
  Check(size.GetValue(output).Equals(extent),"Preserve native crossing dimensions");Check((int)t.GetField("m_MinLod").GetValue(output)==lod,"Preserve native LOD");
  foreach(string f in new[]{"m_GameLayers","m_EditorLayers"}){Check(Convert.ToInt64(t.GetField(f).GetValue(output))==0,"Suppress only rendering layers");Check(Convert.ToInt64(t.GetField(f).GetValue(input))==layer,"Do not modify shared original geometry");}
 }
 Console.WriteLine(checks+" compiled render-only crosswalk policy checks passed.");
 }
}
