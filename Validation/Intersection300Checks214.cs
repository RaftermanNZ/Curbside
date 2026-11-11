using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Unity.Entities;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;

class Intersection300Checks214
{
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly BindingFlags Methods=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly Assembly Mod=Assembly.LoadFrom("MedianGrassReplacement.dll");
    static readonly Type Far=Mod.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
    static readonly Type Road=Far.GetNestedType("Approach",BindingFlags.NonPublic);
    static readonly Type Garden=Mod.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);
    static readonly Type Ped=Mod.GetType("MedianGrassReplacement.Systems.PlainCrossingPlacementPolicy",true);
    static readonly Type Crossing=Ped.GetNestedType("Crossing",BindingFlags.NonPublic),Support=Ped.GetNestedType("Support",BindingFlags.NonPublic);
    static readonly MethodInfo Move=Far.GetMethod("TryMove",Methods),Arm=Far.GetMethod("TryArmMountFrom",Methods),Wire=Far.GetMethod("TrySignalMountFrom",Methods),FarSide=Far.GetMethod("IsFarSide",Methods),Ground=Far.GetMethod("TryGround",Methods),Plan=Ped.GetMethod("TryPlan",Methods);
    static void Set(object x,string name,object value)=>x.GetType().GetField(name,Fields).SetValue(x,value);
    static T Get<T>(object x,string name)=>(T)x.GetType().GetField(name,Fields).GetValue(x);
    static IList List(Type item)=>(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
    static Entity Id(int index)=>new Entity{Index=index,Version=3};
    static float3 Point(float2 p,float height)=>new float3(p.x,height,p.y);
    static string Pose(Transform x)=>string.Join(",",new[]{x.m_Position.x,x.m_Position.y,x.m_Position.z,x.m_Rotation.value.x,x.m_Rotation.value.y,x.m_Rotation.value.z,x.m_Rotation.value.w}.Map(v=>v.ToString("R",CultureInfo.InvariantCulture)));
    static bool Outside(float3 p,IList roads,int ignore=-1)
    {
        if(!math.all(math.isfinite(p)))return false;
        foreach(var road in roads)
        {
            if(roads.IndexOf(road)==ignore)continue;
            var o=math.normalizesafe(Get<float2>(road,"Outward"));var l=Get<float3>(road,"Left").xz;var r=Get<float3>(road,"Right").xz;
            var mid=(l+r)*.5f;var side=new float2(-o.y,o.x);float a=math.dot(l-mid,side),b=math.dot(r-mid,side),s=math.dot(p.xz-mid,side);
            if(math.dot(p.xz-mid,o)>=-.749f&&s>math.min(a,b)-.199f&&s<math.max(a,b)+.199f)return false;
        }
        return true;
    }
    static IList Roads(int legs,int shape,int width,int flow,int id,out List<float3> surface,out IList crossings)
    {
        var roads=List(Road);crossings=List(Crossing);surface=new List<float3>();
        float mirror=id%2==0?1:-1,turn=(id%13)*.37f;
        var centre=new float3((id%3)*1700,(id%4)*6,-(id%5)*1300);
        float3 P(float3 v){v.x*=mirror;return centre+new float3(v.x*(float)Math.Cos(turn)-v.z*(float)Math.Sin(turn),v.y,v.x*(float)Math.Sin(turn)+v.z*(float)Math.Cos(turn));}
        float2 D(float2 v)=>P(Point(v,0)).xz-centre.xz;
        var angles=new float[legs];float minimumAngle=360;
        for(int edge=0;edge<legs;edge++)
        {
            float angle=legs==3?(shape==1?new[]{-90f,30f,150f}[edge]:new[]{-90f,0f,180f}[edge]):-90f+edge*360f/legs;
            if(shape==2)angle+=edge%2==0?7f:-11f;
            if(shape==4)angle+=edge==1?22f:edge==2?-9f:0;
            angles[edge]=angle;
        }
        for(int a=0;a<legs;a++)for(int b=a+1;b<legs;b++)
        {
            float gap=math.abs(angles[a]-angles[b])%360;minimumAngle=math.min(minimumAngle,math.min(gap,360-gap));
        }
        for(int edge=0;edge<legs;edge++)
        {
            float angle=angles[edge];
            float radians=angle*(float)Math.PI/180;
            var outward=new float2((float)Math.Cos(radians),(float)Math.Sin(radians));var lateral=new float2(-outward.y,outward.x);
            float half=width==0?4:width==1?6:width==2?10:width==3?15:new[]{3f,6f,10f,14f,5f,18f}[edge];
            float maxHalf=width==4?18:half;
            // Native geometry supplies real mouths. Synthetic inputs must not
            // reuse regular-polygon spacing after skewing the road angles:
            // that put one road's supposed signal bank inside another road.
            float depth=math.max(18,maxHalf/(float)Math.Tan(minimumAngle*Math.PI/360)+6)+(shape==3?edge%2*6:0);
            var mouth=outward*depth+lateral*(shape==3?(edge%2==0?3:-4):0);
            float height=shape==2?edge*.18f:0;
            var left=P(Point(mouth+lateral*half,height));var right=P(Point(mouth-lateral*half,height));
            int count=math.min(4,math.max(1,(int)(half/3)));if(flow==1)count=math.max(1,count-edge%2);if(flow==2&&edge==0)count=0;
            var lanes=new List<float3>();var masks=new List<ushort>();
            for(int lane=0;lane<count;lane++)
            {
                float offset=width==3?3.5f+lane*2.6f:.8f+lane*2.6f;
                if(flow==1)offset=math.min(half-1,offset+1.1f);
                lanes.Add(P(Point(mouth-lateral*offset,height)));masks.Add((ushort)(1<<lane));
            }
            var road=Activator.CreateInstance(Road);
            ushort all=0;foreach(ushort mask in masks)all|=mask;
            foreach(var item in new Dictionary<string,object>{{"Left",left},{"Right",right},{"Outward",D(outward)},{"Incoming",lanes},{"LaneMasks",masks},{"VehicleMask",all},{"Road",Id(edge+10)},{"JunctionCentre",centre},{"HasJunctionCentre",true}})Set(road,item.Key,item.Value);
            roads.Add(road);surface.Add(left);surface.Add(right);
            var crossing=Activator.CreateInstance(Crossing);Set(crossing,"A",left);Set(crossing,"B",right);Set(crossing,"FacingA",math.normalizesafe(right.xz-left.xz));Set(crossing,"FacingB",math.normalizesafe(left.xz-right.xz));Set(crossing,"Mask",(ushort)(1<<edge));Set(crossing,"Lane",Id(100+edge));crossings.Add(crossing);
        }
        return roads;
    }
    static void Main(string[] args)
    {
        bool observe=Array.IndexOf(args,"--observe")>=0;
        var rows=new List<object>();var hazards=new List<object>();var evidence=new StringBuilder();
        int proposals=0,accepted=0,pedProposals=0,pedAccepted=0;
        for(int legs=3;legs<=6;legs++)for(int shape=0;shape<5;shape++)for(int width=0;width<5;width++)for(int flow=0;flow<3;flow++)
        {
            int id=rows.Count+1;var roads=Roads(legs,shape,width,flow,id,out var surface,out var crossings);
            var modeRows=new List<object>();
            for(int mode=0;mode<5;mode++)
            {
                string name=mode==0?"plain-vanilla-far-side":new[]{"garden","timber-wire","rigid-arm","metal-wire"}[mode-1];
                var supports=List(Support);int tried=0,placedCount=0,skipped=0;
                foreach(var road in roads)
                {
                    int origin=roads.IndexOf(road);if(Get<List<float3>>(road,"Incoming").Count==0){skipped++;continue;}
                    proposals++;tried++;
                    var a=Get<float3>(road,"Left");var b=Get<float3>(road,"Right");
                    var source=new Transform{m_Position=b+(b-a)*.02f,m_Rotation=quaternion.LookRotationSafe(Point(Get<float2>(road,"Outward"),0),new float3(0,1,0))};
                    if(!Outside(source.m_Position,roads,origin))hazards.Add(new{id,mode=name,origin,reason="fixture source bank overlaps another road",pose=Pose(source)});
                    int bound=mode==0?origin:(int)Garden.GetMethod("NearestForPhase",Methods).Invoke(null,new object[]{source,roads,Get<ushort>(road,"VehicleMask")});
                    if(mode>0&&bound!=origin)hazards.Add(new{id,mode=name,origin,bound,sourceOutsideRoadEnvelopes=Outside(source.m_Position,roads),sourceOutsideOtherRoads=Outside(source.m_Position,roads,origin),reason="phase-based source approach changed",pose=Pose(source)});
                    bool wire=mode>0&&(bool)Garden.GetMethod("IsWireStyle",Methods).Invoke(null,new object[]{(byte)(mode-1)});
                    object[] call=mode==0?new object[]{source,roads,null}:wire?new object[]{source,roads,bound,null}:new object[]{source,roads,bound,new List<float3>(),null};
                    bool fit=(bool)(mode==0?Move:wire?Wire:Arm).Invoke(null,call);
                    var placed=fit?(Transform)call[mode==0?2:wire?3:4]:source;
                    if(mode>0)fit=fit&&(bool)FarSide.Invoke(null,new object[]{placed.m_Position,roads,origin});
                    if(fit)
                    {
                        object[] ground={source.m_Position,placed.m_Position,surface,0f};
                        fit=(bool)Ground.Invoke(null,ground);if(fit)placed.m_Position.y=(float)ground[3];
                    }
                    evidence.Append(id).Append('/').Append(mode).Append('/').Append(origin).Append('/').Append(fit).Append('/').Append(fit?Pose(placed):"-").Append(';');
                    if(!fit)continue;
                    accepted++;placedCount++;
                    if(mode==0&&!placed.m_Rotation.Equals(source.m_Rotation))hazards.Add(new{id,mode=name,origin,reason="plain native model rotation changed",pose=Pose(placed)});
                    if(!Outside(placed.m_Position,roads))hazards.Add(new{id,mode=name,origin,reason="vehicle support in outward road/median envelope",pose=Pose(placed)});
                    var support=Activator.CreateInstance(Support);Set(support,"Actor",Id(200+origin));Set(support,"Position",placed.m_Position);Set(support,"Radius",.25f);supports.Add(support);
                }
                int pedestrianCount=0;
                for(int origin=0;origin<legs;origin++)
                {
                    pedProposals++;
                    var crossing=crossings[origin];var a=Get<float3>(crossing,"A");var b=Get<float3>(crossing,"B");var facing=Get<float2>(crossing,"FacingA");
                    var original=new Transform{m_Position=a+(a-b)*.03f,m_Rotation=quaternion.LookRotationSafe(Point(facing,0),new float3(0,1,0))};
                    object[] plan={original,(ushort)(1<<origin),crossings,supports,.2f,null};
                    bool fit=(bool)Plan.Invoke(null,plan);
                    if(fit)
                    {
                        var pose=Get<Transform>(plan[5],"Pose");
                        var chosen=Get<Entity>(plan[5],"Crossing");var support=Get<Entity>(plan[5],"Support");
                        object[] ground={original.m_Position,pose.m_Position,surface,0f};fit=(bool)Ground.Invoke(null,ground);
                        if(fit)
                        {
                            pose.m_Position.y=(float)ground[3];pedAccepted++;pedestrianCount++;
                            if(!Outside(pose.m_Position,roads))hazards.Add(new{id,mode=name,origin,reason="pedestrian offset entered another road/median envelope",pose=Pose(pose)});
                            if(chosen!=Id(100+origin))hazards.Add(new{id,mode=name,origin,reason="pedestrian matched a different crossing",pose=Pose(pose)});
                            evidence.Append("ped/").Append(id).Append('/').Append(mode).Append('/').Append(origin).Append('/').Append(Pose(pose)).Append('/').Append(chosen.Index).Append('/').Append(support.Index).Append(';');
                        }
                    }
                    if(!fit)evidence.Append("ped-rejected/").Append(id).Append('/').Append(mode).Append('/').Append(origin).Append(';');
                }
                modeRows.Add(new{mode=name,vehicleProposals=tried,vehicleAccepted=placedCount,outgoingMouthsSkipped=skipped,pedestrianAccepted=pedestrianCount});
            }
            rows.Add(new{id,legs,shape,widthProfile=width,flowProfile=flow,reflected=id%2!=0,modes=modeRows});
        }
        string fingerprint;using(var sha=SHA256.Create())fingerprint=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(evidence.ToString()))).Replace("-","");
        var result=new{passed=hazards.Count==0,scenarios=rows.Count,proposals,accepted,pedestrianProposals=pedProposals,pedestrianAccepted=pedAccepted,hazards,geometryFingerprint=fingerprint,results=rows,
            limits="300 synthetic 3-6-leg geometry configurations x five vehicle modes plus standalone pedestrian association. Executes compiled value methods and caller-equivalent grounding/rejection only. No actual mod DLLs, vanilla road prefabs, renderer, terrain jobs, native ECS, save files or FPS captures."};
        File.WriteAllText("Intersection-300-Results214.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(result));
        if(rows.Count!=300)throw new Exception("Expected 300 distinct configurations");
        Console.WriteLine("300 scenarios: "+proposals+" vehicle proposals, "+accepted+" accepted; "+pedProposals+" pedestrian proposals, "+pedAccepted+" accepted; hazards="+hazards.Count+"; fingerprint="+fingerprint);
        if(hazards.Count!=0&&!observe)throw new Exception("Geometric hazards require investigation before claiming this fixture passed");
    }
}
static class ScenarioArrayMap
{
    public static U[] Map<T,U>(this T[] values,Func<T,U> map){var result=new U[values.Length];for(int i=0;i<values.Length;i++)result[i]=map(values[i]);return result;}
}
