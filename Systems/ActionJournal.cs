using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
namespace ModDiagnostics
{
    // Opt-in, bounded local journal. Never holds ECS buffers, jobs, textures or native views.
    internal static class ActionJournal
    {
        private static readonly object Gate=new object();
        private static readonly List<string> Pending=new List<string>();
        private static StreamWriter writer;private static long sequence,bytes,dropped;private static double started;
        internal static bool Active {get;private set;}
        internal static bool Screenshots {get;private set;}
        internal static string Folder {get;private set;}
        internal static string Status {get;private set;}="Recording off.";
        internal static double Clock=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        internal static bool Start(string root,string build,bool screenshots)
        {
            Stop("restarted");
            try {
                Folder=Path.Combine(root,"LineRecording-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(Folder);
                writer=new StreamWriter(Path.Combine(Folder,"actions.jsonl"),false,new UTF8Encoding(false));
                sequence=bytes=dropped=0;started=Clock;Screenshots=screenshots;Active=true;
                TraceBuffer.SetEnabled(true);TraceBuffer.Journal=Record;
                Record("session.start",build+"; screenshots="+screenshots+"; local only; observed changes do not identify their creator; watched-owner limits apply");
                Status="Recording locally: "+Folder;Flush();return true;
            } catch(Exception e){Stop("start failed: "+e.GetType().Name);return false;}
        }
        private static string Quote(string value)=>"\""+(value??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t")+"\"";
        internal static void Record(string stage,string detail)
        {
            if(!Active)return;
            lock(Gate){if(!Active)return;if(Pending.Count>=4096){dropped++;return;}
                if(detail!=null&&detail.Length>8192)detail=detail.Substring(0,8192)+" [detail limit]";
                Pending.Add("{\"seq\":"+(++sequence)+",\"utc\":"+Quote(DateTime.UtcNow.ToString("O"))+",\"elapsed\":"+(Clock-started).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+",\"stage\":"+Quote(stage)+",\"detail\":"+Quote(detail)+"}");}
        }
        internal static void Flush()
        {
            if(!Active)return;
            try {lock(Gate){foreach(var row in Pending){writer.WriteLine(row);bytes+=Encoding.UTF8.GetByteCount(row)+1;}Pending.Clear();writer.Flush();}
                if(bytes>=32*1024*1024||Clock-started>=1200)Stop("session limit reached (32 MiB or 20 minutes)");
            }catch(Exception e){Stop("write failed: "+e.GetType().Name);}
        }
        internal static void Stop(string reason)
        {
            TraceBuffer.Journal=null;
            lock(Gate){try{if(writer!=null){foreach(var row in Pending)writer.WriteLine(row);Pending.Clear();writer.WriteLine("{\"stage\":\"session.stop\",\"detail\":"+Quote(reason+"; dropped="+dropped)+"}");writer.Dispose();}}catch{}finally{writer=null;Pending.Clear();Active=false;Screenshots=false;Status=reason+(Folder==null?"":". Files: "+Folder);}}
        }
    }
}
