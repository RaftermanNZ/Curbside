using System;using System.Reflection;using System.Collections.Generic;
class SignalUpgradeChecks226
{
 static void Main()
 {
  var t=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.SignalUpgradePolicy",true);
  var flags=BindingFlags.Static|BindingFlags.NonPublic;var native=t.GetMethod("NeedsNativeRebuild",flags);var visual=t.GetMethod("NeedsVisualRefresh",flags);
  int checks=0;
  for(int source=0;source<6;source++)for(int target=0;target<6;target++)
  foreach(bool controller in new[]{false,true})foreach(bool upgraded in new[]{false,true})foreach(bool remove in new[]{false,true})
  {
   bool custom=target>0,fromCustom=source>0&&!custom&&!remove;
   bool rebuild=(bool)native.Invoke(null,new object[]{remove,controller});
   if(rebuild!=(remove||!controller))throw new Exception("Native enable/remove path changed");checks++;
   bool refresh=(bool)visual.Invoke(null,new object[]{rebuild,custom,fromCustom,upgraded});
   if(controller&&!remove&&rebuild)throw new Exception("Appearance-only switch requests actor regeneration");checks++;
   if(controller&&!remove&&(custom||fromCustom||!upgraded)&&!refresh)throw new Exception("Appearance-only switch does not queue customization/cleanup");checks++;
  }
  // An appearance/timing refresh must leave the old-actor pool intact, so the
  // native create/delete branch is never entered by this Curbside request.
  var actors=new HashSet<int>{11,12,13,14};var before=new HashSet<int>(actors);
  foreach(int style in new[]{1,2,3,4,5,0,1,1})
  {
   bool rebuild=(bool)native.Invoke(null,new object[]{false,true});
   if(rebuild){actors.Clear();for(int i=0;i<4;i++)actors.Add(100+style*10+i);}
   if(!actors.SetEquals(before))throw new Exception("Existing actor identities changed for a style request");checks++;
  }
  Console.WriteLine("PASS: "+checks+" signal-upgrade decisions. Existing-controller appearance switches skip Curbside's native rebuild request; enable/remove and visual cleanup remain. Native ECS actor reuse and third-party rebuilds require runtime validation.");
 }
}
