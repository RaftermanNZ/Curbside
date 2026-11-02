using System;using System.Collections.Generic;using System.Reflection;
class ManagedMaintenanceChecks
{
 static int checks;static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
 static void Main()
 {
  var type=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.ManagedWorkIndex`2",true).MakeGenericType(typeof(int),typeof(int));var index=Activator.CreateInstance(type,true);
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  Action<int,int> add=(o,i)=>type.GetMethod("Add",flags).Invoke(index,new object[]{o,i});Action<int> remove=i=>type.GetMethod("Remove",flags).Invoke(index,new object[]{i});
  var output=new List<int>();Action<int> rotate=n=>{output.Clear();type.GetMethod("Rotate",flags).Invoke(index,new object[]{output,n});};
  for(int i=0;i<100000;i++)add(i/10,i);
  rotate(64);Check(output.Count==64&&new HashSet<int>(output).Count==64,"Idle audit must stay bounded for a large registry");
  var all=new HashSet<int>();for(int pass=0;pass<1564;pass++){rotate(64);foreach(var i in output)all.Add(i);}Check(all.Count==100000,"Round-robin must not starve the tail");
  output.Clear();type.GetMethod("CopyOwner",flags).Invoke(index,new object[]{32,output});Check(output.Count==10,"Road edit must target its own visuals");
  add(33,320);output.Clear();type.GetMethod("CopyOwner",flags).Invoke(index,new object[]{32,output});Check(output.Count==9,"Reassigned owner retains stale index");
  remove(320);remove(320);Check((int)type.GetProperty("Count",flags).GetValue(index)==99999,"Idempotent removal changed registry count");
  type.GetMethod("Clear",flags).Invoke(index,null);rotate(64);Check(output.Count==0,"Save/load reset must discard stale IDs");
  add(1,1);add(1,1);rotate(64);Check(output.Count==1,"Duplicate registration created duplicate work");rotate(0);Check(output.Count==0,"Zero budget was ignored");
  type.GetMethod("Clear",flags).Invoke(index,null);
  var model=new Dictionary<int,int>();var random=new Random(147);
  for(int step=0;step<20000;step++){
   int item=random.Next(2000),owner=random.Next(100);
   if(random.Next(3)==0){remove(item);model.Remove(item);}else{add(owner,item);model[item]=owner;}
   if(step%101==0){
    Check((int)type.GetProperty("Count",flags).GetValue(index)==model.Count,"Mutation count differs from independent model");
    for(int o=0;o<100;o++){output.Clear();type.GetMethod("CopyOwner",flags).Invoke(index,new object[]{o,output});var expected=new HashSet<int>();foreach(var pair in model)if(pair.Value==o)expected.Add(pair.Key);Check(expected.SetEquals(output)&&expected.Count==output.Count,"Mutation left stale or duplicate owner IDs");}
    rotate(64);Check(output.Count==Math.Min(64,model.Count)&&new HashSet<int>(output).Count==output.Count,"Mutation broke bounded unique rotation");foreach(var i in output)Check(model.ContainsKey(i),"Rotation retained removed ID");
   }
  }
  var visited=new HashSet<int>();for(int pass=0;pass<40;pass++){rotate(64);foreach(var i in output)visited.Add(i);}Check(visited.Count==model.Count,"Mutation starved surviving IDs");
  Console.WriteLine("PASS: "+checks+" compiled maintenance-index checks, including 100,000 entries, bounded visits, fairness, reassignment, removal and reset. Not an in-game FPS measurement.");
 }
}
