using System;using System.Reflection;
class SignalLaneBindingChecks227
{
 static void Main()
 {
  var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");var flags=BindingFlags.Static|BindingFlags.NonPublic;
  var method=assembly.GetType("MedianGrassReplacement.Systems.SignalLaneBindingPolicy",true).GetMethod("Physical",flags);
  int count=0;
  foreach(bool master in new[]{false,true})foreach(bool secondary in new[]{false,true})
  {
   bool actual=(bool)method.Invoke(null,new object[]{master,secondary});
   if(actual!=(!master&&!secondary))throw new Exception("Aggregate/secondary source admitted or physical source rejected");count++;
  }
  Console.WriteLine("PASS: "+count+" compiled lane-eligibility decisions. Production binding-loop replay is separate; no lane timing writes or native ECS execution.");
 }
}
