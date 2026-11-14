using System;
using System.Collections.Generic;

namespace MedianGrassReplacement.Systems
{
    // Native PostCreate appends this tag on every database refresh. Native VT
    // lookup requires exactly one tag. Retain the first occurrence and never
    // discard an unfamiliar tag to force a cache into a different configuration.
    internal static class SignalMidMipCacheTags
    {
        internal const string ExpectedTag = "TS512_MB3";

        internal static int Normalize(IReadOnlyList<string> tags)
        {
            if (!(tags is List<string> writable)) return 0;
            bool found = false;
            int write = 0;
            int count = writable.Count;
            for (int read = 0; read < count; read++)
            {
                string tag = writable[read];
                if (string.Equals(tag, ExpectedTag, StringComparison.Ordinal))
                {
                    if (found) continue;
                    found = true;
                }
                if (write != read) writable[write] = tag;
                write++;
            }
            int removed = count - write;
            if (removed != 0) writable.RemoveRange(write, removed);
            return removed;
        }
    }
}
