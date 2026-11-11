using System;
using System.Collections;
using System.Reflection;
using Unity.Entities;

class LaneQueueChecks215
{
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");var flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance;
        var valueType=assembly.GetType("MedianGrassReplacement.Systems.TemporaryPreviewWrite",true);
        var dictionary=typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(Entity),valueType);
        var queue=assembly.GetType("MedianGrassReplacement.Systems.TemporaryPreviewWrites",true).GetMethod("QueueLanePrefab",flags);
        Entity Id(int n,int version=1)=>new Entity{Index=n,Version=version};int checks=0;
        for(int state=0;state<100;state++)foreach(bool prior in new[]{false,true})foreach(bool ready in new[]{false,true})
        {
            var actor=Id(7,state%3+1);var other=Id(7,state%3+10);var current=Id(90+state%5);var target=state%2==0?current:Id(100+state%7);
            var requests=(IDictionary)Activator.CreateInstance(dictionary);
            queue.Invoke(null,new object[]{requests,other,Id(1),Id(2)});
            if(prior)queue.Invoke(null,new object[]{requests,actor,current,Id(200)});
            queue.Invoke(null,new object[]{requests,actor,current,target});
            Entity actual=current;if(ready&&requests.Contains(actor))actual=(Entity)valueType.GetField("Prefab",flags).GetValue(requests[actor]);
            if(actual!=(ready?target:current)||!requests.Contains(other))throw new Exception("latest intent or other entity version changed");checks+=2;
            if(current==target){if(requests.Contains(actor))throw new Exception("old conflicting request survived no-op");checks++;}
            else{var value=requests[actor];if((bool)valueType.GetField("HasPose",flags).GetValue(value)||(bool)valueType.GetField("HasOriginal",flags).GetValue(value))throw new Exception("lane-only queue changed identity or pose");checks++;}
        }
        Console.WriteLine("PASS: "+checks+" lane no-op / last-write / entity-version cases. The existing native readiness, flags, fence and playback implementation remains unchanged.");
    }
}
