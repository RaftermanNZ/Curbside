using System;
using System.Reflection;
class OwnerPolicyChecks
{
    static void Main()
    {
        var type=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.GeneratedPaintOwnerPolicy",true);
        var policy=type.GetMethod("Retire",BindingFlags.Static|BindingFlags.NonPublic);
        bool[][] cases={
            new[]{true,false,true,false}, // Valid road/junction: retain all paint.
            new[]{true,true,true,true},   // Deletion event before owner is purged.
            new[]{false,false,false,true},// Owner already destroyed.
            new[]{true,false,false,true}, // Incomplete/removed owner definition.
            new[]{true,true,false,true},  // Deleted owner with definition removed.
            new[]{false,true,true,true}   // Stale owner/version cannot keep paint.
        };
        foreach(var row in cases)
            if((bool)policy.Invoke(null,new object[]{row[0],row[1],row[2]})!=row[3])
                throw new Exception("Owner retirement scenario failed.");
        Console.WriteLine(cases.Length+" owner lifecycle scenarios passed: valid paint retained; deleted, missing and incomplete owners retired.");
    }
}
