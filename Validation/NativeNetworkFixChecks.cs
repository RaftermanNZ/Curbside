using System;using System.Reflection;
class NativeNetworkFixChecks
{
 static int checks;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Main()
 {
  var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");var flags=BindingFlags.Static|BindingFlags.NonPublic;
  Check(assembly.GetType("MedianGrassReplacement.Systems.NetworkAtlasCache")==null,"Unvalidated GPU atlas compositor remains in candidate");
  var scope=assembly.GetType("MedianGrassReplacement.Systems.NetworkTextureScope",true).GetMethod("SharedParkingAtlas",flags);
  Func<string,int,int,bool> shared=(n,w,h)=>(bool)scope.Invoke(null,new object[]{n,w,h});
  Check(shared("ParkingLane7_BaseColor_native",4096,4096),"Verified shared parking atlas must remain protected");
  foreach(string name in new[]{"RoadEU_BaseColor","RoadParts_BaseColor","TiledRoad_BaseColor","RoadTransition_BaseColor","BikePathPavement800cm_BaseColor",null,""})Check(!shared(name,4096,4096),"Unrelated native atlas must not be blocked: "+name);
  Check(!shared("ParkingLane7_BaseColor_native",2048,2048),"Name alone must not identify a native atlas");
  var bands=assembly.GetType("MedianGrassReplacement.Systems.ParkingAtlasPolicy",true).GetMethod("Region",flags);
  Func<int,int> region=x=>(int)bands.Invoke(null,new object[]{x});
  Check(region(0)==1&&region(703)==1,"Native lowered parking tread must use the road selection");
  Check(region(1114)==2&&region(1541)==2,"Native raised sidewalk must use the sidewalk selection");
  foreach(int x in new[]{-1,704,965,967,993,1050,1113,1542,1880,2000,4095,4096})Check(region(x)==0,"Unverified/curb atlas strip changed: "+x);
  var global=assembly.GetType("MedianGrassReplacement.Systems.GlobalSurfacePolicy",true);
  Func<string,int> kind=n=>(int)global.GetMethod("Kind",flags).Invoke(null,new object[]{n,"BH/NetCompositionMeshLitShader"});
  Check(kind("Batch (Road_EU)")==3&&kind("Batch (Road_NA)")==3,"Established regular road/sidewalk path must remain available");
  Check(kind("Batch (Sidewalk9)")==2&&kind("Batch (Pavement)")==2,"Established sidewalk classification was removed");
  foreach(string name in new[]{"Batch (Grass)","Batch (Curb)","Batch (GravelShoulder)","Batch (RoadMarking)","Batch (Rail)"})Check(kind(name)==0,"Unrelated network material classified: "+name);
  Func<string,int> area=n=>(int)global.GetMethod("AssetKind",flags).Invoke(null,new object[]{n,"BH/AreaDecalShader"});
  Check(area("Area batch (Pavement Surface 01)")==4&&area("Area batch (Pavement Surface 02)")==2,"Exact existing pavement targets changed");
  Check(area("Area batch (Custom Pavement)")==0&&area("Area batch (Pavement Surface 03)")==0,"Extra surface scope expanded");
  var startup=assembly.GetType("GardenCitySignals.NativeSignalStartupPolicy",true).GetMethod("Expired",flags);
  Func<double,double,double,bool> expired=(now,start,progress)=>(bool)startup.Invoke(null,new object[]{now,start,progress});
  Check(!expired(29,0,0)&&!expired(40,0,30),"Active native loading loses startup grace");
  Check(expired(65,0,30)&&expired(120,0,119),"Native loading is not bounded");
  Console.WriteLine("PASS: "+checks+" network texture containment, restored ordinary sidewalk routing and bounded signal startup cases. Native visuals remain unverified.");
 }
}
