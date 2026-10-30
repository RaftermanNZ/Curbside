using System;
using System.Reflection;
class SignalBindingChecks
{
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var policy=assembly.GetType("MedianGrassReplacement.Systems.SignalBindingPolicy",true).GetMethod("Changed",BindingFlags.NonPublic|BindingFlags.Static);
        ushort[] masks={0,1,2,3,7,128,32768,65535};int count=0;
        foreach(ushort vehicle in masks)foreach(ushort pedestrian in masks)foreach(ushort beforeVehicle in masks)foreach(ushort beforePedestrian in masks)
        {
            bool changed=(bool)policy.Invoke(null,new object[]{vehicle,pedestrian,beforeVehicle,beforePedestrian});
            if(changed!=(vehicle!=beforeVehicle||pedestrian!=beforePedestrian))throw new Exception("Phase membership change missed, or unchanged binding rebuilt");
            count++;
        }
        var covered=assembly.GetType("MedianGrassReplacement.Systems.SignalBindingPolicy",true).GetMethod("Covered",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(ushort required in masks)foreach(ushort visible in masks)foreach(bool needsVehicle in new[]{false,true})foreach(bool needsPedestrian in new[]{false,true})
        {
            bool actual=(bool)covered.Invoke(null,new object[]{required,required,needsVehicle,needsPedestrian,visible,visible});
            bool expected=(!needsVehicle&&!needsPedestrian)||(required!=0&&(visible&required)==required);
            if(actual!=expected)throw new Exception("Missing phase hidden or complete custom coverage rejected");count++;
        }
        // The reported large-T actors had phases (vehicle0,ped1) and (vehicle1,ped2).
        if(!(bool)covered.Invoke(null,new object[]{(ushort)0,(ushort)1,false,true,(ushort)3,(ushort)3})
            ||!(bool)covered.Invoke(null,new object[]{(ushort)1,(ushort)2,true,true,(ushort)3,(ushort)3})
            ||(bool)covered.Invoke(null,new object[]{(ushort)1,(ushort)2,true,true,(ushort)3,(ushort)1}))throw new Exception("Captured T-junction phase case failed");count+=3;
        foreach(var reference in assembly.GetReferencedAssemblies())
            if(reference.Name.StartsWith("C2VM",StringComparison.Ordinal)||reference.Name.StartsWith("TrafficToolEssentials",StringComparison.Ordinal))
                throw new Exception("Optional timing controller became a required assembly dependency");
        var moved=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightSystem",true).GetNestedType("Moved",BindingFlags.NonPublic);
        if(typeof(Unity.Entities.IComponentData).IsAssignableFrom(moved)||typeof(Colossal.Serialization.Entities.ISerializable).IsAssignableFrom(moved))
            throw new Exception("Derived signal binding became persisted state");
        Console.WriteLine("PASS: "+count+" native signal membership cases; no timing-mod assembly dependency or serialized binding added. Timed-phase runtime visual tests pending.");
    }
}
