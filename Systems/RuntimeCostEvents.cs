using System;
using System.Globalization;
using System.Text;

namespace ModDiagnostics
{
    // Called only by existing diagnostic timing callbacks. No Update, engine
    // queries, timers, subscriptions or file I/O on the recording path.
    internal static class RuntimeCostEvents
    {
        internal const int Capacity = 32768;
        private struct Sample { internal int Frame; internal string Name; internal long Start, End; }
        private static Sample[] rows;
        private static int head, count;
        private static long total;
        private static bool frozen;

        internal static void Reset()
        {
            if (rows != null) Array.Clear(rows, 0, rows.Length);
            head = count = 0; total = 0; frozen = false;
        }

        internal static void Record(int frame, string name, long start, long end)
        {
            if (frozen || end < start) return;
            if (rows == null) rows = new Sample[Capacity];
            rows[head] = new Sample { Frame = frame, Name = name, Start = start, End = end };
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
            total++;
        }

        internal static string FreezeCsv(long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            frozen = true;
            var csv = new StringBuilder("# Curbside callback elapsed time includes waits; nested calls overlap; not full CPU frame time.\n");
            csv.Append("# retained=").Append(count).Append(" total=").Append(total).Append(" frequency=").Append(frequency).Append('\n');
            csv.Append("sequence,frame,system,startTicks,endTicks,elapsedMs\n");
            for (int i = 0; i < count; i++)
            {
                var sample = rows[(head - count + i + Capacity) % Capacity];
                csv.Append(total - count + i + 1).Append(',').Append(sample.Frame).Append(',')
                    .Append('"').Append((sample.Name ?? "").Replace("\"", "\"\"")).Append('"').Append(',')
                    .Append(sample.Start).Append(',').Append(sample.End).Append(',')
                    .Append(((sample.End - sample.Start) * (1000d / frequency)).ToString("F6", CultureInfo.InvariantCulture)).Append('\n');
            }
            return csv.ToString();
        }
    }
}
