using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Unity.Mathematics;
using Game.Prefabs;
using Colossal.Mathematics;

class InwardCornerChecks
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Assembly assembly; static Type approach,policy,shape; static int checks,headChecks; static float2 testOrigin; static Type garden; static IDictionary modules;
    static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static object Call(Type t,string name,params object[] args)=>t.GetMethod(name,S).Invoke(null,args);
    static void Set(object o,string n,object v)=>approach.GetField(n,F).SetValue(o,v);
    static float2 D(float a)=>new float2(math.cos(a),math.sin(a));
    static float3 X(float2 p)=>new float3(p.x+testOrigin.x,100,p.y+testOrigin.y);
    static List<float2> Outline(List<float2> directions,List<float> left,List<float> right,float depth,float expansion)
    {
        var result=new List<float2>();
        for(int i=0;i<directions.Count;i++)
        {
            int j=(i+1)%directions.Count;var a=directions[i];var b=directions[j];
            var ar=new float2(-a.y,a.x);var br=new float2(-b.y,b.x);
            var first=a*depth+ar*(left[i]+expansion);var last=b*depth-br*(right[j]+expansion);
            result.Add(a*depth-ar*(right[i]+expansion));result.Add(first);
            float handle=math.distance(first,last)/3;var c1=first-a*handle;var c2=last-b*handle;
            for(int k=1;k<=16;k++){float t=k/16f,u=1-t;result.Add(first*(u*u*u)+c1*(3*u*u*t)+c2*(3*u*t*t)+last*(t*t*t));}
        }
        return result;
    }
    // Independent polygon oracle; rejects both road overlap and a base extending
    // beyond the supplied outer sidewalk. Does not call production containment.
    static bool Inside(float2 p,List<float2> polygon,float radius)
    {
        bool inside=false;
        for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
        {
            var a=polygon[j];var b=polygon[i];
            if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            var delta=b-a;float t=math.saturate(math.dot(p-a,delta)/math.max(.000001f,math.lengthsq(delta)));
            if(math.distancesq(p,a+delta*t)<radius*radius-.00001f)return false;
        }
        return inside;
    }
    static float Distance(float2 p,List<float2> polygon)
    {
        float d=float.MaxValue;
        for(int i=0;i<polygon.Count;i++)
        {var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var edge=b-a;float t=math.saturate(math.dot(p-a,edge)/math.max(.000001f,math.lengthsq(edge)));d=math.min(d,math.distance(p,a+edge*t));}
        return d;
    }
    static bool RoadClear(float2 p,List<float2> road)=>!Inside(p,road,0)&&Distance(p,road)>=.409f;
    static void Main(string[] command)
    {
        assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");approach=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy+Approach",true);
        policy=assembly.GetType("MedianGrassReplacement.Systems.WireCornerLayoutPolicy",true);shape=assembly.GetType("MedianGrassReplacement.Systems.SignalJunctionFootprint",true);
        var sidewalk=approach.GetField("Sidewalk",F);bool candidate=sidewalk!=null;
        garden=assembly.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);
        if(candidate)
        {
            new Core223().LoadWireModules(new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<MCatalog>(File.ReadAllText("SignalLayoutCatalog225.json")));
            modules=(IDictionary)garden.GetField("modules",S).GetValue(null);
        }
        var pack=assembly.GetType("GardenCitySignals.SignalPackPolicy",true);
        pack.GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});pack.GetProperty("UseLowerRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        if(candidate)
        {
            var bounds=assembly.GetType("MedianGrassReplacement.Systems.SignalCornerBounds",true);
            for(int i=0;i<20;i++)foreach(bool inverse in new[]{false,true})
            {
                var offset=new float3(i-10,2,3);var local=new float3(i*.37f-4,1,2);
                var actual=(float3)Call(bounds,"PieceAnchor",offset,local,inverse);var expected=offset+new float3(inverse?-local.x:local.x,local.y,local.z);
                Check(math.distance(actual,expected)<.00001f,"Native piece-coordinate contract failed");
            }
            var square=new List<float2>{new float2(-5,-5),new float2(5,-5),new float2(5,5),new float2(-5,5)};
            var patch=Call(shape,"FromOutline",square);
            foreach(float x in new[]{0,4.4f,4.6f,6})Check((bool)shape.GetMethod("ContainsBase",F).Invoke(patch,new object[]{new float2(x,0),.45f})==(x<=4.55f),"Complete-base edge containment failed");
        }
        var layouts=new[]{new[]{0f,90,180},new[]{0f,90,180,270},new[]{0f,70,176,265},new[]{0f,72,144,216,288},new[]{0f,65,145,216,290},new[]{0f,60,120,180,240,300},new[]{0f,50,120,180,230,300}};
        int cases=0,fits=0,rejections=0,outside=0,clear=0;var examples=new List<object>();var unresolvedExamples=new List<object>();var perLayout=new List<object>();var elapsed=Stopwatch.StartNew();
        for(int layout=0;layout<layouts.Length;layout++)
        {
            int fit0=fits,reject0=rejections,out0=outside;
            for(int treatment=0;treatment<8;treatment++)for(int pass=0;pass<20;pass++)
            {
                testOrigin=pass>=10?new float2(12000,-9000):float2.zero;
                int n=layouts[layout].Length;var dirs=layouts[layout].Select(a=>D((a+pass*18)*math.PI/180)).ToList();
                var left=new List<float>();var right=new List<float>();
                for(int i=0;i<n;i++){left.Add(6+(i%3)*1.5f+((treatment&1)!=0?1.5f:0));right.Add(6+((i+1)%3)*1.5f+((treatment&2)!=0?2:0)+((treatment&4)!=0&&i%2==0?2:0));}
                float depth=28;var roadOutline=Outline(dirs,left,right,depth,0);var outer=Outline(dirs,left,right,depth,3.5f);
                roadOutline=roadOutline.Select(p=>p+testOrigin).ToList();outer=outer.Select(p=>p+testOrigin).ToList();
                var pavement=Call(shape,"FromOutline",roadOutline);var full=Call(shape,"FromOutline",outer);Check(pavement!=null&&full!=null,"Invalid fixture");
                var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
                for(int i=0;i<n;i++)
                {
                    var a=Activator.CreateInstance(approach);var d=dirs[i];var across=new float2(-d.y,d.x);
                    Set(a,"Left",X(d*depth+across*left[i]));Set(a,"Right",X(d*depth-across*right[i]));Set(a,"Outward",d);
                    Set(a,"JunctionCentre",new float3(testOrigin.x,100,testOrigin.y));Set(a,"HasJunctionCentre",true);
                    Set(a,"Incoming",new List<float3>{X(d*depth+across*1.5f),X(d*depth+across*4.5f)});Set(a,"LaneMasks",new List<ushort>{1,2});
                    Set(a,"Pavement",pavement);if(candidate)sidewalk.SetValue(a,full);roads.Add(a);
                }
                var sources=Enumerable.Range(0,n).ToList();var proposed=new List<float3>();
                for(int i=0;i<n;i++)proposed.Add(X(-dirs[i]*29+new float2(dirs[i].y,-dirs[i].x)*29));
                if(n==3)proposed.Add(X(-dirs[0]*29+new float2(-dirs[0].y,dirs[0].x)*29));
                foreach(byte style in new byte[]{1,3})
                {
                    cases++;var args=new object[]{proposed,roads,sources,style,null};bool ok=(bool)Call(policy,"TryFit",args);
                    if(!ok){rejections++;Check(args[4]==null,"Partial plan leaked");if(unresolvedExamples.Count<30)unresolvedExamples.Add(new{layout,treatment,pass,style});
                        if(candidate&&layout==4&&pass==0&&style==1)
                        {
                            var jt=assembly.GetType("MedianGrassReplacement.Systems.WireJunctionLayoutPolicy",true);
                            var build=new object[]{roads,null};var built=(bool)Call(jt,"Build",build);
                            if(built)
                            {
                                var g=build[1];var gt=g.GetType();var xy=new List<float2>((List<float2>)gt.GetField("Reference",F).GetValue(g));
                                bool projected=(bool)Call(jt,"Project",g,xy,-1,default(float2));
                                var cg=Call(jt,"CornerGeometry",g,0);xy=new List<float2>((List<float2>)gt.GetField("Reference",F).GetValue(g));
                                bool cornerProjected=(bool)Call(jt,"Project",cg,xy,-1,default(float2));
                                var debugOrigin=(float2)gt.GetField("Origin",F).GetValue(g);
                                var world=xy.Select(p=>new float3(p.x+debugOrigin.x,100,p.y+debugOrigin.y)).ToList();
                                var far=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);
                                var choices=new List<object>();
                                for(int source=0;source<roads.Count;source++)for(int c=0;c<world.Count;c++)
                                    choices.Add(new{source,c,far=(bool)Call(far,"IsFarSide",world[c],roads,source),head=(bool)Call(policy,"HeadSpan",world,c,roads[source],style),baseClear=(bool)Call(policy,"ClearBase",world[c],roads)});
                                Func<string,object[]> Fields=name=>((IEnumerable)gt.GetField(name,F).GetValue(g)).Cast<object>().Select(o=>o.GetType().GetFields(F).ToDictionary(f=>f.Name,f=>(object)f.GetValue(o).ToString())).Cast<object>().ToArray();
                                File.WriteAllText("Trace.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new{projected,cornerProjected,choices,xy=xy.Select(p=>new[]{p.x,p.y}),planes=Fields("Planes"),spans=Fields("Spans"),roadOutline=roadOutline.Select(p=>new[]{p.x,p.y}),outer=outer.Select(p=>new[]{p.x,p.y})}));
                                if(command.Length>0&&command[0]=="trace")return;
                            }
                        }
                        continue;}
                    fits++;var points=(List<float3>)args[4];Check((bool)Call(policy,"Valid",points,roads,sources,style),"Invalid output");
                    bool all=true;
                    foreach(var point in points)
                    {
                        Check(RoadClear(point.xz,roadOutline),"Pole base entered pavement");
                        bool inside=Inside(point.xz,outer,.449f);all &= inside;
                        if(candidate)Check(inside,"Accepted pole extends outside sidewalk");
                    }
                    if(all)clear++;else{outside++;if(examples.Count<10)examples.Add(new{layout,treatment,pass,style,points=points.Select(p=>new[]{p.x,p.y,p.z}).ToArray()});}
                    if(candidate)CheckHeads(points,roads,sources,style);
                    // Every emitted span/head pair still passes its production
                    // served-road checks; displacing one pole is rejected.
                    var damaged=new List<float3>(points);damaged[0]=damaged[1];Check(!(bool)Call(policy,"Valid",damaged,roads,sources,style),"Duplicate pole accepted");
                }
            }
            perLayout.Add(new{layout,arms=layouts[layout].Length,fits=fits-fit0,unresolved=rejections-reject0,outside=outside-out0});
        }
        var result=new{candidate,cases,checks,headChecks,fits,unresolved=rejections,insideSidewalk=clear,outsideSidewalk=outside,elapsedMs=elapsed.Elapsed.TotalMilliseconds,perLayout,examples,unresolvedExamples,scope="Compiled geometry helper execution, seven synthetic junction geometries x eight asymmetric width/treatment profiles x 20 rotations x two wire styles. Independent full-base road and outer-sidewalk oracle. Explicit unresolved count; no ECS node-buffer read, native World, live geometry, build/save/preview or FPS proof."};
        File.WriteAllText(command.Length>0?command[0]:"InwardCornerChecks.json",new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(result));Console.WriteLine(new JavaScriptSerializer().Serialize(new{candidate,cases,checks,fits,unresolved=rejections,outside,elapsedMs=elapsed.Elapsed.TotalMilliseconds}));
    }
    static void CheckHeads(List<float3> points,IList roads,List<int> sources,byte style)
    {
        int poleCount=0;
        var headModule=(StaticObjectPrefab)modules["HeadWire"];var headMesh=headModule.m_Meshes[0].m_Mesh;
        var poleMesh=((StaticObjectPrefab)modules[style==3?"MetalWirePole":"WirePole"]).m_Meshes[0].m_Mesh;
        for(int source=0;source<sources.Count;source++)
        {
            var road=roads[sources[source]];var outward=(float2)approach.GetField("Outward",F).GetValue(road);var lanes=(List<float3>)approach.GetField("Incoming",F).GetValue(road);
            var layout=new object[]{lanes,outward,points[source],null,null};Check((bool)Call(garden,"TryLayout",layout),"Cannot place heads on fitted span");
            var inward=(float3)layout[3];var heads=(float[])layout[4];int next=(int)Call(garden,"WireNeighborForLayout",points,source,inward,true);
            int ring=(int)Call(garden,"WireRingNeighbor",points,source);
            Check(next>=0&&(next==ring||(int)Call(garden,"WireRingNeighbor",points,next)==source),"Head span takes a diagonal shortcut");
            var faceType=garden.GetNestedType("CrossingFace",BindingFlags.NonPublic);var faces=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(faceType));
            var args=new object[]{points[source],outward,inward,heads,style,points,points.Count>sources.Count?sources.Count:-1,true,true,faces,null,null,(ushort)0};
            Check((bool)Call(garden,"ComposeWire",args),"Production mesh composition rejected fitted corner");
            var meshes=(List<ObjectMeshInfo>)args[10];var rotation=(quaternion)args[11];int count=0;
            var wireType=assembly.GetType("MedianGrassReplacement.Systems.SignalWirePolicy",true);
            var curve=(Bezier4x3)Call(wireType,"Span",points[source]+new float3(0,7.45f,0),points[next]+new float3(0,7.45f,0));
            foreach(var mesh in meshes)
            {
                if(ReferenceEquals(mesh.m_Mesh,poleMesh))poleCount++;
                if(!ReferenceEquals(mesh.m_Mesh,headMesh))continue;
                var position=points[source]+math.rotate(rotation,mesh.m_Position);var delta=points[next]-points[source];
                float t=math.dot(position.xz-points[source].xz,delta.xz)/math.lengthsq(delta.xz);Check(t>=0&&t<=1,"Head extends past its wire endpoint");
                var onWire=MathUtils.Position(curve,t);
                var localEnd=math.rotate(math.inverse(rotation),points[next]-points[source]);
                var localCurve=(Bezier4x3)Call(wireType,"Span",new float3(0,7.45f,0),localEnd+new float3(0,7.45f,0));
                var localWire=MathUtils.Position(localCurve,mesh.m_Position.x/localEnd.x);
                Check(math.distance(mesh.m_Position.xz,localWire.xz)<.0002f&&math.abs(mesh.m_Position.y+1.51f-localWire.y)<.0002f,"Local head-to-wire geometry changed");
                Check(math.distance(position.xz,onWire.xz)<.006f&&math.abs(position.y+1.51f-onWire.y)<.003f,"Head pivot/hanger offset misses angled cable: source="+source+" style="+style+" position="+position+" wire="+onWire+" t="+t+" xzError="+math.distance(position.xz,onWire.xz)+" yError="+math.abs(position.y+1.51f-onWire.y));
                var facing=math.rotate(math.mul(rotation,mesh.m_Rotation),new float3(0,0,1));
                Check(math.dot(facing,new float3(outward.x,0,outward.y))>.99999f,"Head no longer faces its served lanes");count++;headChecks++;
            }
            Check(count==lanes.Count,"Lane heads were lost or duplicated");
        }
        Check(poleCount==points.Count,"Physical corner pole duplicated or omitted");
    }

}
