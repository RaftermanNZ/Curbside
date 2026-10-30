using System;
using System.Diagnostics;
using System.Text;

namespace ModDiagnostics
{
    // Independent copy in each assembly: neither mod depends on the other.
    // No engine access, timers, file writer, background loop or subscriptions.
    internal static class TraceBuffer
    {
        internal const int Capacity=128, DetailLimit=160;
        private static readonly object Gate=new object();
        private static readonly string[] Rows=new string[Capacity];
        private static int head,count;private static long sequence;private static bool enabled;
        internal static bool Enabled { get { return enabled; } }
        internal static void SetEnabled(bool value){lock(Gate){enabled=value;if(value){Array.Clear(Rows,0,Rows.Length);head=count=0;sequence=0;}}}
        internal static void Record(string stage,string detail)
        {
            if(!enabled)return;
            lock(Gate)
            {
                if(!enabled)return;
                string text=(detail??"").Replace('\n',' ').Replace('\r',' ');
                if(text.Length>DetailLimit)text=text.Substring(0,DetailLimit);
                Rows[head]=(++sequence)+" @"+Stopwatch.GetTimestamp()+" "+stage+": "+text;
                head=(head+1)%Capacity;if(count<Capacity)count++;
            }
        }
        internal static string Snapshot(string build)
        {
            lock(Gate)
            {
                var output=new StringBuilder("Diagnostic snapshot: "+build+"; enabled="+enabled+"; retained="+count+"/"+Capacity+"; ticks/sec="+Stopwatch.Frequency+"\n");
                for(int i=0;i<count;i++)output.AppendLine(Rows[(head-count+i+Capacity)%Capacity]);
                return output.ToString();
            }
        }
    }
}
