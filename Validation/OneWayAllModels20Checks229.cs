using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Unity.Mathematics;
class WireBank229 {public float[] left,right,outward;public float[][] lanes;}
class WireCase229 {public string node,time;public WireBank229[] banks;public float[][] supports;public int[] sources;public bool expectedRepair;}
class OneWayAllModels20Checks229
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Assembly assembly;static Type approach,policy;static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static object Call(string method,params object[] args)=>policy.GetMethod(method,S).Invoke(null,args);
    static float3 V(float[] p)=>new float3(p[0],p[1],p[2]);
    static IList Roads(WireBank229[] banks)
    {
        var result=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
        foreach(var b in banks)
        {
            var road=Activator.CreateInstance(approach);
            approach.GetField("Left",F).SetValue(road,V(b.left));approach.GetField("Right",F).SetValue(road,V(b.right));
            approach.GetField("Outward",F).SetValue(road,new float2(b.outward[0],b.outward[1]));
            approach.GetField("Incoming",F).SetValue(road,b.lanes.Select(V).ToList());
            approach.GetField("LaneMasks",F).SetValue(road,b.lanes.Select(_=>(ushort)1).ToList());result.Add(road);
        }
        return result;
    }
    static bool OutsidePhysicalRoad(float3 point,WireBank229[] banks)
    {
        foreach(var b in banks)
        {
            var d=math.normalizesafe(new float2(b.outward[0],b.outward[1]));var lateral=new float2(-d.y,d.x);
            var left=V(b.left).xz;var right=V(b.right).xz;var middle=(left+right)*.5f;var delta=point.xz-middle;
            float a=math.dot(left-middle,lateral),c=math.dot(right-middle,lateral),offset=math.dot(delta,lateral);
            float along=math.max(0,-math.dot(delta,d));float side=math.max(0,math.max(math.min(a,c)-offset,offset-math.max(a,c)));
            if(along*along+side*side < .1599f)return false;
        }
        return true;
    }
    static void Main()
    {
        assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        approach=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy+Approach",true);
        policy=assembly.GetType("MedianGrassReplacement.Systems.WireCornerLayoutPolicy",true);
        var pack=assembly.GetType("GardenCitySignals.SignalPackPolicy",true);
        pack.GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        pack.GetProperty("UseLowerRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        int fits=0,guarded=0,cases=0,shiftChecks=0;
        var far=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);var armMount=far.GetMethod("TryAppearanceArmMountFrom",S);var move=far.GetMethod("TryMove",S);
        var counts=new List<object>();
        foreach(int legs in new[]{3,4,6})
        {
            int accepted=0,rejected=0;int combinations=1<<(3*legs);
            for(int configuration=0;configuration<combinations;configuration+=(legs==6?4093:1))
            {
                double angle=(configuration%12)*Math.PI/6;
                var b=new List<WireBank229>();var p=new List<float3>();
                Func<double,float2> D=a=>new float2((float)Math.Cos(a),(float)Math.Sin(a));
                Func<float3,float[]> A=v=>new[]{v.x,v.y,v.z};
                float maximumWidth=0;
                for(int arm=0;arm<legs;arm++)
                {
                    int treatment=(configuration>>(3*arm))&7;float median=(treatment&1)!=0?3.5f:0;
                    maximumWidth=math.max(maximumWidth,math.max(8.5f+((treatment&2)!=0?2:0)+median*.25f,7.5f+((treatment&4)!=0?2:0)+median*.75f));
                }
                // Keep each native-shaped mouth outside neighboring road strips.
                // Six arms require more depth than a four-arm fixture of equal width.
                double halfAngle=Math.PI/(legs==3?4:legs);
                float depth=math.max(16,maximumWidth/(float)Math.Tan(halfAngle)+2);
                float extent=(depth+1)/(float)Math.Cos(halfAngle);
                for(int i=0;i<legs;i++)
                {
                    double roadAngle=angle+(legs==3?i*Math.PI/2:i*2*Math.PI/legs);
                    var outward=D(roadAngle);var across=new float2(-outward.y,outward.x);
                    int treatment=(configuration>>(3*i))&7;
                    float median=(treatment&1)!=0?3.5f:0;
                    float left=8.5f+((treatment&2)!=0?2:0)+median*.25f;
                    float right=7.5f+((treatment&4)!=0?2:0)+median*.75f;
                    var mouth=new float3(outward.x*depth,100,outward.y*depth);
                    var lanes=new List<float[]>();
                    for(int lane=0;lane<2;lane++)lanes.Add(A(mouth+new float3(across.x,0,across.y)*(1.5f+3*lane+median*.25f)));
                    b.Add(new WireBank229{left=A(mouth+new float3(across.x,0,across.y)*left),right=A(mouth-new float3(across.x,0,across.y)*right),outward=new[]{outward.x,outward.y},lanes=lanes.ToArray()});
                    double cornerAngle=roadAngle+Math.PI+(legs==3?Math.PI/4:Math.PI/legs);
                    var corner=D(cornerAngle)*extent;p.Add(new float3(corner.x,100,corner.y));
                }
                if(legs==3){var corner=D(angle+3*Math.PI/4)*extent;p.Add(new float3(corner.x,100,corner.y));}
                for(int pass=0;pass<20;pass++)
                {
                    float angle2=pass*math.PI/10,mirror=pass%2==0?1:-1;
                    Func<float3,float3> P=x=>new float3(mirror*x.x*math.cos(angle2)-x.z*math.sin(angle2),x.y,mirror*x.x*math.sin(angle2)+x.z*math.cos(angle2));
                    var transformedPoints=p.Select(P).ToList();
                    var transformedBanks=b.Select(bank=>{var direction=P(new float3(bank.outward[0],0,bank.outward[1]));return new WireBank229{left=A(P(V(bank.left))),right=A(P(V(bank.right))),outward=new[]{direction.x,direction.z},lanes=bank.lanes.Select(l=>A(P(V(l)))).ToArray()};}).ToList();
                for(int incomingMask=1;incomingMask<(1<<legs);incomingMask++)
                {
                    var flowBanks=transformedBanks.Select((bank,i)=>new WireBank229{left=bank.left,right=bank.right,outward=bank.outward,
                        lanes=(incomingMask&(1<<i))!=0?bank.lanes:new float[0][]}).ToArray();
                    var roads=Roads(flowBanks);var sources=Enumerable.Range(0,legs).Where(i=>(incomingMask&(1<<i))!=0).ToList();
                    var proposed=sources.Select(i=>transformedPoints[i]).ToList();
                    foreach(byte style in new byte[]{0,1,2,3,4,5})
                    {
                        bool wire=style==1||style==3;bool ok;List<float3> fitted;
                        if(wire){var args=new object[]{proposed,roads,sources,style,null};ok=(bool)Call("TryFit",args);fitted=ok?(List<float3>)args[4]:null;}
                        else
                        {
                            int origin=sources[pass%sources.Count];var bank=flowBanks[origin];var native=new Game.Objects.Transform(V(bank.right)+math.normalizesafe(V(bank.right)-V(bank.left))*.6f,quaternion.identity);
                            var args=style==5?new object[]{native,roads,null}:new object[]{native,roads,origin,new List<float3>(),style,null};
                            ok=(bool)(style==5?move:armMount).Invoke(null,args);fitted=ok?new List<float3>{((Game.Objects.Transform)args[args.Length-1]).m_Position}:null;
                        }
                        cases++;
                        if(wire)Check(ok,"Incoming subset rejected legs="+legs+" config="+configuration+" mask="+incomingMask+" style="+style+" pass="+pass);
                        if(!ok){guarded++;rejected++;continue;}
                        if(wire){Check((bool)Call("Valid",fitted,roads,sources,style),"Invalid incoming subset");Check(fitted.Count==(legs==3?4:legs),"Missing physical support");}
                        foreach(var point in fitted)Check(OutsidePhysicalRoad(point,flowBanks),"Pole base entered outward roadway");
                        accepted++;fits++;
                        Check(proposed.SequenceEqual(sources.Select(i=>transformedPoints[i])),"Input changed");
                    }
                }
            }
                }
            counts.Add(new{legs,combinations,styles=2,accepted,rejected});
        }
        File.WriteAllText("WireCorner229-OneWayAllModels20.json",new JavaScriptSerializer().Serialize(new{cases,checks,fits,guarded,shiftChecks,counts,scope="20 distinct rotations/reflections for every nonempty incoming-arm subset and binary treatment on synthetic T/four-arm banks, 65 six-arm treatment samples; all five custom models and plain Far Side. Native generation/turning curves not exercised. Non-wire rejection remains unresolved, never successful fallback."}));
        Console.WriteLine("PASS: "+cases+" geometry/treatment cases, "+checks+" assertions; "+fits+" fits, "+guarded+" guarded, "+shiftChecks+" coherent shifts. No native ECS or performance claim.");
    }
}
