using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;

class FortyIntersectionChecks214
{
    static readonly Assembly Mod=Assembly.LoadFrom("MedianGrassReplacement.dll");
    static readonly Type Policy=Mod.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
    static readonly Type Approach=Policy.GetNestedType("Approach",BindingFlags.NonPublic);
    static readonly MethodInfo Arm=Policy.GetMethod("TryArmMountFrom",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo Wire=Policy.GetMethod("TrySignalMountFrom",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo Far=Policy.GetMethod("IsFarSide",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly string[] Names={"equal-width","mixed-width","asymmetric-lanes","outgoing-one-way-leg","protected-lane-bank","wide-median","offset-mouths","sloping-mouths","acute-skew","invalid-direction"};
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static float3 Get(object road,string name)=>(float3)Approach.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(road);
    static float2 Direction(object road)=>(float2)Approach.GetField("Outward",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(road);
    static List<float3> Lanes(object road)=>(List<float3>)Approach.GetField("Incoming",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(road);

    static IList Roads(int legs,int kind,int pass,out float3 centre)
    {
        var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));
        float angle=(pass/2)*36f*(float)Math.PI/180f;
        float mirror=pass%2==0?1:-1;
        var shift=new float3(pass>=10?1300f:0,pass%3*8f,pass>=10?-1700f:0);centre=shift;
        float3 P(float3 p){p.x*=mirror;return shift+new float3(p.x*(float)Math.Cos(angle)-p.z*(float)Math.Sin(angle),p.y,p.x*(float)Math.Sin(angle)+p.z*(float)Math.Cos(angle));}
        float2 D(float2 d)=>P(new float3(d.x,0,d.y)).xz-shift.xz;
        for(int i=0;i<legs;i++)
        {
            float degrees=legs==3?new[]{-90f,0f,180f}[i]:-90f+i*360f/legs;
            if(kind==8)degrees+=i==1?24f:i==2?-8f:0f;
            float radians=degrees*(float)Math.PI/180f;
            var outward=new float2((float)Math.Cos(radians),(float)Math.Sin(radians));
            var lateral=new float2(-outward.y,outward.x);
            float half=kind==1?3f+(i%3)*6f:kind==5?14f:kind==4?12f:6f;
            float depth=kind==6?26f+(i%2)*7f:kind==1?18f+(i%3)*4f:20f;
            float offset=kind==6?(i%2==0?4f:-3f):0f;
            float height=kind==7?(i%2)*.75f:0;
            var mouth=outward*depth+lateral*offset;
            float3 Point(float2 p)=>new float3(p.x,height,p.y);
            var left=P(Point(mouth+lateral*half));var right=P(Point(mouth-lateral*half));
            int count=kind==4?4:kind==1?1+i%3:2;
            if(kind==3&&i==0)count=0;
            var incoming=new List<float3>();var masks=new List<ushort>();
            for(int lane=0;lane<count;lane++)
            {
                float across=kind==2?half*.2f+lane*1.5f:kind==5?3f+lane*3f:-(lane+.5f)*(2f*half/count)*.5f;
                incoming.Add(P(Point(mouth+lateral*across)));masks.Add((ushort)(1<<lane));
            }
            var item=Activator.CreateInstance(Approach);
            foreach(var kv in new Dictionary<string,object>{
                {"Left",left},{"Right",right},{"Outward",kind==9&&i==legs-1?float2.zero:D(outward)},
                {"Incoming",incoming},{"LaneMasks",masks},{"JunctionCentre",shift},{"HasJunctionCentre",true}})
                Approach.GetField(kv.Key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(item,kv.Value);
            roads.Add(item);
        }
        return roads;
    }

    // Independent geometric safety oracle: reject a pole in any outward road
    // strip, including its median. Does not call Curbside's IsRoadside method.
    static bool OutsideRoads(float3 point,IList roads)
    {
        if(!math.all(math.isfinite(point)))return false;
        foreach(var road in roads)
        {
            var outward=Direction(road);float length=math.length(outward);
            if(!math.isfinite(length)||length<.7f)return false;
            outward/=length;
            var left=Get(road,"Left").xz;var right=Get(road,"Right").xz;var middle=(left+right)/2;
            var lateral=new float2(-outward.y,outward.x);
            var delta=point.xz-middle;
            float a=math.dot(left-middle,lateral),b=math.dot(right-middle,lateral),x=math.dot(delta,lateral);
            if(math.dot(delta,outward)>=-.749f&&x>math.min(a,b)-.199f&&x<math.max(a,b)+.199f)return false;
        }
        return true;
    }

    static void Main()
    {
        var rows=new List<object>();int attempts=0,accepted=0,rejected=0,unsupportedOrigins=0;
        for(int legs=3;legs<=6;legs++)for(int kind=0;kind<10;kind++)
        {
            int caseAccepted=0,caseRejected=0,caseAttempts=0,empty=0;
            for(int pass=0;pass<20;pass++)
            {
                var roads=Roads(legs,kind,pass,out var centre);
                for(int origin=0;origin<legs;origin++)
                {
                    var lanes=Lanes(roads[origin]);if(lanes.Count==0){empty++;continue;}
                    var side=Get(roads[origin],"Right");var other=Get(roads[origin],"Left");
                    var source=new Transform{m_Position=side+(side-other)*.02f,m_Rotation=quaternion.identity};
                    foreach(bool wire in new[]{false,true})
                    {
                        attempts++;caseAttempts++;
                        object[] args=wire?new object[]{source,roads,origin,null}:new object[]{source,roads,origin,new List<float3>(),null};
                        bool fitted=(bool)(wire?Wire:Arm).Invoke(null,args);
                        var placed=fitted?(Transform)args[wire?3:4]:source;
                        bool valid=fitted&&(bool)Far.Invoke(null,new object[]{placed.m_Position,roads,origin});
                        if(!valid){rejected++;caseRejected++;continue;}
                        accepted++;caseAccepted++;
                        Check(OutsideRoads(placed.m_Position,roads),"Support entered a road/median: "+legs+" "+kind+" "+pass+" "+origin);
                        Check(math.all(math.isfinite(placed.m_Position))&&math.all(math.isfinite(placed.m_Rotation.value)),"Nonfinite pose");
                        var forward=-math.normalize(Direction(roads[origin]));
                        Check(math.dot(placed.m_Position.xz-centre.xz,forward)>=.249f,"Near-side support accepted");
                        Check(kind!=9,"Malformed road direction accepted");
                        if(!wire)
                        {
                            object[] occupied={source,roads,origin,new List<float3>{placed.m_Position},null};
                            if((bool)Arm.Invoke(null,occupied))
                                Check(math.distancesq(((Transform)occupied[4]).m_Position.xz,placed.m_Position.xz)>=1.5624f,"Occupied support reused");
                        }
                    }
                }
            }
            if(caseAccepted==0)unsupportedOrigins++;
            rows.Add(new { id=rows.Count+1,legs,configuration=Names[kind],transformedPasses=20,attempts=caseAttempts,
                accepted=caseAccepted,conservativelyRejected=caseRejected,outgoingMouthsSkipped=empty,
                interpretation=kind==9?"invalid-input negative control":caseAccepted==0?"no supported placement in this synthetic fixture; not a compatibility success":"accepted placements satisfy geometric safety invariants",
                relatedModRoutes=new[]{"TTE/native phase evidence checked separately","Move It: transformed coordinates","Node Controller: offset/skew geometry","Road Builder: mixed width/median/lane envelope","Town/Traffic: producer schedule replay","other signal stylers: runtime coexistence still pending"}});
        }
        Check(rows.Count==40,"Expected forty distinct topology/configuration cases");
        File.WriteAllText("Forty-Intersection-Results214.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new{
            passed=true,configurations=40,transformsPerConfiguration=20,attempts,accepted,conservativelyRejected=rejected,noAcceptedConfigurations=unsupportedOrigins,checks,results=rows,
            limits="Compiled Curbside geometry policies over synthetic native-shaped approach data, not instantiated vanilla prefabs, actual mod DLL execution, terrain grounding, pedestrian-path attachment, native ECS/rendering or every possible intersection. Rejection is reported, not counted as successful visual coverage."}));
        Console.WriteLine("PASS: 40 distinct 3/4/5/6-leg configurations x20 geometric transforms; "+attempts+" arm/wire proposals, "+accepted+" accepted safety-checked, "+rejected+" conservatively rejected. "+checks+" invariants. Native game/mod/terrain/preview/save/FPS validation pending.");
    }
}
