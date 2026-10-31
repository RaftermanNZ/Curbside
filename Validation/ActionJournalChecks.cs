using System;using System.IO;using System.Reflection;
class ActionJournalChecks
{
 static void Main(){
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var t=a.GetType("ModDiagnostics.ActionJournal",true);var trace=a.GetType("ModDiagnostics.TraceBuffer",true);
  object Call(string name,params object[] args)=>t.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
  string Property(string name)=>(string)t.GetProperty(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  void Check(bool yes,string message){if(!yes)throw new Exception(message);}
  var root=Path.Combine(Environment.CurrentDirectory,"journal-check-"+Guid.NewGuid().ToString("N"));
  Check((bool)Call("Start",root,"test",false),"start");
  string folder=Property("Folder");
  string detail=new string('x',1200)+" quotes=\"slash\\ newline\n";
  trace.GetMethod("Record",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"check.full",detail});
  Call("Flush");Call("Stop","test complete");
  string text=File.ReadAllText(Path.Combine(folder,"actions.jsonl"));
  Check(text.Contains(new string('x',1200)),"journal must retain long records beyond ring limit");
  Check(text.Contains("\\\"slash\\\\ newline\\n"),"JSON escaping");
  Check(text.Contains("session.stop"),"stop marker");
  long size=new FileInfo(Path.Combine(folder,"actions.jsonl")).Length;Call("Record","off","not recorded");Call("Flush");Check(new FileInfo(Path.Combine(folder,"actions.jsonl")).Length==size,"off isolation");
  Check(trace.GetField("Journal",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)==null,"detach on stop");
  Check((bool)Call("Start",root,"limits",false),"restart");
  for(int i=0;i<5000;i++)Call("Record","burst",i.ToString());
  Call("Stop","bounded queue");text=File.ReadAllText(Path.Combine(Property("Folder"),"actions.jsonl"));Check(text.Contains("dropped=904"),"loss reported at queue limit");
  Check((bool)Call("Start",root,"byte cap",false),"third start");t.GetField("bytes",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,(long)32*1024*1024);Call("Flush");Check(!(bool)t.GetProperty("Active",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null),"auto stop byte cap");
  Console.WriteLine("PASS: full-detail journal, escaping, stop flush, off isolation, subscriber cleanup, bounded queue with reported loss, restart and byte-limit termination. Screenshots and observed ECS changes need live validation.");
 }
}
