using System;
using System.Reflection;
using Unity.Entities;

class SignalPoseOwnershipChecks214
{
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var policy=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true).GetMethod("CanRestorePose",BindingFlags.NonPublic|BindingFlags.Static);
        var owner=new Entity{Index=42,Version=7};var native=new Entity{Index=8,Version=3};var applied=new Entity{Index=9,Version=4};
        var owners=new[]{owner,new Entity{Index=42,Version=8},new Entity{Index=43,Version=7},Entity.Null};
        var prefabs=new[]{native,applied,new Entity{Index=9,Version=5},new Entity{Index=10,Version=4},Entity.Null};
        int checks=0;
        foreach(var actualOwner in owners)foreach(var current in prefabs)foreach(bool unchanged in new[]{false,true})
        {
            bool accepted=(bool)policy.Invoke(null,new object[]{actualOwner,owner,current,native,applied,unchanged});
            bool expected=actualOwner==owner&&unchanged&&(current==native||current==applied);
            if(accepted!=expected)throw new Exception("Cleanup overwrote a foreign pose/owner or failed to restore its own untouched pose");checks++;
        }
        foreach(var actualOwner in owners)foreach(bool unchanged in new[]{false,true})
        {
            // Plain Far Side changes position without changing prefab identity.
            bool accepted=(bool)policy.Invoke(null,new object[]{actualOwner,owner,native,native,native,unchanged});
            if(accepted!=(actualOwner==owner&&unchanged))throw new Exception("Plain/native-model relocation restoration regressed");checks++;
        }
        // An originally ownerless native child must still undo its own exact
        // write. This differs from removing a non-null owner after application.
        if(!(bool)policy.Invoke(null,new object[]{Entity.Null,Entity.Null,applied,native,applied,true}))throw new Exception("Recorded ownerless source failed to undo its own unchanged write");checks++;
        if((bool)policy.Invoke(null,new object[]{Entity.Null,owner,applied,native,applied,true}))throw new Exception("Removed original owner accepted");checks++;
        Console.WriteLine("PASS: "+checks+" cleanup ownership cases: native/custom/plain styles, moved/rotated pose, new owner, reused entity versions, missing owner and foreign prefab. Native save round trips and actual Move It/Anarchy coexistence remain unverified.");
    }
}
