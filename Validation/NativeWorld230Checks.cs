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
class NativeWorld230Checks
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Assembly assembly;static Type approach,policy;static int checks;static float3 junction;
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
            approach.GetField("JunctionCentre",F).SetValue(road,junction);approach.GetField("HasJunctionCentre",F).SetValue(road,true);
            approach.GetField("Outward",F).SetValue(road,new float2(b.outward[0],b.outward[1]));
            approach.GetField("Incoming",F).SetValue(road,b.lanes.Select(V).ToList());
            approach.GetField("LaneMasks",F).SetValue(road,b.lanes.Select(_=>(ushort)1).ToList());result.Add(road);
        }
        return result;
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
        var counts=new List<object>();
        foreach(int legs in new[]{3,4,5,6})
        {
            int accepted=0,rejected=0;int combinations=64;
            for(int configuration=0;configuration<combinations;configuration++)
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
                for(int world=0;world<20;world++)
                {
                    float location=new[]{0f,350,1300,9000,12000,16000,-9000,-12000,-16000,20000}[world%10];
                    var shift=new float3(location,world%3*.2f,-location*.7f);junction=shift;
                    var translated=b.Select(x=>new WireBank229{left=A(V(x.left)+shift),right=A(V(x.right)+shift),outward=x.outward,lanes=x.lanes.Select(l=>A(V(l)+shift)).ToArray()}).ToArray();
                    var original=p; p=p.Select(x=>x+shift).ToList();
                var roads=Roads(translated);var sources=Enumerable.Range(0,legs).ToList();
                foreach(byte style in new byte[]{1,3})
                {
                var args=new object[]{p,roads,sources,style,null};bool ok=(bool)Call("TryFit",args);cases++;Check(ok,"Five-way fit rejected configuration="+configuration+" style="+style);
                if(ok)
                {
                    var fitted=(List<float3>)args[4];Check((bool)Call("Valid",fitted,roads,sources,style),"Matrix accepted invalid "+legs+"/"+configuration);
                    Check(fitted.Count==p.Count,"Unexpected support creation");accepted++;fits++;
                    if(configuration%257==0)
                    {
                        var warped=new List<float3>(fitted);warped[1]=warped[0];
                        Check(!(bool)Call("Valid",warped,roads,sources,style),"Duplicate corner passed matrix gate");
                        float3 centre=float3.zero;foreach(var point in fitted)centre+=point;centre/=fitted.Count;
                        for(int support=0;support<fitted.Count;support++)
                        {
                            var away=math.normalizesafe(fitted[support].xz-centre.xz)*.1f;
                            var moved=(List<float3>)Call("Shift",fitted,roads,support,fitted[support]+new float3(away.x,0,away.y));
                            Check(moved!=null,"Shift failed in matrix");
                            Check((bool)Call("Valid",moved,roads,sources,style),"Shift broke matrix topology");shiftChecks++;
                        }
                    }
                }
                else{Check(args[4]==null,"Failed fit leaked partial plan");rejected++;guarded++;}
                }
                p=original;
            }
                }
            counts.Add(new{legs,combinations,styles=2,accepted,rejected});
        }
        File.WriteAllText("NativeWorld230Checks.json",new JavaScriptSerializer().Serialize(new{cases,checks,fits,guarded,shiftChecks,counts,scope="299,520 labelled binary median/left-grass/right-grass configurations x both wire styles across 3-6 approaches, with deterministic rotations and topology-dependent native-shaped mouth separation. Synthetic banks; not native road generation or all prefab combinations. Unpaired five-leg layouts deliberately retain native fallback."}));
        Console.WriteLine("PASS: "+cases+" geometry/treatment cases, "+checks+" assertions; "+fits+" fits, "+guarded+" guarded, "+shiftChecks+" coherent shifts. No native ECS or performance claim.");
    }
}
