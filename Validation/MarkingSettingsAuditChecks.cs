using System;using System.Collections.Generic;using System.Reflection;using System.Runtime.Serialization;
class MarkingSettingsAuditChecks
{
 static int n;static void Check(bool ok,string why){n++;if(!ok)throw new Exception(why);}
 static void Main(){var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var t=a.GetType("MedianGrassReplacement.Setting",true);var s=FormatterServices.GetUninitializedObject(t);foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.NonPublic))if(f.FieldType.Name=="DecalNumberDraft")f.SetValue(s,Activator.CreateInstance(f.FieldType,true));t.GetMethod("SetDefaultValues",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{"C:/Fixture"});
 Func<string,object> get=k=>t.GetProperty(k).GetValue(s);Action<string,object> set=(k,v)=>t.GetProperty(k).SetValue(s,v);
 Check((string)get("AutomaticMarkingTheme")=="City","Default must follow map theme");
 foreach(var mode in new[]{"EU","NA","City"}){set("AutomaticMarkingTheme",mode);Check((string)get("AutomaticMarkingTheme")==mode,"Theme choice failed");Check((bool)get("FollowCityMarkingTheme")== (mode=="City"),"Theme checkbox backing state disagrees");}
 set("CenterLineStyle","Yellow Double Line");set("CurbLineStyle","Yellow Double Line");set("MedianLineStyle","Yellow Double Line");
 set("DisableAutomaticMarkings",true);Check((bool)get("DisableAutomaticMarkings"),"Vanilla master did not enable");
 Check((string)get("CenterLineStyle")=="Yellow Double Line","Master erased legacy setting instead of retaining it");set("DisableAutomaticMarkings",false);
 set("PreserveVanillaJunctionLines",true);Check((bool)get("DisableAutomaticMarkings"),"Retired preserve choice failed to retain native paint");set("UseVanillaAutomaticMarkings",false);Check(!(bool)get("PreserveVanillaJunctionLines"),"Main control cannot leave retired mode");
 foreach(string name in new[]{"CenterLineStyle","CurbLineStyle","MedianLineStyle","PreserveVanillaJunctionLines","FollowCityMarkingTheme","EuropeanMarkings","ThemeAssetEdgeColors"})Check(Array.Exists(t.GetProperty(name).GetCustomAttributesData().ToArrayCompat(),v=>v.AttributeType.Name=="SettingsUIHiddenAttribute"),"Redundant control remains visible: "+name);
 var qtype=a.GetType("MedianGrassReplacement.Systems.ManagedWorkQueue`1",true).MakeGenericType(typeof(int));var q=Activator.CreateInstance(qtype,true);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var add=qtype.GetMethod("Add",flags);var take=qtype.GetMethod("Take",flags);
 for(int i=0;i<100000;i++)add.Invoke(q,new object[]{i});for(int i=0;i<100000;i++){add.Invoke(q,new object[]{0});var args=new object[]{0};Check((bool)take.Invoke(q,args)&& (int)args[0]==i,"Repeated first-road update starved queued road");}Check((int)qtype.GetProperty("Count",flags).GetValue(q)==1,"Repeated queued item was duplicated");qtype.GetMethod("Clear",flags).Invoke(q,null);Check((int)qtype.GetProperty("Count",flags).GetValue(q)==0,"Load reset retained queue");
 Console.WriteLine("PASS: "+n+" settings/theme/native-mode and FIFO starvation checks. Runtime appearance and FPS unverified.");}
}
static class Extensions{public static CustomAttributeData[] ToArrayCompat(this IList<CustomAttributeData> source){var r=new CustomAttributeData[source.Count];source.CopyTo(r,0);return r;}}
