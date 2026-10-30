using System;
using System.Collections.Generic;
using System.Reflection;
class WorkBatchChecks
{
    static void Main()
    {
        var take=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.WorkBatchPolicy",true).GetMethod("Take",BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(typeof(int));
        int checks=0;
        foreach(int size in new[]{0,1,7,8,9,1000,100000})foreach(int budget in new[]{0,1,2,8})
        {
            var source=new HashSet<int>();for(int i=0;i<size;i++)source.Add(i);
            var output=new List<int>{-1};take.Invoke(null,new object[]{source,output,budget});
            if(output.Count!=Math.Min(size,budget)||source.Count!=size)throw new Exception("Snapshot exceeded budget or consumed pending work");checks++;
            var expected=new List<int>();foreach(var item in source){if(expected.Count>=budget)break;expected.Add(item);}
            for(int i=0;i<expected.Count;i++)if(expected[i]!=output[i])throw new Exception("Existing queue selection order changed");
            // Rebuild callbacks may add/remove pending nodes after the snapshot.
            foreach(int item in output)source.Remove(item);source.Add(size+1);
            if(source.Count!=size-output.Count+1)throw new Exception("Unprocessed work lost");checks++;
            take.Invoke(null,new object[]{source,output,budget});
            if(output.Count>budget||output.Contains(-1))throw new Exception("Scratch output retained prior work");checks++;
        }
        Console.WriteLine("PASS: "+checks+" bounded queue cases through 100,000 nodes, preserving work/order across source mutation. No in-game frame-time claim.");
    }
}
