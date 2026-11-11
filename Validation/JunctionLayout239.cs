using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;
using Unity.Mathematics;
using Colossal.Mathematics;
using Curbside.CornerStudy;
using Transform = Game.Objects.Transform;

class Slice { public int roadIndex,sideIndex; public float[][] curb,back; public float[] Outside; }
class Road { public float[] left,right,outward; public float[][] lanes; }
class Actor { public string actor,prefab; public float[] original; public int source,mask; }
class Scene { public string node; public float[] centre; public Slice[] slices; public Road[] roads; public Actor[] props; }
class Capture { public Scene[] nodes; }
class JunctionLayout239
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic, F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Type far,garden,approach,planner;
    static int assertions;
    static object Call(Type t,string name,params object[] args)=>t.GetMethod(name,S).Invoke(null,args);
    static void Set(object value,string name,object field)=>approach.GetField(name,F).SetValue(value,field);
    static float3 V(float[] v)=>new float3(v[0],v[1],v[2]);
    static float[] A(float3 p)=>new[]{p.x,p.y,p.z};
    static void Check(bool condition,string why){assertions++;if(!condition)throw new Exception(why);}
    static int[] Assign(bool[,] allowed)
    {
        int n=allowed.GetLength(0),m=allowed.GetLength(1);var reachable=new bool[1<<m];
        var previous=new int[1<<m];var corner=new int[1<<m];reachable[0]=true;
        for(int mask=0;mask<reachable.Length;mask++)
        {
            int source=math.countbits((uint)mask);if(!reachable[mask]||source>=n)continue;
            for(int c=0;c<m;c++)if((mask&(1<<c))==0&&allowed[source,c])
            {int next=mask|(1<<c);if(!reachable[next]){reachable[next]=true;previous[next]=mask;corner[next]=c;}}
        }
        int finish=-1;
        for(int mask=0;mask<reachable.Length;mask++)if(reachable[mask]&&math.countbits((uint)mask)==n){finish=mask;break;}
        if(finish<0)return null;var result=new int[n];
        for(int i=n-1;i>=0;i--){result[i]=corner[finish];finish=previous[finish];}return result;
    }
    static List<float3> Points(Bezier4x3 b,bool reverse)
    {var p=new List<float3>();for(int i=0;i<=16;i++)p.Add(MathUtils.Position(b,reverse?1-i/16f:i/16f));return p;}
    static bool InStrip(float3 point,CornerProposal corner,IList<CurbSlice> slices,float radius)
    {
        var a=slices.Single(s=>s.Road==corner.RoadA&&s.Side==corner.SideA);
        var b=slices.Single(s=>s.Road==corner.RoadB&&s.Side==corner.SideB);
        var polygon=Points(a.Curb,false);polygon.AddRange(Points(b.Curb,true));
        polygon.AddRange(Points(b.Back,false));polygon.AddRange(Points(a.Back,true));
        return (bool)typeof(CornerRule).GetMethod("Contains",S).Invoke(null,new object[]{polygon,point.xz,radius});
    }
    static void Main(string[] args)
    {
        var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
        var capture=json.Deserialize<Capture>(File.ReadAllText(args[0]));
        var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");
        far=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
        garden=asm.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);
        planner=asm.GetType("MedianGrassReplacement.Systems.WireLayoutPlanner",true);
        approach=far.GetNestedType("Approach",BindingFlags.NonPublic);
        asm.GetType("GardenCitySignals.SignalPackPolicy",true).GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        asm.GetType("GardenCitySignals.SignalPackPolicy",true).GetProperty("UseLowerRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        var outcomes=new List<object>();
        int wirePlans=0,wireAccepted=0,armPlans=0,armAccepted=0,armOutsideStrip=0,sharedCornerBanks=0,sharedCornerMissing=0;
        int simultaneousArmPlans=0,simultaneousArmAccepted=0;
        foreach(var scene in capture.nodes.Where(n=>n.slices.Length==n.roads.Length*2&&n.slices.Length>0))
        for(int pass=0;pass<20;pass++)
        {
            float theta=pass*math.PI/10;var offset=pass<10?float3.zero:new float3(6000,0,-7000);
            Func<float3,float3> P=p=>offset+new float3(p.x*math.cos(theta)-p.z*math.sin(theta),p.y,p.x*math.sin(theta)+p.z*math.cos(theta));
            Func<float2,float2> D=p=>new float2(p.x*math.cos(theta)-p.y*math.sin(theta),p.x*math.sin(theta)+p.y*math.cos(theta));
            Func<float[][],Bezier4x3> B=c=>new Bezier4x3(P(V(c[0])),P(V(c[1])),P(V(c[2])),P(V(c[3])));
            var slices=scene.slices.Select(s=>new CurbSlice{Road=s.roadIndex,Side=s.sideIndex,Kind=BoundaryKind.Curb,Curb=B(s.curb),Back=B(s.back),HasBack=true,Outside=P(V(s.Outside))-offset}).ToList();
            var corners=CornerRule.Calculate(slices,.7238954f,null,1.5f);
            Check(corners.Count==scene.roads.Length,"Captured topology did not pair native corners");
            foreach(var c in corners)Check(c.Fits&&c.Clearance>=1.495f,"Captured corner containment/setback failed");
            var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
            foreach(var r in scene.roads)
            {
                var a=Activator.CreateInstance(approach);Set(a,"Left",P(V(r.left)));Set(a,"Right",P(V(r.right)));
                Set(a,"Outward",D(new float2(r.outward[0],r.outward[1])));Set(a,"Incoming",r.lanes.Select(p=>P(V(p))).ToList());
                Set(a,"HasJunctionCentre",true);Set(a,"JunctionCentre",P(V(scene.centre)));
                Set(a,"WireBank",r.lanes.Select(p=>P(V(p))).ToList());Set(a,"WireCurbs",slices.Select(s=>s.Curb).ToList());roads.Add(a);
            }
            // One captured vehicle actor per source bank, preferring identifiable
            // native right-hand main heads. Customized prefab names do not retain
            // native light-type metadata in this snapshot. This tests policies,
            // not the ECS primary-actor/support-sharing selection pipeline.
            var leaders=scene.props.Where(p=>p.mask!=0&&p.source>=0)
                .GroupBy(p=>p.source).OrderBy(g=>g.Key)
                .Select(g=>g.FirstOrDefault(p=>p.prefab.Contains("CarCrosswalkRight"))??g.First()).ToArray();
            Check(leaders.Length==scene.roads.Length,"Missing captured vehicle source bank");
            foreach(byte style in new byte[]{0,1,2,3,4})
            {
                bool wire=style==1||style==3;
                if(wire)
                {
                    wirePlans++;
                    var call=new object[]{corners,roads,leaders.Select(p=>p.source).ToList(),leaders.Select(p=>P(V(p.original))).ToList(),style,null,null,null};
                    bool ok=(bool)Call(planner,"Build",call);if(ok)wireAccepted++;
                    if(pass==0)outcomes.Add(new{scene.node,style,path="existing complete wire Build at centre corner station",accepted=ok,reason=call[7]});
                    continue;
                }
                var occupied=new List<float3>();
                var allowed=new bool[leaders.Length,corners.Count];
                for(int bank=0;bank<leaders.Length;bank++)
                {
                    var actor=leaders[bank];
                    armPlans++;
                    var original=new Transform(P(V(actor.original)),quaternion.identity);
                    var call=new object[]{original,roads,actor.source,occupied,null};
                    bool fitted=(bool)Call(far,"TryArmMountFrom",call);
                    Transform placed=fitted?(Transform)call[4]:original;
                    bool contained=fitted&&corners.Any(c=>InStrip(placed.m_Position,c,slices,.41f));
                    if(fitted){armAccepted++;occupied.Add(placed.m_Position);if(!contained)armOutsideStrip++;}
                    // Offline candidate experiment: use already fitted physical
                    // corners, then apply unchanged lane/head eligibility.
                    var candidates=new List<int>();
                    for(int c=0;c<corners.Count;c++)
                    {
                        bool across=(bool)Call(planner,"Across",corners[c].Position,roads,actor.source);
                        bool appearance=(bool)Call(garden,"AppearanceCandidate",roads[actor.source],corners[c].Position,style);
                        if(across&&appearance){candidates.Add(c);allowed[bank,c]=true;}
                    }
                    sharedCornerBanks++;if(candidates.Count==0)sharedCornerMissing++;
                    if(pass==0)outcomes.Add(new{scene.node,style,path="existing TryArmMountFrom before native/curb adjustment",actor.actor,source=actor.source,accepted=fitted,position=A(placed.m_Position),baseFitsCapturedCornerStrip=contained,commonCornerCandidates=candidates.ToArray()});
                }
                simultaneousArmPlans++;var assignment=Assign(allowed);
                var armCall=new object[]{corners,roads,leaders.Select(p=>p.source).ToList(),style,null};
                bool armPlan=(bool)Call(asm.GetType("MedianGrassReplacement.Systems.ArmCornerLayoutPlanner",true),"Build",armCall);
                Check(armPlan==(assignment!=null),"Compiled arm planner differs from candidate matrix matching");
                if(assignment!=null)
                {
                    simultaneousArmAccepted++;
                    Check(assignment.Distinct().Count()==assignment.Length,"Shared corner assignment duplicates a physical pole");
                    for(int i=0;i<assignment.Length;i++)Check(allowed[i,assignment[i]],"Shared corner assignment loses a served bank");
                }
                if(pass==0)outcomes.Add(new{scene.node,style,path="offline simultaneous shared-corner experiment",accepted=assignment!=null,assignment});
            }
        }
        File.WriteAllText(args[1],json.Serialize(new{assertions,wirePlans,wireAccepted,armPlans,armAccepted,armOutsideStrip,sharedCornerBanks,sharedCornerMissing,simultaneousArmPlans,simultaneousArmAccepted,outcomes,
            scope="Offline Test239 compiled candidate policies; baseline Test238 remains installed. Four captured junctions, twenty rotations/translations, all five styles. Arm replay stops before native-mount adjustment, grounding, shared-pole merging and actual art composition; it identifies missing safety checks in this intermediate path, not final in-game placements. Compiled common-corner arm planner tested alongside independent matching; no deployment. No native World, full vehicle Bezier paths, physics, saves or FPS executed."}));
        Console.WriteLine("AUDIT: "+assertions+" assertions; wire "+wireAccepted+"/"+wirePlans+" complete centre-station plans; arm "+armAccepted+"/"+armPlans+" intermediate mounts, "+armOutsideStrip+" outside captured corner strips; simultaneous common-corner arm plans "+simultaneousArmAccepted+"/"+simultaneousArmPlans);
    }
}
