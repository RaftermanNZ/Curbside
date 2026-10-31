using System;using System.Linq;using GardenCitySignals;
class SignalModuleCatalogChecks {
 static void Main(){
  var names=SignalModuleCatalog.Names;
  if(names.Length!=61||names.Distinct(StringComparer.Ordinal).Count()!=61)throw new Exception("Expected61 unique active models");
  if(names.Any(n=>n.StartsWith("CSGGardenArm",StringComparison.Ordinal)||n.StartsWith("CSGRigidArm",StringComparison.Ordinal)))throw new Exception("Legacy arm still required");
  foreach(string family in new[]{"Garden","Rigid"})for(int i=1;i<=19;i++)if(!names.Contains("CSGModular"+family+"Arm"+i.ToString("00")))throw new Exception("Missing active reach");
  for(int i=1;i<=8;i++)if(!names.Contains("CSGWireSegment"+i.ToString("00")))throw new Exception("Missing fallback cable");
  foreach(string n in new[]{"CSGPedHead","CSGWireTimber","CSGModularGardenPole","CSGModularRigidPole","CSGGardenLuminaire","CSGRigidLuminaire","CSGWireLuminaire"})if(!names.Contains(n))throw new Exception("Missing model category");
  Console.WriteLine("PASS:61 unique active models; all reaches and categories retained;38 unused legacy arms excluded.");
 }
}
