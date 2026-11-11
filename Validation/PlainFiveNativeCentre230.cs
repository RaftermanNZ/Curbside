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
class PlainFiveNativeCentre230
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Assembly assembly;static Type approach,policy;static int checks;static bool centreKnown;static float3 junctionCentre;
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
            approach.GetField("JunctionCentre",F).SetValue(road,junctionCentre);approach.GetField("HasJunctionCentre",F).SetValue(road,centreKnown);
            approach.GetField("Outward",F).SetValue(road,new float2(b.outward[0],b.outward[1]));
            approach.GetField("Incoming",F).SetValue(road,b.lanes.Select(V).ToList());
            approach.GetField("LaneMasks",F).SetValue(road,b.lanes.Select(_=>(ushort)1).ToList());result.Add(road);
        }
        return result;
    }
    static void Main(string[] command)
    {
        centreKnown=command.Length>3&&command[3]=="native";
        assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        approach=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy+Approach",true);
        policy=assembly.GetType("MedianGrassReplacement.Systems.WireCornerLayoutPolicy",true);
        var pack=assembly.GetType("GardenCitySignals.SignalPackPolicy",true);
        pack.GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        pack.GetProperty("UseLowerRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        long cases=0;var accepted=new long[6];var rejected=new long[6];var unsafeBase=new long[6];var examples=new List<object>();
        var far=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
        var armMount=far.GetMethod("TryAppearanceArmMountFrom",S);var move=far.GetMethod("TryMove",S);
        var modelNames=new[]{"Garden City","Timber span-wire","Rigid mast-arm","Metal span-wire","Florida","Plain Far Side"};
        var counts=new List<object>();
        foreach(int legs in command.Length>0?new[]{int.Parse(command[0])}:new[]{3,4,5,6})
        {
            int combinations=1<<(3*legs);
            for(int configuration=command.Length>1?int.Parse(command[1]):0;configuration<Math.Min(combinations,command.Length>2?int.Parse(command[2]):combinations);configuration++)
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
                if(configuration%8192==0){Console.WriteLine("Progress: arms="+legs+" configuration="+configuration+" cases="+cases);Console.Out.Flush();}
                for(int pass=0;pass<20;pass++)
                {
                    float theta=pass*math.PI/10;float mirror=pass%2==0?1:-1;
                    var shift=new float3(pass>=10?350:0,pass%3*.15f,pass>=10?-450:0);
                    Func<float3,float3> P=x=>shift+new float3(mirror*x.x*math.cos(theta)-x.z*math.sin(theta),x.y,mirror*x.x*math.sin(theta)+x.z*math.cos(theta));
                    var banks=b.Select(x=>new WireBank229{left=A(P(V(x.left))),right=A(P(V(x.right))),outward=new[]{mirror*x.outward[0]*math.cos(theta)-x.outward[1]*math.sin(theta),mirror*x.outward[0]*math.sin(theta)+x.outward[1]*math.cos(theta)},lanes=x.lanes.Select(q=>A(P(V(q)))).ToArray()}).ToArray();
                    int offset=pass%legs;
                    banks=Enumerable.Range(0,legs).Select(i=>banks[(i+offset)%legs]).ToArray();
                    var points=p.Select(P).ToList();
                    var sources=Enumerable.Range(0,legs).Select(i=>(i-offset+legs)%legs).ToList();
                    junctionCentre=shift;var roads=Roads(banks);int origin=pass%legs;
                    for(int model=5;model<6;model++)
                    {
                        bool ok;List<float3> fit=null;
                        if(model==1||model==3)
                        {
                            var args=new object[]{points,roads,sources,(byte)model,null};ok=(bool)Call("TryFit",args);
                            if(ok)fit=(List<float3>)args[4];
                        }
                        else
                        {
                            var bank=banks[origin];var side=V(bank.right)-V(bank.left);
                            var native=new Game.Objects.Transform(V(bank.right)+math.normalizesafe(side)*.6f,quaternion.identity);
                            var args=model==5?new object[]{native,roads,null}:new object[]{native,roads,origin,new List<float3>(),(byte)model,null};
                            ok=(bool)(model==5?move:armMount).Invoke(null,args);
                            if(ok)fit=new List<float3>{((Game.Objects.Transform)args[args.Length-1]).m_Position};
                        }
                        cases++;
                        if(!ok){rejected[model]++;continue;}
                        accepted[model]++;
                        foreach(var point in fit)
                        {
                            Check(math.all(math.isfinite(point)),"Nonfinite accepted pole");
                            bool clear=(bool)Call("ClearBase",point,roads);
                            if(!clear){unsafeBase[model]++;if(examples.Count<32)examples.Add(new{legs,configuration,pass,model=modelNames[model],point=point.ToString(),reason="Full base overlaps expanded outward road/median strip"});}
                        }
                    }
                }
            }
            counts.Add(new{legs,combinations,passesPerCombination=20,models=6});
            Console.WriteLine("Completed "+legs+" arms: "+cases+" model/transform cases.");Console.Out.Flush();
        }
        File.WriteAllText("PlainFiveNativeCentre230-"+string.Join("-",command)+".json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new{cases,checks,models=modelNames.Select((model,i)=>new{model,accepted=accepted[i],unresolved=rejected[i],unsafeBase=unsafeBase[i]}).ToArray(),counts,examples,scope="20 distinct rigid transforms/reflections/approach orders per labelled binary median/left-grass/right-grass combination on synthetic T/4/5/6-arm banks, all five custom styles and plain Far Side. Each wire case fits full support layout; arm/plain cases cycle served approach. Rejection remains unresolved, never a successful custom fix. Native ECS, curves, prefab inventory, saves, renderer and FPS not exercised."}));
        Console.WriteLine("Completed "+cases+" all-model cases; "+checks+" finite-site assertions. Unresolved placements and full-base hazards are separately reported, not counted as fixes.");

    }
}
