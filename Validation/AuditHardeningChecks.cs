using System;using System.Linq;using System.Reflection;using System.Threading.Tasks;using Game.Net;
class AuditHardeningChecks
{
 static int count;static readonly Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");
 static MethodInfo Method(string type,string name)=>A.GetType(type,true).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
 static void Check(bool b,string text){count++;if(!b)throw new Exception(text);}
 static void Main()
 {
  var recover=Method("MedianGrassReplacement.Systems.SaveRecoveryPolicy","CanRecover");
  for(int bits=0;bits<16;bits++)Check((bool)recover.Invoke(null,new object[]{(bits&1)!=0,(bits&2)!=0,(bits&4)!=0,(bits&8)!=0})==(bits==15),"Recovered active/unobserved/other-world save");
  var task=new TaskCompletionSource<bool>();
  Check(!(bool)recover.Invoke(null,new object[]{true,true,task.Task.IsCompleted,true}),"Incomplete queue released guard");task.SetResult(true);
  Check((bool)recover.Invoke(null,new object[]{true,true,task.Task.IsCompleted,true}),"Completed exceptional queue cannot recover");
  var length=Method("GardenCitySignals.SignalPackPolicy","ValidManifestLength");
  foreach(long bytes in new long[]{1,65536,553081,2097152})Check((bool)length.Invoke(null,new object[]{bytes}),"Valid native file-catalog manifest rejected");
  foreach(long bytes in new long[]{-1,0,2097153,long.MaxValue})Check(!(bool)length.Invoke(null,new object[]{bytes}),"Invalid native manifest length accepted");
  var catalog=(string[])A.GetType("GardenCitySignals.SignalModuleCatalog",true).GetField("Names",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  Check(catalog.Length==61&&catalog.Distinct().Count()==61,"Module catalog incomplete");
  var fixturePath=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Prepared-Pack-Test1-native-pack.json");
  var fixtureText=System.IO.File.ReadAllText(fixturePath);
  var fixture=(System.Collections.Generic.Dictionary<string,object>)new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=2097152}.DeserializeObject(fixtureText);
  Check((bool)length.Invoke(null,new object[]{new System.IO.FileInfo(fixturePath).Length}),"Installed native manifest length regression");
  var fixtureModules=((object[])fixture["modules"]).Select(x=>(string)x).ToArray();
  var pack=Method("GardenCitySignals.SignalPackPolicy","ValidManifest");
  Func<string,int,string[],bool> valid=(format,min,names)=>(bool)pack.Invoke(null,new object[]{format,min,names});
  Check(valid((string)fixture["format"],(int)fixture["minimumCurbsideBuild"],fixtureModules),"Actual installed native manifest header/catalog rejected");
  Check(valid("Curbside.NativeSignalPack.v1",103,catalog),"Existing prepared pack rejected");
  Check(valid("Curbside.NativeSignalPack.v1",103,catalog.Reverse().ToArray()),"Catalog order incorrectly constrained");
  Check(!valid("foreign",103,catalog),"Foreign format accepted");Check(!valid("Curbside.NativeSignalPack.v1",int.MaxValue,catalog),"Future build accepted");
  Check(!valid("Curbside.NativeSignalPack.v1",0,catalog),"Missing minimum accepted");Check(!valid("Curbside.NativeSignalPack.v1",103,null),"Missing catalog accepted");
  for(int i=0;i<catalog.Length;i++){
   Check(!valid("Curbside.NativeSignalPack.v1",103,catalog.Where((x,n)=>n!=i).ToArray()),"Incomplete pack accepted");
   var duplicate=(string[])catalog.Clone();duplicate[i]=catalog[(i+1)%catalog.Length];Check(!valid("Curbside.NativeSignalPack.v1",103,duplicate),"Duplicate catalog accepted");
  }
  var owns=Method("MedianGrassReplacement.Systems.ApproachStopPolicy","OwnsPriority");
  Func<int,CarLaneFlags> flags=n=>((n&1)!=0?CarLaneFlags.Stop:0)|((n&2)!=0?CarLaneFlags.Yield:0)|((n&4)!=0?CarLaneFlags.RightOfWay:0);
  for(int a=0;a<8;a++)for(int b=0;b<8;b++)Check((bool)owns.Invoke(null,new object[]{flags(a)|CarLaneFlags.TurnLeft|CarLaneFlags.Highway,flags(b)})==(a==b),"Restoration ignored later external priority or compared unrelated flags");
  var paths=(string[])Method("GardenCitySignals.PackSystem","SourceImportPaths").Invoke(null,null);
  Check(paths.Length==61&&paths.Distinct().Count()==61&&paths.All(p=>p.StartsWith("ProjectFiles/CSG",StringComparison.Ordinal)&&!p.Contains("..")),"Integrated import catalog drifted");
  var header=Method("MedianGrassReplacement.Systems.FileTextureLoader","PngHeaderFits");
  byte[] png={137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,16,0,0,0,16,0};
  Check((bool)header.Invoke(null,new object[]{png,4096}),"4K boundary rejected");png[18]=32;
  Check(!(bool)header.Invoke(null,new object[]{png,4096}),"8K accepted at4K limit");Check(!(bool)header.Invoke(null,new object[]{new byte[23],4096}),"Truncated header accepted");
  Console.WriteLine("PASS: "+count+" hardening checks: queue completion, invalid/missing packs, external priority ownership, integrated source paths and PNG bounds. Native failure recovery still needs live validation.");
 }
}
