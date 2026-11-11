using System;using System.Collections.Generic;using MedianGrassReplacement.Systems;
class MaintenanceIndex235
{
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Main(){var index=new SignalMaintenanceIndex<int>();var work=new List<SignalMaintenanceIndex<int>.Entry>();
 for(int i=0;i<10000;i++)index.Track(i,i/8,SignalMaintenanceIndex<int>.Moved);
 index.CopyNode(17,work);Check(work.Count==8,"Local edit walked the city");foreach(var e in work)Check(e.Node==17,"Foreign owner in node work");
 var seen=new HashSet<int>();for(int pass=0;pass<157;pass++){index.TakeRecovery(64,work);Check(work.Count==64,"Recovery budget changed");foreach(var e in work)seen.Add(e.Actor);}Check(seen.Count==10000,"Recovery starved a city actor");
 index.Track(9,1,SignalMaintenanceIndex<int>.Hidden);index.Forget(9,SignalMaintenanceIndex<int>.Moved);Check(index.TryGet(9,out var remaining)&&remaining.Kinds==SignalMaintenanceIndex<int>.Hidden,"Mixed ownership removed too early");index.Forget(9,SignalMaintenanceIndex<int>.Hidden);Check(!index.TryGet(9,out _),"Retired record survived");
 index.Track(10,2000,SignalMaintenanceIndex<int>.Moved);index.CopyNode(1,work);Check(work.TrueForAll(e=>e.Actor!=10),"Owner transfer left old membership");index.CopyNode(2000,work);Check(work.Count==1&&work[0].Actor==10,"Owner transfer lost new membership");
 for(int pass=0;pass<2000;pass++){index.TakeRecovery(64,work);Check(work.Count<=64,"Recovery exceeded budget");foreach(var e in work)if(e.Actor%3==0)index.Forget(e.Actor,255);index.Track(20000+pass,3000+pass,1);}
 index.Clear();index.TakeRecovery(64,work);Check(index.Count==0&&work.Count==0,"Load/save reset retained records");index.Track(1,1,1);index.Forget(1,255);index.Track(2,2,1);index.TakeRecovery(64,work);Check(work.Count==1&&work[0].Actor==2,"Cursor referenced a removed sole actor");
 Console.WriteLine("PASS: "+checks+" ownership/index assertions, 10,000 actors; local edit visits 8, recovery stays at 64 records, churn and reset retain fairness. Managed algorithm, not runtime frame timings.");}
}
