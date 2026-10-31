using System;using System.Reflection;using System.Collections.Generic;
class SignalBatchPathsChecks
{
 static void Main(){
  var type=Assembly.LoadFrom("GardenCitySignals.dll").GetType("GardenCitySignals.PackSystem",true);
  var paths=(string[])type.GetMethod("SourceImportPaths",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
  var unique=new HashSet<string>(paths,StringComparer.Ordinal);
  if(paths.Length!=53||unique.Count!=53)throw new Exception("Incomplete or duplicated module batch");
  foreach(string p in paths)if(!p.StartsWith("ProjectFiles/CSG",StringComparison.Ordinal)||p.Contains(".."))throw new Exception("Invalid module source path");
  foreach(string p in new[]{"ProjectFiles/CSGHeadBlack","ProjectFiles/CSGHeadYellow","ProjectFiles/CSGHeadWire","ProjectFiles/CSGPedHead","ProjectFiles/CSGWirePole","ProjectFiles/CSGLampArm","ProjectFiles/CSGSignPlate","ProjectFiles/CSGGardenArm19","ProjectFiles/CSGRigidArm19","ProjectFiles/CSGWireSegment08"})
   if(!unique.Contains(p))throw new Exception("Missing model category or maximum reach: "+p);
  Console.WriteLine("53 unique source paths verified, including all styles, pedestrian/sign/lamp modules and maximum arm/wire reaches.");
 }
}
