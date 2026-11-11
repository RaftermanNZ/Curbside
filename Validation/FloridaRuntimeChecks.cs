using System;using System.Linq;using System.Reflection;
class FloridaRuntimeChecks
{
 static Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");static BindingFlags F=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static;static int count;
 static object Call(string type,string method,params object[] args)=>A.GetType(type,true).GetMethod(method,F).Invoke(null,args);
 static void Check(bool value,string label){count++;if(!value)throw new Exception(label);}
 static void Main(){
  string policy="MedianGrassReplacement.Systems.GardenCityLightPolicy";
  string[] assets={"MGR Garden City Traffic Lights","MGR Span Wire Traffic Lights","MGR Rigid Mast Traffic Lights","MGR Metal Span Wire Traffic Lights","MGR Florida Horizontal Traffic Lights"};
  for(int i=0;i<assets.Length;i++){Check((byte)Call(policy,"Style",assets[i])==i,"Stable persisted style value");Check((bool)Call(policy,"IsAsset",assets[i]),"Tool routes to signal system");}
  Check(!(bool)Call(policy,"IsWireStyle",(byte)4),"Florida has no span-wire variant");Check((bool)Call(policy,"IsRigidStyle",(byte)4),"Florida shares the rigid streetlight/arm family");
  Check(Math.Abs((float)Call(policy,"ArmHeadHeight",(byte)4,2.75f,10.5f)-6.58f)<.00001f,"Reviewed upper fixture height");
  var position=Call(policy,"StreetSignPosition",(byte)4);Check(Math.Abs((float)position.GetType().GetField("x").GetValue(position)-1.2f)<.00001f,"Reviewed sign along position");Check(Math.Abs((float)position.GetType().GetField("y").GetValue(position)-5.77f)<.00001f,"Reviewed sign height");
  var modules=(string[])A.GetType("GardenCitySignals.SignalModuleCatalog",true).GetField("Names",F).GetValue(null);var florida=(string[])A.GetType("GardenCitySignals.FloridaModuleCatalog",true).GetField("Names",F).GetValue(null);var combined=modules.Concat(florida).ToArray();
  Check(modules.Length==65&&florida.Length==4,"Original catalog remains independently complete");
  Check((bool)Call("GardenCitySignals.SignalPackPolicy","ValidManifest","Curbside.NativeSignalPack.v1",214,modules),"Legacy/current original-only manifest accepted");
  Check((bool)Call("GardenCitySignals.SignalPackPolicy","ValidManifest","Curbside.NativeSignalPack.v1",214,combined),"Combined manifest accepted");
  Check(!(bool)Call("GardenCitySignals.SignalPackPolicy","ValidManifest","Curbside.NativeSignalPack.v1",214,combined.Take(68).ToArray()),"Partial Florida manifest rejected");
  var bad=(string[])combined.Clone();bad[68]=bad[67];Check(!(bool)Call("GardenCitySignals.SignalPackPolicy","ValidManifest","Curbside.NativeSignalPack.v1",214,bad),"Duplicate module rejected");
  Check(!(bool)Call("GardenCitySignals.SignalPackPolicy","ValidManifest","Curbside.NativeSignalPack.v1",999,combined),"Future core requirement rejected");
  for(int i=0;i<256;i++)Check((byte)Call("GardenCitySignals.FloridaCatalogPolicy","NormalizeStyle",(byte)i)==(i<=4?i:0),"Actual compiled saved-style normalization");
  var names=(string[])A.GetType("MedianGrassReplacement.Systems.UpgradeAssetsSystem",true).GetField("Names",F).GetValue(null);Check(names.Last()==assets[4]&&names.Count(n=>n==assets[4])==1,"Exactly one appended Florida tool");
  var fields=A.GetType("MedianGrassReplacement.Systems.MGRSignalStyle",true).GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);Check(fields.Length==1&&fields[0].Name=="Value"&&fields[0].FieldType==typeof(byte),"Saved component retains one byte");
  Console.WriteLine("PASS: "+count+" compiled Florida route, catalog, manifest, positioning and saved-style checks. Native UI, phases and save/reload remain unverified.");
 }
}
