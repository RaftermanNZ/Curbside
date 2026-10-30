using System.Collections.Generic;
namespace MedianGrassReplacement.Systems
{
    internal static class WorkBatchPolicy
    {
        // Snapshot only the work this pass can consume. Callers may then mutate
        // the source queue during processing without retaining its enumerator.
        internal static void Take<T>(HashSet<T> source, List<T> output, int limit)
        {
            output.Clear();
            if(limit<=0)return;
            foreach(var item in source)
            {
                output.Add(item);
                if(output.Count>=limit)break;
            }
        }
    }
}
