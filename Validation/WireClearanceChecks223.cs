using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Web.Script.Serialization;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;
using Flags = Game.Objects.GeometryFlags;

class CShape
{
    public string actor,name,flags,mask;
    public float[] boundsMin,boundsMax,size,legSize,legOffset,position,rotation;
    public bool unsupported;
    public ObjectGeometryData Geometry() => new ObjectGeometryData {m_Flags=(Flags)Enum.Parse(typeof(Flags),flags),
        m_Bounds=new Bounds3(V(boundsMin),V(boundsMax)),m_Size=V(size),m_LegSize=V(legSize),m_LegOffset=new float2(legOffset[0],legOffset[1])};
    public Transform Pose()=>new Transform(V(position),Q(rotation));
    public CollisionMask GetMask()=>(CollisionMask)Enum.Parse(typeof(CollisionMask),mask);
    public static float3 V(float[] a)=>new float3(a[0],a[1],a[2]);
    public static quaternion Q(float[] a)=>new quaternion(a[0],a[1],a[2],a[3]);
}
class CPart {public string name;public float[] boundsMin,boundsMax,position,rotation;}
class CActor {public CShape shape;public int support,approach;public CPart[] parts;public bool primary;}
class CBank {public float[] left,right,outward;public float[][] lanes;public ushort[] masks;}
class CCurve {public float[] a,b,c,d;public bool start;}
class CScene {public string time,node;public float[][] positions;public CActor[] actors;public CShape[] obstacles;public CBank[] banks;public CCurve[] curves;public float[] lampMin,lampMax;public int[] sourceApproaches;}
class CPair {public CShape applied,original,candidate;public bool ownerExcluded;}
class CCases {public CScene[] scenes;public CPair[] pairs;public string packageSha256;}
class MBounds {public float[] min,max;}
class MCatalog {public string packageSha256;public Dictionary<string,MBounds> meshes;}
class CBody {public ObjectGeometryData G;public Transform P;public CollisionMask M;}

class Core223
{
    const BindingFlags B=BindingFlags.Static|BindingFlags.NonPublic;
    readonly Type policy,shape,list,approach,far,garden,wire;
    readonly Dictionary<RenderPrefab,string> meshNames=new Dictionary<RenderPrefab,string>();
    public void LoadWireModules(MCatalog catalog)
    {
        var modules=(IDictionary)garden.GetField("modules",B).GetValue(null);
        foreach(var name in new[]{"WirePole","WireTimber","MetalWirePole","HeadWire","LowerHeadWire","PedHeadWire","SignPlate","WireLuminaire","MetalWireLuminaire"})
        {
            var b=catalog.meshes["CSG"+name+" Mesh"];
            var mesh=(RenderPrefab)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(RenderPrefab));
            mesh.bounds=new Bounds3(CShape.V(b.min),CShape.V(b.max));
            var module=(StaticObjectPrefab)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StaticObjectPrefab));
            module.m_Meshes=new[]{new ObjectMeshInfo{m_Mesh=mesh}};modules[name]=module;meshNames[mesh]="CSG"+name+" Mesh";
        }
    }
    public CPart[] ComposeCaptured(CScene scene,CActor actor,IList<float3> points,out quaternion rotation)
    {
        var b=scene.banks[actor.approach];var position=points[actor.support];
        rotation=actor.shape.Pose().m_Rotation;
        if(!Layout(b.lanes.Select(CShape.V).ToList(),new float2(b.outward[0],b.outward[1]),position,out var inward,out var heads))return null;
        var faceType=garden.GetNestedType("CrossingFace",BindingFlags.NonPublic);
        var faces=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(faceType));
        foreach(var part in actor.parts.Where(p=>p.name=="CSGPedHeadWire Mesh"))
        {
            var f=Activator.CreateInstance(faceType);faceType.GetField("Facing",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,math.rotate(math.mul(actor.shape.Pose().m_Rotation,CShape.Q(part.rotation)),new float3(0,0,1)));
            faceType.GetField("Mask",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,(ushort)1);faces.Add(f);
        }
        byte style=(byte)(actor.parts.Any(p=>p.name=="CSGMetalWirePole Mesh")?3:1);
        var args=new object[]{position,new float2(b.outward[0],b.outward[1]),inward,heads,style,points,-1,true,true,faces,null,null,null};
        if(!(bool)garden.GetMethod("ComposeWire",B).Invoke(null,args))return null;
        rotation=(quaternion)args[11];
        return ((List<ObjectMeshInfo>)args[10]).Select(p=>{var mesh=(RenderPrefab)p.m_Mesh;return new CPart{name=meshNames[mesh],boundsMin=new[]{mesh.bounds.min.x,mesh.bounds.min.y,mesh.bounds.min.z},boundsMax=new[]{mesh.bounds.max.x,mesh.bounds.max.y,mesh.bounds.max.z},position=new[]{p.m_Position.x,p.m_Position.y,p.m_Position.z},rotation=new[]{p.m_Rotation.value.x,p.m_Rotation.value.y,p.m_Rotation.value.z,p.m_Rotation.value.w}};}).ToArray();
    }
    public Core223()
    {
        var a=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MedianGrassReplacement.dll"));
        policy=a.GetType("MedianGrassReplacement.Systems.WireClearancePolicy",true);shape=a.GetType("MedianGrassReplacement.Systems.WireClearanceShape",true);
        list=typeof(List<>).MakeGenericType(shape);far=a.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
        approach=far.GetNestedType("Approach",BindingFlags.NonPublic);garden=a.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);
        wire=a.GetType("MedianGrassReplacement.Systems.SignalWirePolicy",true);
    }
    object S(CBody body)
    {
        object s=Activator.CreateInstance(shape);
        shape.GetField("Geometry",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s,body.G);
        shape.GetField("Pose",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s,body.P);
        shape.GetField("Mask",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s,body.M);return s;
    }
    object L(IList<CBody> bodies) {var l=(IList)Activator.CreateInstance(list);foreach(var b in bodies)l.Add(S(b));return l;}
    public bool Overlap(CBody a,CBody b)=>(bool)policy.GetMethod("Overlaps",B).Invoke(null,new[]{S(a),S(b)});
    public Bounds3 Bounds(Bounds3 b,float3 p,quaternion q,bool standing,float h)
        =>(Bounds3)policy.GetMethod("TransformPart",B).Invoke(null,new object[]{b,p,q,standing,h});
    public int Neighbour(IList<float3> p,int i,float3 inward)=>(int)garden.GetMethod("WireNeighbor",B).Invoke(null,new object[]{p,i,inward});
    public int2 LampPair(IList<float3> p)=>(int2)garden.GetMethod("LuminairePair",B).Invoke(null,new object[]{p});
    public quaternion LampRotation(float3 p,float3 centre)=>(quaternion)garden.GetMethod("InwardLampRotation",B).Invoke(null,new object[]{p,centre});
    public float Drop(float span,float t)=>(float)wire.GetMethod("Drop",B).Invoke(null,new object[]{span,t});
    public object Banks(CBank[] banks)
    {
        var l=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
        foreach(var b in banks)
        {var a=Activator.CreateInstance(approach);Action<string,object> set=(n,v)=>approach.GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(a,v);
         set("Left",CShape.V(b.left));set("Right",CShape.V(b.right));set("Outward",new float2(b.outward[0],b.outward[1]));
         set("Incoming",b.lanes.Select(CShape.V).ToList());if(b.masks!=null)set("LaneMasks",b.masks.ToList());
         set("JunctionCentre",float3.zero);set("HasJunctionCentre",false);l.Add(a);}return l;
    }
    public object Call(string type,string method,params object[] args)
        =>policy.Assembly.GetType(type,true).GetMethod(method,B|BindingFlags.Public).Invoke(null,args);
    public T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);
    public void Centre(object banks,float3 centre)
    {var l=(IList)banks;for(int i=0;i<l.Count;i++){var a=l[i];approach.GetField("JunctionCentre",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,centre);approach.GetField("HasJunctionCentre",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,true);l[i]=a;}}
    public bool Mount(int style,Transform source,object banks,int index,IList<float3> occupied,out Transform pose)
    {
        var args=style<0?new object[]{source,banks,null}:style==1||style==3?new object[]{source,banks,index,(byte)style,null}:new object[]{source,banks,index,occupied,(byte)style,null};
        string name=style<0?"TryMove":style==1||style==3?"TryAppearanceSignalMountFrom":"TryAppearanceArmMountFrom";
        bool ok=(bool)far.GetMethod(name,B).Invoke(null,args);pose=(Transform)args[args.Length-1];return ok;
    }
    public bool Layout(IList<float3> lanes,float2 outward,float3 pole,out float3 inward,out float[] heads)
    {var args=new object[]{lanes,outward,pole,null,null};bool ok=(bool)garden.GetMethod("TryLayout",B).Invoke(null,args);inward=(float3)args[3];heads=(float[])args[4];return ok;}
    public void PackLayout(bool lower)
    {var p=policy.Assembly.GetType("GardenCitySignals.SignalPackPolicy",true);foreach(var name in new[]{"UseRigidSignLayout","UseLowerRigidSignLayout"})p.GetProperty(name,B).GetSetMethod(true).Invoke(null,new object[]{name=="UseRigidSignLayout"||lower});}
    public bool Far(float3 p,object banks,int source)=>(bool)far.GetMethod("IsFarSide",B).Invoke(null,new object[]{p,banks,source});
    public float3? Ground(float3 origin,float3 p,IList<float3> surface)
    {var args=new object[]{origin,p,surface,0f};if(!(bool)far.GetMethod("TryGround",B).Invoke(null,args))return null;p.y=(float)args[3];return p;}
    public bool SolveCoherent(IList<float3> points,IList<CBody> obstacles,Func<List<float3>,List<CBody>> build,
        Func<int,float3,float3?> site,Func<IList<float3>,int,float3,List<float3>> reshape,out List<float3> result,out int count)
    {
        Func<List<float3>,object> wrapper=p=>{var value=build(p);return value==null?null:L(value);};
        var p0=Expression.Parameter(typeof(List<float3>),"points");
        var callback=Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(List<float3>),list),
            Expression.Convert(Expression.Invoke(Expression.Constant(wrapper),p0),list),p0).Compile();
        var args=new object[]{points,L(obstacles),callback,site,reshape,null,0};
        bool ok=(bool)policy.GetMethod("TrySolveCoherent",B).Invoke(null,args);result=(List<float3>)args[5];count=(int)args[6];return ok;
    }
    public bool Solve(IList<float3> points,IList<CBody> obstacles,Func<List<float3>,List<CBody>> build,
        Func<int,float3,float3?> site,out List<float3> result,out int count)
    {
        Func<List<float3>,object> wrapper=p=>{var value=build(p);return value==null?null:L(value);};
        var p0=Expression.Parameter(typeof(List<float3>),"points");
        var callback=Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(List<float3>),list),
            Expression.Convert(Expression.Invoke(Expression.Constant(wrapper),p0),list),p0).Compile();
        var args=new object[]{points,L(obstacles),callback,site,null,0};
        bool ok=(bool)policy.GetMethod("TrySolve",B).Invoke(null,args);result=(List<float3>)args[4];count=(int)args[5];return ok;
    }
}

// Parameterized layout/phase audits, not an ECS world or TTE simulation.
class LayoutMatrix225
{
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception("Layout matrix: "+why);}
    static float[] A(float3 v)=>new[]{v.x,v.y,v.z};
    static readonly string[] Names={"Native Far Side","Garden City","Timber Wire","Rigid Arm","Metal Wire","Florida"};
    static bool ExpectedShare(ushort[] masks,ushort[] phases,int lanes)
    {
        if(masks.Length!=lanes||phases.Length<2)return false;ushort used=0,mapped=0;
        foreach(var m in masks)mapped|=m;
        foreach(var p in phases){if(p==0||(used&p)!=0||(mapped&p)!=p)return false;used|=p;}
        return masks.All(m=>phases.Count(p=>(m&p)!=0)<=1);
    }
    static Bounds3 Composite(Core223 core,MCatalog catalog,int style,object road,Transform pose,float3 inward,float[] heads,float reach)
    {
        var result=new Bounds3(new float3(float.MaxValue),new float3(float.MinValue));
        Action<string,float3,quaternion> part=(name,p,q)=>{
            Check(catalog.meshes.ContainsKey(name+" Mesh"),"Missing authored module "+name);
            var b=catalog.meshes[name+" Mesh"];var bounds=core.Bounds(new Bounds3(CShape.V(b.min),CShape.V(b.max)),p,q,true,1.7f);
            result.min=math.min(result.min,bounds.min);result.max=math.max(result.max,bounds.max);
        };
        bool wire=style==1||style==3,rigid=style==2||style==4;
        part(style==4?"CSFPole":style==3?"CSGMetalWirePole":style==1?"CSGWirePole":rigid?"CSGModularRigidPole":"CSGModularGardenPole",float3.zero,quaternion.identity);
        if(style==1)part("CSGWireTimber",float3.zero,quaternion.identity);
        if(!wire)
        {
            int size=(int)core.Call("MedianGrassReplacement.Systems.GardenCityLightPolicy","Size",reach);
            Check(size>=0&&size<19,"Invalid fitted arm size");
            part("CSGModular"+(rigid?"Rigid":"Garden")+"Arm"+(size+1).ToString("00"),float3.zero,quaternion.identity);
        }
        var outward=core.Field<float2>(road,"Outward");var face=quaternion.LookRotationSafe(new float3(outward.x,0,outward.y),new float3(0,1,0));
        var rotation=quaternion.LookRotationSafe(math.cross(inward,new float3(0,1,0)),new float3(0,1,0));var headRotation=math.mul(math.inverse(rotation),face);
        foreach(float d in heads)
        {
            var p=wire?new float3(d,5.94f-core.Drop(math.max(reach,2),d/math.max(reach,2)),0)
                :new float3(d,(float)core.Call("MedianGrassReplacement.Systems.GardenCityLightPolicy","ArmHeadHeight",(byte)style,d,reach),0)+math.rotate(headRotation,new float3(0,0,.12f));
            part(style==4?"CSFHorizontalHead":wire?"CSGHeadWire":rigid?"CSGHeadYellow":"CSGHeadBlack",p,headRotation);
        }
        part(style==4?"CSFLowerHead":wire?"CSGLowerHeadWire":style==2?"CSGLowerHeadYellow":"CSGLowerHeadBlack",math.rotate(headRotation,new float3(0,0,.42f))+new float3(0,3.8f,0),headRotation);
        part("CSGSignPlate",(float3)core.Call("MedianGrassReplacement.Systems.GardenCityLightPolicy","StreetSignPosition",(byte)style),quaternion.identity);
        part(style==4?"CSFPedHead":wire?"CSGPedHeadWire":"CSGPedHead",new float3(0,2.55f,.44f),quaternion.identity);
        Check(math.all(math.isfinite(result.min))&&math.all(math.isfinite(result.max))&&math.all(result.max>=result.min),"Invalid combined module bounds");
        return result;
    }
    public static void Run(Core223 core,CCases captured)
    {
        var serializer=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
        var catalog=serializer.Deserialize<MCatalog>(File.ReadAllText("SignalLayoutCatalog225.json"));
        var all=new List<object>();var fingerprints=new HashSet<string>();
        try
        {
            for(int pass=0;pass<2;pass++)
            {
                var rows=new List<object>();int count=pass==0?120:200,start=pass==0?0:120;
                for(int category=0;category<2;category++)for(int family=0;family<6;family++)for(int k=0;k<count;k++)
                {
                    int id=start+k,style=family-1,legs=3+id%4;bool tte=category==1,leftHand=(id&1)!=0,oneWay=id%5==0;
                    core.PackLayout((id&2)!=0);
                    float globalAngle=(id+1)*.17317f+category*.0443f;float median=id%3*1.25f;
                    var banks=new CBank[legs];var phases=new ushort[legs][];int pattern=id%8;
                    for(int road=0;road<legs;road++)
                    {
                        float angle=globalAngle+road*(2*math.PI/legs);
                        if(legs==3&&id%2==0)angle=globalAngle+new[]{0f,math.PI/2,math.PI}[road];
                        if(id%4==1)angle+=(road%2==0?.07f:-.04f);
                        var direction=new float2(math.cos(angle),math.sin(angle));var side=new float2(-direction.y,direction.x);
                        int laneCount=tte?3+(id+road)%2:1+(id+road)%4;
                        if(oneWay&&road==legs-1&&id%2==0)laneCount=0;
                        float width=8+laneCount*3.2f+median+(id+road*3)%7*.7f;
                        float asym=(id%3==1?(road%2==0?1.2f:.8f):1);
                        var mouth=new float3(direction.x,0,direction.y)*(15+(road*7+id)%11);
                        mouth.y=((id+road)%5-2)*.04f;
                        var l=mouth+new float3(side.x,0,side.y)*(width*.5f*asym);
                        var r=mouth-new float3(side.x,0,side.y)*(width*.5f/asym);
                        var lanes=new List<float[]>();var masks=new List<ushort>();
                        int bit=(id+road)%14;ushort x=(ushort)(1<<bit),y=(ushort)(1<<((bit+1)%16)),z=(ushort)(1<<((bit+2)%16));
                        for(int lane=0;lane<laneCount;lane++)
                        {
                            float lateral=(oneWay?-width*.5f+2.2f+lane*3.2f:median*.5f+1.6f+lane*3.2f)*(leftHand?-1:1);
                            lanes.Add(A(mouth+new float3(side.x,0,side.y)*lateral));
                            masks.Add(!tte?x:pattern==2?(ushort)(lane==0?x|y:z):lane==0?x:lane==1?y:z);
                        }
                        ushort[] controllers=!tte?new[]{x}:pattern==0?new[]{x,(ushort)(y|z)}:pattern==1?new[]{x,y,z}:pattern==2?new[]{x,y,z}
                            :pattern==3?new[]{(ushort)(x|y),z}:pattern==4?new[]{x,(ushort)(1<<((bit+3)%16))}:pattern==5?new[]{(ushort)0,y}:pattern==6?new[]{x,x}:new[]{x,y,z};
                        if(tte&&pattern==7&&masks.Count>0)masks.RemoveAt(masks.Count-1);
                        banks[road]=new CBank{left=A(l),right=A(r),outward=new[]{direction.x,direction.y},lanes=lanes.ToArray(),masks=masks.ToArray()};phases[road]=controllers;
                    }
                    string signature=family+"|"+category+"|"+serializer.Serialize(banks);
                    Check(fingerprints.Add(signature),"Repeated layout input across passes");
                    var bankList=core.Banks(banks);core.Centre(bankList,float3.zero);
                    int fits=0,fallbacks=0,shareable=0,shareRejected=0,headCount=0;var occupied=new List<float3>();
                    for(int road=0;road<legs;road++)
                    {
                        var b=banks[road];var approach=((IList)bankList)[road];var controllers=phases[road];
                        bool expected=ExpectedShare(b.masks,controllers,b.lanes.Length);
                        bool shared=(bool)core.Call("MedianGrassReplacement.Systems.SignalSupportGroupPolicy","CanShare",approach,controllers.ToList());
                        Check(shared==expected,"Unsafe or missed shared-support eligibility");
                        bool reverse=(bool)core.Call("MedianGrassReplacement.Systems.SignalSupportGroupPolicy","CanShare",approach,controllers.Reverse().ToList());
                        Check(reverse==shared,"Controller order changed sharing eligibility");if(shared)shareable++;else if(tte)shareRejected++;
                        var source=new Transform(CShape.V(leftHand?b.left:b.right),quaternion.identity);
                        bool mounted=core.Mount(style,source,bankList,road,occupied,out var pose)&&core.Far(pose.m_Position,bankList,road);
                        foreach(ushort phase in controllers)
                        {
                            var selected=core.Call("MedianGrassReplacement.Systems.GardenCityLightPolicy","PhaseApproach",approach,phase);
                            var incoming=core.Field<List<float3>>(selected,"Incoming");ushort known=0;foreach(var m in b.masks)known|=m;
                            int expectedHeads=phase==0?b.lanes.Length:b.masks.Length==b.lanes.Length&&(known&phase)==phase?b.masks.Count(m=>(m&phase)!=0):0;
                            Check(incoming.Count==expectedHeads,"Phase lane membership lost or invented");
                            var before=(ushort[])controllers.Clone();
                            if(!mounted||!core.Layout(incoming,new float2(b.outward[0],b.outward[1]),pose.m_Position,out var inward,out var heads))
                            {fallbacks++;Check(controllers.SequenceEqual(before),"Fallback changed masks");continue;}
                            float reach=heads[heads.Length-1]+.35f;
                            if(style>=0&&style!=1&&style!=3&&shared)
                            {if(!core.Layout(core.Field<List<float3>>(approach,"Incoming"),new float2(b.outward[0],b.outward[1]),pose.m_Position,out var _,out var full)){fallbacks++;continue;}reach=full[full.Length-1]+.35f;}
                            if(style>=0&&style!=1&&style!=3&&(int)core.Call("MedianGrassReplacement.Systems.GardenCityLightPolicy","Size",reach)<0){fallbacks++;continue;}
                            if(style<0){var original=captured.pairs[0].original.Geometry();Check(math.all(math.isfinite(original.m_Bounds.max)),"Native reference geometry invalid");}
                            else Composite(core,catalog,style,selected,pose,inward,heads,reach);
                            Check(controllers.SequenceEqual(before),"Rendering changed native controller membership");fits++;headCount+=heads.Length;
                        }
                        if(mounted)occupied.Add(pose.m_Position);
                        bool covered=(bool)core.Call("MedianGrassReplacement.Systems.SignalBindingPolicy","Covered",(ushort)0,(ushort)4,false,true,(ushort)0,(ushort)4);
                        Check(covered,"Independent pedestrian phase rejected");
                        Check(!(bool)core.Call("MedianGrassReplacement.Systems.SignalBindingPolicy","Covered",(ushort)0,(ushort)4,false,true,(ushort)0,(ushort)2),"Uncovered pedestrian source would be hidden");
                    }
                    rows.Add(new{pass=pass+1,index=id,family=Names[family],tte,legs,leftHand,oneWay,median,
                        acceptedPhaseLayouts=fits,guardedPhaseLayouts=fallbacks,shareableApproaches=shareable,unshareableApproaches=shareRejected,headCount,
                        scope="Compiled fitting, phase partition and authored-bound audit; guarded/unshareable results are not proof of native fallback appearance, pole deconfliction or live TTE operation."});
                }
                Check(rows.Count==(pass==0?1440:2400),"Requested pass count missing");
                File.WriteAllText("Signal-Layout-Pass"+(pass+1)+".json",serializer.Serialize(rows));all.AddRange(rows);
            }
        }
        finally{core.PackLayout(false);}
        File.WriteAllText("Signal-Layout-Matrix-Summary.json",serializer.Serialize(new{cases=all.Count,uniqueInputs=fingerprints.Count,
            families=Names,firstPassPerFamilyPerCategory=120,additionalPassPerFamilyPerCategory=200,checks,catalog.packageSha256,
            actualTTERuntimeExecuted=false,actualVanillaRoadMeshesConstructed=false,scope="Offline geometry/binding models; runtime native node generation, traffic logic, mod ownership/timing, previews and saves remain unverified."}));
        Console.WriteLine("PASS: "+all.Count+" distinct layout/phase audits: 120 + 200 additional per signal family, in both general and TTE-style groups; "+checks+" contract/bounds checks. Guarded/unshareable configurations are retained as explicit outcomes, not claimed as complete in-game layouts.");
    }
}

class WireClearanceChecks223
{
    static int checks;
    static readonly Core223 Core=new Core223();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static CBody Body(CShape s)=>new CBody{G=s.Geometry(),P=s.Pose(),M=s.GetMask()};
    static CBody Box(float3 p,float size=1)=>new CBody{G=new ObjectGeometryData{m_Flags=Flags.Physical,m_Bounds=new Bounds3(new float3(-size,0,-size),new float3(size,8,size)),m_Size=new float3(size*2,8,size*2)},P=new Transform(p,quaternion.identity),M=CollisionMask.Overground};
    static List<CBody> Rebuild(CScene scene,List<float3> points)
    {
        var bodies=new List<CBody>();
        foreach(var a in scene.actors.OrderBy(x=>int.Parse(x.shape.actor.Split('(')[1].Split(':')[0])))
        {
            var g=a.shape.Geometry();var pose=a.shape.Pose();var old=pose.m_Position;pose.m_Position=points[a.support];
            var parts=a.primary?Core.ComposeCaptured(scene,a,points,out pose.m_Rotation):a.parts;
            if(parts==null)return null;
            float3 inward=math.rotate(pose.m_Rotation,new float3(1,0,0));
            var bounds=new Bounds3(new float3(float.MaxValue),new float3(float.MinValue));
            foreach(var part in parts)
            {
                var p=CShape.V(part.position);var rotation=CShape.Q(part.rotation);
                if(!a.primary&&part.name=="CSGHeadWire Mesh")
                {
                    float distance=p.x-math.dot(pose.m_Position-old,inward);
                    if(distance<.349f||distance>29.501f)return null;
                    int other=Core.Neighbour(points,a.support,inward);if(other<0)return null;
                    var local=math.rotate(math.inverse(pose.m_Rotation),points[other]-pose.m_Position);
                    if(local.x<distance+.3f)return null;
                    float span=math.length(local.xz);p=local/local.x*distance+new float3(0,5.94f,0);p.y-=Core.Drop(span,distance/local.x);
                }
                var b=Core.Bounds(new Bounds3(CShape.V(part.boundsMin),CShape.V(part.boundsMax)),p,rotation,(g.m_Flags&Flags.Standing)!=0,g.m_LegSize.y);
                bounds.min=math.min(bounds.min,b.min);bounds.max=math.max(bounds.max,b.max);
            }
            g.m_Bounds=bounds;g.m_Size=Game.Objects.ObjectUtils.GetSize(bounds);
            bodies.Add(new CBody{G=g,P=pose,M=a.shape.GetMask()});
        }
        var pair=Core.LampPair(points);float3 centre=float3.zero;foreach(var p in points)centre+=p;centre/=points.Count;
        foreach(int i in new[]{pair.x,pair.y})
        {
            var g=new ObjectGeometryData{m_Flags=Flags.Physical|Flags.Overridable,m_Bounds=new Bounds3(CShape.V(scene.lampMin),CShape.V(scene.lampMax))};
            g.m_Size=Game.Objects.ObjectUtils.GetSize(g.m_Bounds);
            bodies.Add(new CBody{G=g,P=new Transform(points[i],Core.LampRotation(points[i],centre)),M=CollisionMask.Overground});
        }
        return bodies;
    }
    static void Main()
    {
        var data=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=128}.Deserialize<CCases>(File.ReadAllText("WireClearance223.json"));
        Core.LoadWireModules(new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<MCatalog>(File.ReadAllText("SignalLayoutCatalog225.json")));
        var native=new NativeOracle.NativeObjectPairKernel();int positives=0;
        for(int variant=0;variant<8;variant++)for(int angle=0;angle<8;angle++)for(int offset=0;offset<8;offset++)
        {
            var a=Box(new float3(-9000,120,8500),.4f);var b=Box(new float3(-9000+offset*.13f,120,8500),.3f);
            a.P.m_Rotation=quaternion.RotateY(angle*math.PI/4);b.P.m_Rotation=quaternion.RotateY(angle*.17f);
            if((variant&1)!=0){a.G.m_Flags|=Flags.Standing|Flags.CircularLeg;a.G.m_LegSize=new float3(.2f,1.7f,.2f);}
            if((variant&2)!=0){a.G.m_Flags|=Flags.Circular;b.G.m_Flags|=Flags.Circular;}
            if((variant&4)!=0){a.M|=CollisionMask.OnGround|CollisionMask.ExclusiveGround;b.M|=CollisionMask.OnGround|CollisionMask.ExclusiveGround;a.G.m_LegOffset=new float2(.15f);}
            bool hit=native.Evaluate(a.G,a.P,a.M,b.G,b.P,b.M);Check(!hit||Core.Overlap(a,b),"Synthetic native geometry/mask false negative");
        }
        foreach(var p in data.pairs)
        {
            if(p.applied.unsupported||p.candidate.unsupported||p.original.unsupported)continue;
            foreach(bool geometry in new[]{false,true})foreach(bool pose in new[]{false,true})
            {
                var a=new CBody{G=geometry?p.applied.Geometry():p.original.Geometry(),P=pose?p.applied.Pose():p.original.Pose(),M=geometry?p.applied.GetMask():p.original.GetMask()};var b=Body(p.candidate);
                bool hit=native.Evaluate(a.G,a.P,a.M,b.G,b.P,b.M);
                bool conservative=Core.Overlap(a,b);Check(!hit||conservative,"Clearance missed a native overlap: "+p.applied.actor);
                if(hit)positives++;
            }
        }
        var outcomes=new List<object>();
        foreach(var scene in data.scenes)
        {
            var points=scene.positions.Select(CShape.V).ToList();var baseline=Rebuild(scene,points);
            Check(baseline!=null,"Captured layout could not rebuild");
            var actors=scene.actors.OrderBy(x=>int.Parse(x.shape.actor.Split('(')[1].Split(':')[0])).ToArray();
            for(int i=0;i<actors.Length;i++)
            {Check(math.distance(baseline[i].G.m_Bounds.min,actors[i].shape.Geometry().m_Bounds.min)<.003f,"Predicted native minimum bound differs: "+actors[i].shape.actor);
             Check(math.distance(baseline[i].G.m_Bounds.max,actors[i].shape.Geometry().m_Bounds.max)<.003f,"Predicted native maximum bound differs: "+actors[i].shape.actor);}
            var obstacles=scene.obstacles.Select(Body).ToList();var banks=Core.Banks(scene.banks);var surface=new List<float3>();
            foreach(var c in scene.curves)
            {var curve=new Bezier4x3(CShape.V(c.a),CShape.V(c.b),CShape.V(c.c),CShape.V(c.d));surface.Add(MathUtils.Position(curve,c.start?0:1));surface.Add(MathUtils.Position(curve,c.start?.1f:.9f));}
            Func<int,float3,float3?> site=(i,p)=>Core.Far(p,banks,scene.sourceApproaches[i])?Core.Ground(points[i],p,surface):null;
            bool ok=Core.SolveCoherent(points,obstacles,p=>Rebuild(scene,p),site,
                (layout,index,candidate)=>(List<float3>)Core.Call("MedianGrassReplacement.Systems.WireCornerLayoutPolicy","Shift",layout,banks,index,candidate),out var solved,out int count);
            Check(ok,"No complete clearance solution: "+scene.time+" "+scene.node+" evaluations="+count);
            Check(count<=512,"Node evaluation limit exceeded");
            var final=Rebuild(scene,solved);Check(final.Count==baseline.Count,"Controller/attachment count changed");
            foreach(var a in final)foreach(var b in obstacles)
            {Check(!Core.Overlap(a,b),"Final clearance still overlaps");Check(!native.Evaluate(a.G,a.P,a.M,b.G,b.P,b.M),"Native replay rejects final layout");}
            float shift=0;for(int i=0;i<points.Count;i++){Check(site(i,solved[i]).HasValue,"Final support lost valid curb/far side/ground");shift=math.max(shift,math.distance(points[i].xz,solved[i].xz));}
            Check(shift<=.501f,"Unbounded support shift");
            outcomes.Add(new{scene.time,scene.node,evaluations=count,maxShift=shift,actors=final.Count,obstacles=obstacles.Count});
        }
        // 300 varied 3/4/5/6-support policy fixtures: no city/world or native
        // junction gameplay is simulated by these geometric solver checks.
        int scenarios=0;
        for(int n=3;n<=6;n++)for(int variation=0;variation<75;variation++)
        {
            float angle0=variation*.137f;var points=new List<float3>();
            for(int i=0;i<n;i++){float angle=angle0+i*(2*math.PI/n);points.Add(new float3(math.cos(angle)*20,0,math.sin(angle)*20));}
            var blockers=new List<CBody>();
            if(variation%3!=0){var direction=math.normalizesafe(points[0]);blockers.Add(Box(points[0]+direction*.45f,.2f));}
            Func<List<float3>,List<CBody>> build=p=>p.Select(x=>Box(x,.28f)).ToList();
            Check(Core.Solve(points,blockers,build,(i,p)=>p,out var solved,out int count),"Synthetic clearance failed");
            Check(solved.Count==n&&count<=512,"Synthetic topology/budget changed");
            foreach(var a in build(solved))foreach(var b in blockers)Check(!Core.Overlap(a,b),"Synthetic residual collision");
            scenarios++;
        }
        var input=new List<float3>{float3.zero,new float3(10,0,0)};var copy=input.ToArray();
        Check(!Core.Solve(input,new List<CBody>{Box(float3.zero)},p=>p.Select(x=>Box(x)).ToList(),(i,p)=>null,out var failed,out int failedCount)&&failed==null,"Blocked-site fallback failed");
        Check(input.SequenceEqual(copy),"Failure mutated original layout");
        Check(!Core.Solve(input,Enumerable.Range(0,129).Select(i=>Box(float3.zero)).ToList(),p=>throw new Exception("Over-limit callback invoked"),(i,p)=>p,out failed,out failedCount),"Obstacle cap missing");
        int calls=0;
        Check(!Core.Solve(input,new List<CBody>{Box(float3.zero)},p=>{calls++;return calls==1||calls>=3?new List<CBody>{Box(float3.zero)}:new List<CBody>{Box(new float3(100,0,0))};},(i,p)=>p,out failed,out failedCount),"Final full-layout rebuild was not checked");
        LayoutMatrix225.Run(Core,data);
        File.WriteAllText("WireClearance223-Replay.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(outcomes));
        Console.WriteLine("PASS: "+checks+" compiled checks; "+data.scenes.Length+" captured layouts rebuilt (vehicles, extra pedestrian heads, lamps), 300 synthetic topology cases, "+positives+" native-positive counterfactual pairs covered. Local tree scheduling, live previews/saves and actual native tag clearing remain runtime validation.");
    }
}

namespace NativeOracle {
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Objects;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

// Exact installed native arithmetic excerpt. Lookup/owner/search eligibility
// is handled by the replay driver and is not claimed as historical execution.
class NativeObjectPairKernel {
    CollisionMask m_CollisionMask;
    Transform m_ObjectTransform;
    ObjectGeometryData m_PrefabGeometryData;
    Stack m_ObjectStack;
    StackData m_ObjectStackData;
    Bounds3 m_ObjectBounds;
    struct PairBounds { internal Bounds3 m_Bounds; }
    internal bool Hit;
    internal bool Soft;
    internal int CollisionLine;
    internal bool Evaluate(ObjectGeometryData a, Transform pa, CollisionMask ma, ObjectGeometryData b, Transform pb, CollisionMask mb) {
        Hit=false; Soft=false; CollisionLine=0;
        if ((ma & mb)==0) return false;
        m_CollisionMask=ma; m_ObjectTransform=pa; m_PrefabGeometryData=a;
        m_ObjectBounds=ObjectUtils.CalculateBounds(pa.m_Position,pa.m_Rotation,a);
        var bounds=ObjectUtils.CalculateBounds(pb.m_Position,pb.m_Rotation,b);
        bool broad=(ma & CollisionMask.OnGround)!=0 ? MathUtils.Intersect(bounds.xz,m_ObjectBounds.xz) : MathUtils.Intersect(bounds,m_ObjectBounds);
        if (!broad) return false;
        CheckPair(b,pb,mb,new PairBounds {m_Bounds=bounds},(b.m_Flags & (GeometryFlags.Overridable | GeometryFlags.DeleteOverridden))==GeometryFlags.Overridable,default);
        return Hit;
    }
    void CheckPair(ObjectGeometryData objectGeometryData, Transform transform, CollisionMask collisionMask, PairBounds bounds, bool overridableCollision, Entity objectEntity) {
        float3 float5=MathUtils.Center(bounds.m_Bounds);
        float3 pos=default;
        bool flag=false;
        StackData componentData2=default;
        Stack componentData3=default;
if (((m_CollisionMask & CollisionMask.OnGround) == 0 || MathUtils.Intersect(bounds.m_Bounds, m_ObjectBounds)) && !flag)
				{
					quaternion q = math.inverse(m_ObjectTransform.m_Rotation);
					quaternion q2 = math.inverse(transform.m_Rotation);
					float3 float6 = math.mul(q, m_ObjectTransform.m_Position - float5);
					float3 float7 = math.mul(q2, transform.m_Position - float5);
					Bounds3 bounds2 = ObjectUtils.GetBounds(m_ObjectStack, m_PrefabGeometryData, m_ObjectStackData);
					if ((m_PrefabGeometryData.m_Flags & GeometryFlags.IgnoreBottomCollision) != 0)
					{
						bounds2.min.y = math.max(bounds2.min.y, 0f);
					}
					if (ObjectUtils.GetStandingLegCount(m_PrefabGeometryData, out var legCount))
					{
						for (int k = 0; k < legCount; k++)
						{
							float3 float8 = float6 + ObjectUtils.GetStandingLegOffset(m_PrefabGeometryData, k);
							if ((m_PrefabGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
							{
								Cylinder3 cylinder = new Cylinder3
								{
									circle = new Circle2(m_PrefabGeometryData.m_LegSize.x * 0.5f - 0.01f, float8.xz),
									height = new Bounds1(bounds2.min.y + 0.01f, m_PrefabGeometryData.m_LegSize.y + 0.01f) + float8.y,
									rotation = m_ObjectTransform.m_Rotation
								};
								Bounds3 bounds3 = ObjectUtils.GetBounds(componentData3, objectGeometryData, componentData2);
								if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreBottomCollision) != 0)
								{
									bounds3.min.y = math.max(bounds3.min.y, 0f);
								}
								if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount2))
								{
									for (int l = 0; l < legCount2; l++)
									{
										float3 float9 = float7 + ObjectUtils.GetStandingLegOffset(objectGeometryData, l);
										if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
										{
											if (ValidationHelpers.Intersect(cylinder2: new Cylinder3
											{
												circle = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, float9.xz),
												height = new Bounds1(bounds3.min.y + 0.01f, objectGeometryData.m_LegSize.y + 0.01f) + float9.y,
												rotation = transform.m_Rotation
											}, cylinder1: cylinder, pos: ref pos))
											{
												AddCollision(overridableCollision, objectEntity, 446);
												return;
											}
										}
										else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0)
										{
											Box3 box = new Box3
											{
												bounds = 
												{
													min = 
													{
														y = bounds3.min.y + 0.01f,
														xz = objectGeometryData.m_LegSize.xz * -0.5f + 0.01f
													},
													max = 
													{
														y = objectGeometryData.m_LegSize.y + 0.01f,
														xz = objectGeometryData.m_LegSize.xz * 0.5f - 0.01f
													}
												}
											};
											box.bounds += float9;
											box.rotation = transform.m_Rotation;
											if (MathUtils.Intersect(cylinder, box, out var _, out var _))
											{
												AddCollision(overridableCollision, objectEntity, 472);
												return;
											}
										}
									}
									bounds3.min.y = objectGeometryData.m_LegSize.y;
								}
								if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
								{
									if (ValidationHelpers.Intersect(cylinder2: new Cylinder3
									{
										circle = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, float7.xz),
										height = new Bounds1(bounds3.min.y + 0.01f, bounds3.max.y - 0.01f) + float7.y,
										rotation = transform.m_Rotation
									}, cylinder1: cylinder, pos: ref pos))
									{
										AddCollision(overridableCollision, objectEntity, 488);
										return;
									}
									continue;
								}
								Box3 box2 = default;
								box2.bounds = bounds3 + float7;
								box2.bounds = MathUtils.Expand(box2.bounds, -0.01f);
								box2.rotation = transform.m_Rotation;
								if (MathUtils.Intersect(cylinder, box2, out var _, out var _))
								{
									AddCollision(overridableCollision, objectEntity, 499);
									return;
								}
							}
							else
							{
								if ((m_PrefabGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) != 0)
								{
									continue;
								}
								Box3 box3 = new Box3
								{
									bounds = 
									{
										min = 
										{
											y = bounds2.min.y + 0.01f,
											xz = m_PrefabGeometryData.m_LegSize.xz * -0.5f + 0.01f
										},
										max = 
										{
											y = m_PrefabGeometryData.m_LegSize.y + 0.01f,
											xz = m_PrefabGeometryData.m_LegSize.xz * 0.5f - 0.01f
										}
									}
								};
								box3.bounds += float8;
								box3.rotation = m_ObjectTransform.m_Rotation;
								Bounds3 bounds4 = ObjectUtils.GetBounds(componentData3, objectGeometryData, componentData2);
								if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreBottomCollision) != 0)
								{
									bounds4.min.y = math.max(bounds4.min.y, 0f);
								}
								if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount3))
								{
									for (int m = 0; m < legCount3; m++)
									{
										float3 float10 = float7 + ObjectUtils.GetStandingLegOffset(objectGeometryData, m);
										if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
										{
											if (MathUtils.Intersect(new Cylinder3
											{
												circle = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, float10.xz),
												height = new Bounds1(bounds4.min.y + 0.01f, objectGeometryData.m_LegSize.y + 0.01f) + float10.y,
												rotation = transform.m_Rotation
											}, box3, out var _, out var _))
											{
												AddCollision(overridableCollision, objectEntity, 546);
												return;
											}
										}
										else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0)
										{
											Box3 box4 = new Box3
											{
												bounds = 
												{
													min = 
													{
														y = bounds4.min.y + 0.01f,
														xz = objectGeometryData.m_LegSize.xz * -0.5f + 0.01f
													},
													max = 
													{
														y = objectGeometryData.m_LegSize.y + 0.01f,
														xz = objectGeometryData.m_LegSize.xz * 0.5f - 0.01f
													}
												}
											};
											box4.bounds += float10;
											box4.rotation = transform.m_Rotation;
											if (MathUtils.Intersect(box3, box4, out var _, out var _))
											{
												AddCollision(overridableCollision, objectEntity, 572);
												return;
											}
										}
									}
									bounds4.min.y = objectGeometryData.m_LegSize.y;
								}
								if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
								{
									if (MathUtils.Intersect(new Cylinder3
									{
										circle = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, float7.xz),
										height = new Bounds1(bounds4.min.y + 0.01f, bounds4.max.y - 0.01f) + float7.y,
										rotation = transform.m_Rotation
									}, box3, out var _, out var _))
									{
										AddCollision(overridableCollision, objectEntity, 588);
										return;
									}
									continue;
								}
								Box3 box5 = default;
								box5.bounds = bounds4 + float7;
								box5.bounds = MathUtils.Expand(box5.bounds, -0.01f);
								box5.rotation = transform.m_Rotation;
								if (MathUtils.Intersect(box3, box5, out var _, out var _))
								{
									AddCollision(overridableCollision, objectEntity, 599);
									return;
								}
							}
						}
						bounds2.min.y = m_PrefabGeometryData.m_LegSize.y;
					}
					if ((m_PrefabGeometryData.m_Flags & GeometryFlags.Circular) != 0)
					{
						Cylinder3 cylinder2 = new Cylinder3
						{
							circle = new Circle2(m_PrefabGeometryData.m_Size.x * 0.5f - 0.01f, float6.xz),
							height = new Bounds1(bounds2.min.y + 0.01f, bounds2.max.y - 0.01f) + float6.y,
							rotation = m_ObjectTransform.m_Rotation
						};
						Bounds3 bounds5 = ObjectUtils.GetBounds(componentData3, objectGeometryData, componentData2);
						if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreBottomCollision) != 0)
						{
							bounds5.min.y = math.max(bounds5.min.y, 0f);
						}
						if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount4))
						{
							for (int n = 0; n < legCount4; n++)
							{
								float3 float11 = float7 + ObjectUtils.GetStandingLegOffset(objectGeometryData, n);
								if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
								{
									if (ValidationHelpers.Intersect(cylinder2: new Cylinder3
									{
										circle = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, float11.xz),
										height = new Bounds1(bounds5.min.y + 0.01f, objectGeometryData.m_LegSize.y + 0.01f) + float11.y,
										rotation = transform.m_Rotation
									}, cylinder1: cylinder2, pos: ref pos))
									{
										AddCollision(overridableCollision, objectEntity, 633);
										return;
									}
								}
								else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0)
								{
									Box3 box6 = new Box3
									{
										bounds = 
										{
											min = 
											{
												y = bounds5.min.y + 0.01f,
												xz = objectGeometryData.m_LegSize.xz * -0.5f + 0.01f
											},
											max = 
											{
												y = objectGeometryData.m_LegSize.y + 0.01f,
												xz = objectGeometryData.m_LegSize.xz * 0.5f - 0.01f
											}
										}
									};
									box6.bounds += float11;
									box6.rotation = transform.m_Rotation;
									if (MathUtils.Intersect(cylinder2, box6, out var _, out var _))
									{
										AddCollision(overridableCollision, objectEntity, 659);
										return;
									}
								}
							}
							bounds5.min.y = objectGeometryData.m_LegSize.y;
						}
						if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
						{
							if (ValidationHelpers.Intersect(cylinder2: new Cylinder3
							{
								circle = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, float7.xz),
								height = new Bounds1(bounds5.min.y + 0.01f, bounds5.max.y - 0.01f) + float7.y,
								rotation = transform.m_Rotation
							}, cylinder1: cylinder2, pos: ref pos))
							{
								AddCollision(overridableCollision, objectEntity, 675);
								return;
							}
						}
						else
						{
							Box3 box7 = default;
							box7.bounds = bounds5 + float7;
							box7.bounds = MathUtils.Expand(box7.bounds, -0.01f);
							box7.rotation = transform.m_Rotation;
							if (MathUtils.Intersect(cylinder2, box7, out var _, out var _))
							{
								AddCollision(overridableCollision, objectEntity, 687);
								return;
							}
						}
					}
					else
					{
						Box3 box8 = default;
						box8.bounds = bounds2 + float6;
						box8.bounds = MathUtils.Expand(box8.bounds, -0.01f);
						box8.rotation = m_ObjectTransform.m_Rotation;
						Bounds3 bounds6 = ObjectUtils.GetBounds(componentData3, objectGeometryData, componentData2);
						if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreBottomCollision) != 0)
						{
							bounds6.min.y = math.max(bounds6.min.y, 0f);
						}
						if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount5))
						{
							for (int num = 0; num < legCount5; num++)
							{
								float3 float12 = float7 + ObjectUtils.GetStandingLegOffset(objectGeometryData, num);
								if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
								{
									if (MathUtils.Intersect(new Cylinder3
									{
										circle = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, float12.xz),
										height = new Bounds1(bounds6.min.y + 0.01f, objectGeometryData.m_LegSize.y + 0.01f) + float12.y,
										rotation = transform.m_Rotation
									}, box8, out var _, out var _))
									{
										AddCollision(overridableCollision, objectEntity, 717);
										return;
									}
								}
								else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0)
								{
									Box3 box9 = new Box3
									{
										bounds = 
										{
											min = 
											{
												y = bounds6.min.y + 0.01f,
												xz = objectGeometryData.m_LegSize.xz * -0.5f + 0.01f
											},
											max = 
											{
												y = objectGeometryData.m_LegSize.y + 0.01f,
												xz = objectGeometryData.m_LegSize.xz * 0.5f - 0.01f
											}
										}
									};
									box9.bounds += float12;
									box9.rotation = transform.m_Rotation;
									if (MathUtils.Intersect(box8, box9, out var _, out var _))
									{
										AddCollision(overridableCollision, objectEntity, 743);
										return;
									}
								}
							}
							bounds6.min.y = objectGeometryData.m_LegSize.y;
						}
						if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
						{
							if (MathUtils.Intersect(new Cylinder3
							{
								circle = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, float7.xz),
								height = new Bounds1(bounds6.min.y + 0.01f, bounds6.max.y - 0.01f) + float7.y,
								rotation = transform.m_Rotation
							}, box8, out var _, out var _))
							{
								AddCollision(overridableCollision, objectEntity, 759);
								return;
							}
						}
						else
						{
							Box3 box10 = default;
							box10.bounds = bounds6 + float7;
							box10.bounds = MathUtils.Expand(box10.bounds, -0.01f);
							box10.rotation = transform.m_Rotation;
							if (MathUtils.Intersect(box8, box10, out var _, out var _))
							{
								AddCollision(overridableCollision, objectEntity, 771);
								return;
							}
						}
					}
				}
				if (!CommonUtils.ExclusiveGroundCollision(m_CollisionMask, collisionMask))
				{
					return;
				}
				if (ObjectUtils.GetStandingLegCount(m_PrefabGeometryData, out var legCount6))
				{
					for (int num2 = 0; num2 < legCount6; num2++)
					{
						float3 position = ObjectUtils.GetStandingLegPosition(m_PrefabGeometryData, m_ObjectTransform, num2) - float5;
						if ((m_PrefabGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
						{
							Circle2 circle = new Circle2(m_PrefabGeometryData.m_LegSize.x * 0.5f - 0.01f, position.xz);
							Bounds2 intersection10;
							if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount7))
							{
								for (int num3 = 0; num3 < legCount7; num3++)
								{
									float3 position2 = ObjectUtils.GetStandingLegPosition(objectGeometryData, transform, num3) - float5;
									Bounds2 intersection9;
									if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
									{
										Circle2 circle2 = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, position2.xz);
										if (MathUtils.Intersect(circle, circle2))
										{
											AddCollision(overridableCollision, objectEntity, 801);
											return;
										}
									}
									else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0 && MathUtils.Intersect(ObjectUtils.CalculateBaseCorners(bounds: MathUtils.Expand(new Bounds3
									{
										min = 
										{
											xz = objectGeometryData.m_LegSize.xz * -0.5f
										},
										max = 
										{
											xz = objectGeometryData.m_LegSize.xz * 0.5f
										}
									}, -0.01f), position: position2, rotation: transform.m_Rotation).xz, circle, out intersection9))
									{
										AddCollision(overridableCollision, objectEntity, 817);
										return;
									}
								}
							}
							else if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
							{
								Circle2 circle3 = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, (transform.m_Position - float5).xz);
								if (MathUtils.Intersect(circle, circle3))
								{
									AddCollision(overridableCollision, objectEntity, 827);
									break;
								}
							}
							else if (MathUtils.Intersect(ObjectUtils.CalculateBaseCorners(transform.m_Position - float5, transform.m_Rotation, MathUtils.Expand(objectGeometryData.m_Bounds, -0.01f)).xz, circle, out intersection10))
							{
								AddCollision(overridableCollision, objectEntity, 833);
								break;
							}
						}
						else
						{
							if ((m_PrefabGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) != 0)
							{
								continue;
							}
							Quad2 xz = ObjectUtils.CalculateBaseCorners(bounds: MathUtils.Expand(new Bounds3
							{
								min = 
								{
									xz = m_PrefabGeometryData.m_LegSize.xz * -0.5f
								},
								max = 
								{
									xz = m_PrefabGeometryData.m_LegSize.xz * 0.5f
								}
							}, -0.01f), position: position, rotation: m_ObjectTransform.m_Rotation).xz;
							if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount8))
							{
								for (int num4 = 0; num4 < legCount8; num4++)
								{
									float3 position3 = ObjectUtils.GetStandingLegPosition(objectGeometryData, transform, num4) - float5;
									if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
									{
										Circle2 circle4 = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, position3.xz);
										if (MathUtils.Intersect(xz, circle4, out var _))
										{
											AddCollision(overridableCollision, objectEntity, 864);
											return;
										}
									}
									else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0)
									{
										Quad2 xz2 = ObjectUtils.CalculateBaseCorners(bounds: MathUtils.Expand(new Bounds3
										{
											min = 
											{
												xz = objectGeometryData.m_LegSize.xz * -0.5f
											},
											max = 
											{
												xz = objectGeometryData.m_LegSize.xz * 0.5f
											}
										}, -0.01f), position: position3, rotation: transform.m_Rotation).xz;
										if (MathUtils.Intersect(xz, xz2, out var _))
										{
											AddCollision(overridableCollision, objectEntity, 883);
											return;
										}
									}
								}
							}
							else if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
							{
								Circle2 circle5 = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, (transform.m_Position - float5).xz);
								if (MathUtils.Intersect(xz, circle5, out var _))
								{
									AddCollision(overridableCollision, objectEntity, 894);
									break;
								}
							}
							else
							{
								Quad2 xz3 = ObjectUtils.CalculateBaseCorners(transform.m_Position - float5, transform.m_Rotation, MathUtils.Expand(objectGeometryData.m_Bounds, -0.01f)).xz;
								if (MathUtils.Intersect(xz, xz3, out var _))
								{
									AddCollision(overridableCollision, objectEntity, 903);
									break;
								}
							}
						}
					}
					return;
				}
				if ((m_PrefabGeometryData.m_Flags & GeometryFlags.Circular) != 0)
				{
					Circle2 circle6 = new Circle2(m_PrefabGeometryData.m_Size.x * 0.5f - 0.01f, (m_ObjectTransform.m_Position - float5).xz);
					Bounds2 intersection16;
					if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount9))
					{
						for (int num5 = 0; num5 < legCount9; num5++)
						{
							float3 position4 = ObjectUtils.GetStandingLegPosition(objectGeometryData, transform, num5) - float5;
							Bounds2 intersection15;
							if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
							{
								if (MathUtils.Intersect(circle2: new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, position4.xz), circle1: circle6))
								{
									AddCollision(overridableCollision, objectEntity, 925);
									break;
								}
							}
							else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0 && MathUtils.Intersect(ObjectUtils.CalculateBaseCorners(bounds: MathUtils.Expand(new Bounds3
							{
								min = 
								{
									xz = objectGeometryData.m_LegSize.xz * -0.5f
								},
								max = 
								{
									xz = objectGeometryData.m_LegSize.xz * 0.5f
								}
							}, -0.01f), position: position4, rotation: transform.m_Rotation).xz, circle6, out intersection15))
							{
								AddCollision(overridableCollision, objectEntity, 941);
								break;
							}
						}
					}
					else if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
					{
						Circle2 circle7 = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, (transform.m_Position - float5).xz);
						if (MathUtils.Intersect(circle6, circle7))
						{
							AddCollision(overridableCollision, objectEntity, 951);
						}
					}
					else if (MathUtils.Intersect(ObjectUtils.CalculateBaseCorners(transform.m_Position - float5, transform.m_Rotation, MathUtils.Expand(objectGeometryData.m_Bounds, -0.01f)).xz, circle6, out intersection16))
					{
						AddCollision(overridableCollision, objectEntity, 956);
					}
					return;
				}
				Quad2 xz4 = ObjectUtils.CalculateBaseCorners(m_ObjectTransform.m_Position - float5, m_ObjectTransform.m_Rotation, MathUtils.Expand(m_PrefabGeometryData.m_Bounds, -0.01f)).xz;
				if (ObjectUtils.GetStandingLegCount(objectGeometryData, out var legCount10))
				{
					for (int num6 = 0; num6 < legCount10; num6++)
					{
						float3 position5 = ObjectUtils.GetStandingLegPosition(objectGeometryData, transform, num6) - float5;
						if ((objectGeometryData.m_Flags & (GeometryFlags.CircularLeg | GeometryFlags.IgnoreLegCollision)) == GeometryFlags.CircularLeg)
						{
							Circle2 circle8 = new Circle2(objectGeometryData.m_LegSize.x * 0.5f - 0.01f, position5.xz);
							if (MathUtils.Intersect(xz4, circle8, out var _))
							{
								AddCollision(overridableCollision, objectEntity, 971);
								break;
							}
						}
						else if ((objectGeometryData.m_Flags & GeometryFlags.IgnoreLegCollision) == 0)
						{
							Quad2 xz5 = ObjectUtils.CalculateBaseCorners(bounds: MathUtils.Expand(new Bounds3
							{
								min = 
								{
									xz = objectGeometryData.m_LegSize.xz * -0.5f
								},
								max = 
								{
									xz = objectGeometryData.m_LegSize.xz * 0.5f
								}
							}, -0.01f), position: position5, rotation: transform.m_Rotation).xz;
							if (MathUtils.Intersect(xz4, xz5, out var _))
							{
								AddCollision(overridableCollision, objectEntity, 990);
								break;
							}
						}
					}
				}
				else if ((objectGeometryData.m_Flags & GeometryFlags.Circular) != 0)
				{
					Circle2 circle9 = new Circle2(objectGeometryData.m_Size.x * 0.5f - 0.01f, (transform.m_Position - float5).xz);
					if (MathUtils.Intersect(xz4, circle9, out var _))
					{
						AddCollision(overridableCollision, objectEntity, 1001);
					}
				}
				else
				{
					Quad2 xz6 = ObjectUtils.CalculateBaseCorners(transform.m_Position - float5, transform.m_Rotation, MathUtils.Expand(objectGeometryData.m_Bounds, -0.01f)).xz;
					if (MathUtils.Intersect(xz4, xz6, out var _))
					{
						AddCollision(overridableCollision, objectEntity, 1009);
					}
				}
			}

    void AddCollision(bool overridableCollision,Entity other,int line) {Hit=true;Soft=overridableCollision;CollisionLine=line;}
}

}
