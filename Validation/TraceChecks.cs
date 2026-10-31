using System;using ModDiagnostics;
class TraceChecks
{
 static int n;static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
 static void Main()
 {
  TraceBuffer.SetEnabled(false);TraceBuffer.Record("off","must not retain");Check(!TraceBuffer.Snapshot("test").Contains("must not retain"),"Disabled capture must do nothing");
  TraceBuffer.SetEnabled(true);
  for(int i=0;i<200;i++)TraceBuffer.Record("stage", "event="+i);
  string s=TraceBuffer.Snapshot("test");Check(s.Contains("retained=128/128"),"bounded capacity");Check(!s.Contains("event=71"+Environment.NewLine),"old rows evicted");Check(s.Contains("event=72"+Environment.NewLine),"oldest retained row");Check(s.IndexOf("event=72"+Environment.NewLine)<s.IndexOf("event=199"+Environment.NewLine),"chronological wrap order");
  TraceBuffer.Record("long",new string('x',500));s=TraceBuffer.Snapshot("test");Check(s.Contains(new string('x',160)),"detail retained up to limit");Check(!s.Contains(new string('x',161)),"detail capped");
  TraceBuffer.Record("line","first\nsecond\rthird");Check(TraceBuffer.Snapshot("test").Contains("first second third"),"single line events");
  TraceBuffer.SetEnabled(false);s=TraceBuffer.Snapshot("test");TraceBuffer.Record("off","discarded");Check(TraceBuffer.Snapshot("test")==s,"disable freezes capture without a timer or flush");
  TraceBuffer.SetEnabled(true);Check(!TraceBuffer.Snapshot("test").Contains("event="),"new session clears stale trace");
  TraceBuffer.SetEnabled(false);Console.WriteLine(n+" diagnostic capture bounds, opt-in and retention checks passed.");
 }
}
