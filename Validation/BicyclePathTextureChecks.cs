using System;
using System.Reflection;
class BicyclePathTextureChecks
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var policy=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.GlobalSurfacePolicy",true);
        var kind=policy.GetMethod("Kind",BindingFlags.Static|BindingFlags.NonPublic);
        var protect=policy.GetMethod("ProtectedBicyclePath",BindingFlags.Static|BindingFlags.NonPublic);
        string shader="BH/NetCompositionMeshLitShader";
        foreach(var surface in new[]{"BikePathPavement300cm","BikePathPavement800cm","BikePathPavementMiddle300cm","BikePathPavementMiddle800cm","BikePathPavementSide0cm"})
            foreach(var suffix in new[]{"","_9abfd1a7cc766d58dea7d215fcfb149c"})
                foreach(var name in new[]{surface+suffix,"Batch ("+surface+suffix+")"})
                {
                    Check((bool)protect.Invoke(null,new object[]{name}),"Missing native bicycle-path identity: "+name);
                    Check((int)kind.Invoke(null,new object[]{name,shader})==0,"Bicycle-path material replaced: "+name);
                }
        foreach(var name in new[]{null,"","BikeLane150cm","Road_Sidewalk","PathwayPavement800cm","CustomBikePathPavement300cm","BikePathPavement300cm_Custom","BikePathPavement300cm_zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz","BikePathPavement300cm(Clone)","Road_BikePathPavement300cm"})
            Check(!(bool)protect.Invoke(null,new object[]{name}),"Over-broad bicycle-path exclusion: "+name);
        foreach(var name in new[]{"Road_Sidewalk","Road_Asphalt","Road_ParkingRoad","Road_Alley"})
            Check((int)kind.Invoke(null,new object[]{"Batch ("+name+")",shader})==3,"Road classification changed: "+name);
        Check((int)kind.Invoke(null,new object[]{"Batch (PathwayPavement800cm)",shader})==2,"Shared pedestrian surface changed");
        Check((int)kind.Invoke(null,new object[]{"Batch (BikeLane150cm)",shader})==0,"Road bicycle lane changed");
        Console.WriteLine(checks+" bicycle-path scope checks passed; in-game appearance remains unverified.");
    }
}
