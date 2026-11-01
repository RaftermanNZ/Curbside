using System;
using System.Reflection;
internal static class SurfaceRestoreChecks
{
    private static readonly Type Policy=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.SurfaceRestorePolicy",true);
    private static int checks;
    private static void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
    private static bool Recover(bool area,bool materialAsset,bool textureAsset,bool tracked,string current,bool original,bool same)
        =>(bool)Policy.GetMethod("CanRecover",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{area,materialAsset,textureAsset,tracked,current,original,same});
    public static void Main()
    {
        Check(Recover(true,false,false,false,"MGR::OldSidewalk",true,false),"Legacy untracked batch cannot recover its prefab original");
        Check(Recover(true,false,false,false,"MGR::OldCustomSelection",true,false),"Modded area recovery must not require a vanilla replacement name");
        Check(!Recover(true,false,false,true,"MGR::OldSidewalk",true,false),"Tracked original and UV journal must take precedence");
        Check(!Recover(true,false,false,false,"OtherMod::Concrete",true,false),"Later other-mod texture was overwritten");
        Check(!Recover(true,false,false,false,"Sidewalk",true,false),"Ordinary texture filename was mistaken for Curbside ownership");
        Check(!Recover(true,true,false,false,"MGR::OldSidewalk",true,false),"Registered prefab material template admitted");
        Check(!Recover(true,false,true,false,"MGR::OldSidewalk",true,false),"Registered asset texture admitted");
        Check(!Recover(true,false,false,false,"MGR::OldSidewalk",false,false),"Missing or ambiguous original admitted");
        Check(!Recover(false,false,false,false,"MGR::OldSidewalk",true,false),"Non-area road/sidewalk admitted to extra-surface recovery");
        Check(!Recover(true,false,false,false,"MGR::OldSidewalk",true,true),"No-op original admitted");
        foreach(var name in new[]{null,"","MGR::","mgr::Concrete","NotMGR::Concrete"})
            Check(!Recover(true,false,false,false,name,true,false),"Invalid owner marker admitted");
        Console.WriteLine("PASS: "+checks+" recovery ownership/failure-path cases. Native material and rendered-surface behavior still requires game testing.");
    }
}
