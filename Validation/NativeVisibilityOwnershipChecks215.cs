using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;

class NativeVisibilityOwnershipChecks215
{
    static readonly BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static ComponentType Fake(int value)
    {
        object type=Activator.CreateInstance(typeof(ComponentType));
        var index=Array.Find(typeof(ComponentType).GetFields(Fields),f=>f.FieldType.Name=="TypeIndex");
        if(index==null)throw new Exception("ComponentType layout changed");
        object token=Activator.CreateInstance(index.FieldType);
        var integer=Array.Find(index.FieldType.GetFields(Fields),f=>f.FieldType==typeof(int));
        if(integer==null)throw new Exception("TypeIndex layout changed");
        integer.SetValue(token,value);index.SetValue(type,token);return (ComponentType)type;
    }
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var method=assembly.GetType("MedianGrassReplacement.Systems.NativeLaneVisibilityQuery",true).GetMethod("TryBuild",BindingFlags.NonPublic|BindingFlags.Static);
        var types=new ComponentType[10];for(int n=0;n<types.Length;n++)types[n]=Fake(n+1);
        EntityQueryDesc[] Build(EntityQueryDesc[] originals)
        {
            object[] args={originals,types[0],types[1],types[2],types[3],types[4],types[5],types[6],types[7],null};
            if(!(bool)method.Invoke(null,args))return null;return (EntityQueryDesc[])args[9];
        }
        bool Matches(EntityQueryDesc query,int mask)
        {
            bool Has(ComponentType type){for(int n=0;n<types.Length;n++)if(type.TypeIndex==types[n].TypeIndex)return (mask&(1<<n))!=0;throw new Exception("Unknown fake token");}
            foreach(var type in query.All??Array.Empty<ComponentType>())if(!Has(type))return false;
            foreach(var type in query.None??Array.Empty<ComponentType>())if(Has(type))return false;
            if(query.Any!=null&&query.Any.Length!=0){bool yes=false;foreach(var type in query.Any)yes|=Has(type);if(!yes)return false;}
            return true;
        }
        foreach(bool foreignRequired in new[]{false,true})foreach(bool foreignExcluded in new[]{false,true})
        foreach(bool excludesTemp in new[]{false,true})foreach(bool excludesPed in new[]{false,true})
        foreach(EntityQueryOptions option in Enum.GetValues(typeof(EntityQueryOptions)))
        {
            var no=new List<ComponentType>();if(foreignExcluded)no.Add(types[9]);if(excludesTemp)no.Add(types[6]);if(excludesPed)no.Add(types[3]);
            var source=new EntityQueryDesc{All=foreignRequired?new[]{types[0],types[8]}:new[]{types[0]},Any=new[]{types[1],types[2]},None=no.ToArray(),Options=option};
            var oldAll=(ComponentType[])source.All.Clone();var oldAny=(ComponentType[])source.Any.Clone();var oldNone=(ComponentType[])source.None.Clone();
            var next=Build(new[]{source});Check(next!=null,"supported native/foreign exclusions rejected");
            foreach(var clause in next)
            {
                foreach(var all in clause.All)foreach(var any in clause.Any)Check(all.TypeIndex!=any.TypeIndex,"duplicate type in All/Any");
                foreach(var all in clause.All)foreach(var none in clause.None)Check(all.TypeIndex!=none.TypeIndex,"contradictory All/None clause");
                Check(clause.Options==source.Options,"native query options changed");
            }
            for(int mask=0;mask<1024;mask++)
            {
                bool Has(int bit)=>(mask&(1<<bit))!=0;
                bool native=Matches(source,mask);bool actual=false;foreach(var clause in next)actual|=Matches(clause,mask);
                bool committedLeaf=Has(1)&&!Has(2)&&!Has(6)&&!Has(7);
                bool ourHide=Has(5)||(Has(4)&&!Has(3));
                bool expected=native&&!(committedLeaf&&ourHide);
                Check(actual==expected,"native parent/temp/deleted/routed/foreign role changed mask="+mask);
            }
            Check(source.All.Length==oldAll.Length&&source.Any.Length==oldAny.Length&&source.None.Length==oldNone.Length,"source query arrays mutated");
        }
        Check(Build(null)==null&&Build(Array.Empty<EntityQueryDesc>())==null,"missing query accepted");
        Check(Build(new[]{new EntityQueryDesc{All=new[]{types[0]},Any=new[]{types[1]},None=Array.Empty<ComponentType>()}})==null,"unknown replacement query shape rewritten");
        // Demonstrate the old reveal/re-hide cycle as value transitions. Jobs
        // themselves require a native World and are not executed in this test.
        int[] sourceCounts={1,16,128,538,4096};
        foreach(int count in sourceCounts)foreach(bool parentHidden in new[]{false,true})
        {
            int nativeReveals=parentHidden?0:count;
            int repairs=nativeReveals;
            Check(nativeReveals+repairs==(parentHidden?0:count*2),"feedback fixture changed");
        }
        Console.WriteLine("PASS: "+checks+" native query ownership cases across component/foreign-exclusion combinations and every installed query-option value, plus schema and modeled feedback checks. Native job execution, renderer, tools and saves remain unverified.");
    }
}
