using System;using System.Reflection;
class DeferredSettingsChecks
{
 static int checks;static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
 static void Main()
 {
  var t=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.DeferredSettingsWrite`1",true).MakeGenericType(typeof(string));var q=Activator.CreateInstance(t,true);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  Func<string,object[],object> call=(n,a)=>t.GetMethod(n,flags).Invoke(q,a);Func<string,object> get=n=>t.GetProperty(n,flags).GetValue(q);
  call("Force",new object[]{0d});call("Request",new object[]{"first",0d});Check(call("Begin",new object[]{.5d})==null,"Empty flush bypassed a later debounce");
  Check((string)call("Begin",new object[]{1d})=="first","Initial write not started");Check(call("Begin",new object[]{2d})==null,"Concurrent write allowed");
  call("Complete",new object[]{false,1d});Check((string)get("Pending")=="first","Failed setting discarded");Check(call("Begin",new object[]{2d})==null,"Retry ignored backoff");
  Check((string)call("Begin",new object[]{3d})=="first","First retry missing");call("Complete",new object[]{false,3d});call("Begin",new object[]{7d});call("Complete",new object[]{false,7d});Check((int)get("Failures")==3,"Retry counter incorrect");Check(call("Begin",new object[]{100d})==null,"Failed writer retried forever");
  call("Request",new object[]{"new",101d});call("Begin",new object[]{102d});call("Request",new object[]{"newest",102.2d});call("Complete",new object[]{false,102.5d});Check((string)get("Pending")=="newest","Older failed write replaced newer preferences");
  call("Force",new object[]{102.5d});Check((string)call("Begin",new object[]{102.5d})=="newest","Explicit save did not flush pending settings");call("Complete",new object[]{true,103d});Check(get("Pending")==null&&!(bool)get("Saving"),"Successful write left dirty state");
  Console.WriteLine("PASS: "+checks+" compiled settings-write state checks: debounce, bounded retry, newest-value precedence and explicit flush. Native storage/shutdown still require runtime tests.");
 }
}
