using System;using System.Collections;using System.Reflection;using System.Runtime.Serialization;using Unity.Entities;
class Test85Readiness
{
 static BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Main()
 {
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var t=a.GetType("MedianGrassReplacement.Systems.FarSideLightSystem");var moved=t.GetNestedType("Moved",BindingFlags.NonPublic);
  var system=FormatterServices.GetUninitializedObject(t);
  var pf=t.GetField("_plannedSignals",fields);var mf=t.GetField("_moved",fields);
  var plan=(IDictionary)Activator.CreateInstance(pf.FieldType);var applied=(IDictionary)Activator.CreateInstance(mf.FieldType);pf.SetValue(system,plan);mf.SetValue(system,applied);
  var node=new Entity{Index=100,Version=1};var native=new Entity{Index=200,Version=1};var custom=new Entity{Index=201,Version=1};
  var method=t.GetMethod("ReadyCrossingSupports",fields);
  for(int planned=0;planned<=12;planned++)for(int ready=0;ready<=planned;ready++)
  {
   plan.Clear();applied.Clear();
   for(int i=0;i<planned;i++)
   {
    var actor=new Entity{Index=58942+i,Version=17};plan.Add(actor,Activator.CreateInstance(typeof(Game.Objects.Transform)));
    if(i>=ready)continue;object state=Activator.CreateInstance(moved);moved.GetField("Node",fields).SetValue(state,node);moved.GetField("Prefab",fields).SetValue(state,native);moved.GetField("AppliedPrefab",fields).SetValue(state,custom);applied.Add(actor,state);
   }
   var supports=(IList)method.Invoke(system,new object[]{node});Check(supports.Count==ready,"pending support included");
   applied.Clear();Check(supports.Count==ready,"snapshot depends on mutable readiness dictionary");
  }
  plan.Clear();applied.Clear();var key=new Entity{Index=58942,Version=17};plan.Add(key,new Game.Objects.Transform());
  foreach(bool wrongNode in new[]{false,true})foreach(bool changed in new[]{false,true})
  {
   applied.Clear();object state=Activator.CreateInstance(moved);moved.GetField("Node",fields).SetValue(state,wrongNode?new Entity{Index=999,Version=1}:node);moved.GetField("Prefab",fields).SetValue(state,native);moved.GetField("AppliedPrefab",fields).SetValue(state,changed?custom:native);applied.Add(key,state);
   Check(((IList)method.Invoke(system,new object[]{node})).Count==(!wrongNode&&changed?1:0),"foreign node or unchanged native support accepted");
  }
  Console.WriteLine("PASS Test85: "+checks+" production crossing-readiness checks, empty/partial/full 0-12 supports, missing Entity(58942:17), stale snapshots, foreign nodes and unchanged native prefabs.");
 }
}
