using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;

class PlacementBank226 { public float[] left,right,outward;public float[][] lanes;public ushort[] masks; }
class PlacementActor226 { public string actor,prefab;public float[] position;public int approach;public ushort mask;public bool ownedHidden; }
class PlacementInput226 { public string node,time,scope;public PlacementBank226[] banks;public PlacementActor226[] actors; }

class SignalPlacementChecks226
{
    const BindingFlags B=BindingFlags.Static|BindingFlags.NonPublic, F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Assembly assembly;
    static Type far,garden,approach,entry,coverage;
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static float3 V(float[] a)=>new float3(a[0],a[1],a[2]);
    static object Call(Type t,string name,params object[] args)=>t.GetMethod(name,B).Invoke(null,args);
    static object Banks(PlacementBank226[] banks)
    {
        var result=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
        foreach(var b in banks)
        {
            var a=Activator.CreateInstance(approach);
            Action<string,object> set=(n,v)=>approach.GetField(n,F).SetValue(a,v);
            set("Left",V(b.left));set("Right",V(b.right));set("Outward",new float2(b.outward[0],b.outward[1]));
            set("Incoming",b.lanes.Select(V).ToList());set("LaneMasks",b.masks.ToList());result.Add(a);
        }
        return result;
    }
    static void Pack(int mode)
    {
        var t=assembly.GetType("GardenCitySignals.SignalPackPolicy",true);
        t.GetProperty("UseRigidSignLayout",B).GetSetMethod(true).Invoke(null,new object[]{mode!=0});
        t.GetProperty("UseLowerRigidSignLayout",B).GetSetMethod(true).Invoke(null,new object[]{mode==2});
    }
    static bool Mount(bool styled,int style,Transform source,object banks,int road,IList<float3> occupied,out Transform pose)
    {
        bool wire=style==1||style==3;
        string name=wire?(styled?"TryAppearanceSignalMountFrom":"TrySignalMountFrom"):(styled?"TryAppearanceArmMountFrom":"TryArmMountFrom");
        var args=wire?(styled?new object[]{source,banks,road,(byte)style,null}:new object[]{source,banks,road,null})
            :(styled?new object[]{source,banks,road,occupied,(byte)style,null}:new object[]{source,banks,road,occupied,null});
        bool ok=(bool)Call(far,name,args);pose=(Transform)args[args.Length-1];
        if(!ok&&wire&&styled)
        {args=new object[]{source,banks,road,occupied,(byte)style,null};ok=(bool)Call(far,"TryAppearanceArmMountFrom",args);pose=(Transform)args[args.Length-1];}
        return ok;
    }
    static bool Fits(object bank,float3 point,int style)=>(bool)Call(garden,"AppearanceCandidate",bank,point,(byte)style);
    static object Entries(params Tuple<int,ushort,ushort>[] items)
    {
        var values=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entry));
        foreach(var item in items){var e=Activator.CreateInstance(entry);entry.GetField("Approach",F).SetValue(e,item.Item1);entry.GetField("Vehicle",F).SetValue(e,item.Item2);entry.GetField("Pedestrian",F).SetValue(e,item.Item3);values.Add(e);}return values;
    }
    static Tuple<int,ushort,ushort> E(int road,int vehicle,int ped=0)=>Tuple.Create(road,(ushort)vehicle,(ushort)ped);
    static bool Covered(int road,int vehicle,int ped,bool needsV,bool needsP,object values)
        =>(bool)Call(coverage,"Covered",road,(ushort)vehicle,(ushort)ped,needsV,needsP,values);
    static IEnumerable<int[]> Orders(int[] xs)
    {if(xs.Length==0){yield return xs;yield break;}foreach(int x in xs)foreach(var tail in Orders(xs.Where(y=>y!=x).ToArray()))yield return new[]{x}.Concat(tail).ToArray();}

    static void Main()
    {
        assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        far=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);approach=far.GetNestedType("Approach",BindingFlags.NonPublic);
        garden=assembly.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);
        coverage=assembly.GetType("MedianGrassReplacement.Systems.SignalCoveragePolicy",true);entry=assembly.GetType("MedianGrassReplacement.Systems.SignalCoverageEntry",true);
        var data=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<PlacementInput226>(File.ReadAllText("GardenMissing226.json"));
        var banks=Banks(data.banks);
        var primary=data.actors.GroupBy(a=>a.approach).Select(g=>g.OrderBy(a=>{
            var lanes=data.banks[g.Key].lanes.Select(V).ToArray();float3 centre=float3.zero;foreach(var p in lanes)centre+=p;centre/=lanes.Length;
            return math.distancesq(V(a.position).xz,centre.xz);
        }).First()).OrderBy(a=>a.approach).ToArray();
        Check(primary.Length==4&&primary[0].actor=="Entity(75238:129)"&&primary[0].ownedHidden,"Captured failing approach changed");
        Pack(2);
        Check(Mount(false,0,new Transform(V(primary[0].position),quaternion.identity),banks,0,new List<float3>(),out var bad)
            &&!Fits(((IList)banks)[0],bad.m_Position,0),"Original placement/appearance gap no longer reproduced as control");
        var oldBinding=assembly.GetType("MedianGrassReplacement.Systems.SignalBindingPolicy",true);
        Check((bool)Call(oldBinding,"Covered",(ushort)2,(ushort)0,true,false,(ushort)2,(ushort)0),"Original node-wide hide control changed");
        Check(!Covered(0,2,0,true,false,Entries(E(3,2))),"Opposite approach incorrectly hides failed replacement");
        var outcomes=new List<object>();
        for(int mode=0;mode<3;mode++)for(int style=0;style<5;style++)foreach(var order in Orders(new[]{0,1,2,3}))
        {
            Pack(mode);var occupied=new List<float3>();var ready=new List<Tuple<int,ushort,ushort>>();
            var attempts=new List<object>();
            foreach(int road in order)
            {
                var a=primary[road];bool fit=Mount(true,style,new Transform(V(a.position),quaternion.identity),banks,road,occupied,out var pose);
                Check(fit,"Captured approach lost all fitting candidates: style="+style+" mode="+mode+" road="+road);
                Check(Fits(((IList)banks)[road],pose.m_Position,style),"Planner accepted an appearance-rejected mount");
                Check((bool)Call(far,"IsFarSide",pose.m_Position,banks,road),"Styled candidate lost far-side/roadside validity");
                occupied.Add(pose.m_Position);ready.Add(E(road,a.mask));attempts.Add(new{road,position=pose.m_Position.ToString()});
            }
            foreach(var a in primary)
            {
                Check(Covered(a.approach,a.mask,0,true,false,Entries(ready.ToArray())),"Successful own replacement does not cover source");
                Check(!Covered(a.approach,a.mask,0,true,false,Entries(ready.Where(e=>e.Item1!=a.approach).ToArray())),"Removed/failed source still hidden by other approach");
                // A failed vehicle may be reused as a pedestrian donor only if
                // this same movement already has another visible replacement.
                Check(!Covered(a.approach,a.mask,0,true,false,Entries(E((a.approach+2)%4,a.mask,3))),"Pedestrian fallback would consume unserved vehicle controller");
            }
            outcomes.Add(new{mode,style,order,attempts});
        }
        // Native/TTE phase numbers carry no direction. Test bit relabels and
        // partial/combined/protected membership on 3/4/5/6-approach junctions.
        for(int legs=3;legs<=6;legs++)for(int required=0;required<legs;required++)for(int bit=0;bit<16;bit++)
        {
            int phase=1<<bit,second=1<<((bit+1)%16);
            for(int other=0;other<legs;other++)
            {
                Check(Covered(required,phase,0,true,false,Entries(E(other,phase)))==(required==other),"Phase bit mistaken for road identity");
                Check(!Covered(-1,phase,0,true,false,Entries(E(other,phase))),"Unknown source approach suppressed");
                Check(!Covered(required,phase|second,0,true,false,Entries(E(other,phase))),"Partial protected/combined phase suppressed");
            }
            Check(Covered(required,phase|second,0,true,false,Entries(E(required,phase),E(required,second))),"Same-road complete protected phases not composable");
            Check(!Covered(required,phase|second,0,true,false,Entries(E(required,phase),E((required+1)%legs,second))),"Different roads combined into false coverage");
            Check(Covered(-1,0,phase,false,true,Entries(E((required+1)%legs,0,phase))),"Independent pedestrian phase made dependent on vehicle approach");
            Check(!Covered(required,phase,second,true,true,Entries(E(required,phase,phase))),"Dual-role source loses uncovered crossing");
            Check(Covered(required,phase,second,true,true,Entries(E(required,phase),E((required+1)%legs,0,second))),"Dual-role complete vehicle/pedestrian coverage rejected");
        }
        for(int bits=0;bits<64;bits++)
        {
            bool sameOwner=(bits&1)!=0,sameDefinition=(bits&2)!=0,samePose=(bits&4)!=0,ownedHidden=(bits&8)!=0,toolHidden=(bits&16)!=0,overridden=(bits&32)!=0;
            bool ready=(bool)Call(coverage,"Ready",sameOwner,sameDefinition,samePose,ownedHidden,toolHidden,overridden);
            Check(ready==(bits==7),"Unapplied/foreign/hidden/overridden appearance accepted as coverage");
        }
        foreach(int source in new[]{-1,0,1,5})
        {
            Check(!Covered(source,0,0,true,false,Entries(E(source,65535))),"Zero vehicle binding suppressed");
            Check(!Covered(source,1,0,true,false,Entries()),"Absent replacement suppressed");
        }
        File.WriteAllText("SignalPlacement226-Replay.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new{checks,capturedOrders=outcomes.Count,outcomes,
            scope="Compiled placement/coverage contracts with captured banks; no native ECS, renderer, upgrade timing, previews, save round trips or FPS execution."}));
        Console.WriteLine("PASS: "+checks+" placement/coverage checks; 360 captured order/style/pack scenarios; original controls reproduced, wrong-road hiding rejected, all captured approaches find appearance-valid candidates. Native ECS/live visuals/FPS remain unverified.");
    }
}
