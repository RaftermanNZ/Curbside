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
class SkewPipeline230Checks
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
            approach.GetField("JunctionCentre",F).SetValue(road,float3.zero);approach.GetField("HasJunctionCentre",F).SetValue(road,true);
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
        var layouts=new[]{new[]{0f,90,180},new[]{0f,90,180,270},new[]{0f,70,176,265},new[]{0f,72,144,216,288},new[]{0f,65,145,216,290},new[]{0f,60,120,180,240,300},new[]{0f,50,120,180,230,300}};
        int attempted=0,fits=0,seeds=0,recovered=0;var failures=new List<object>();var counts=new int[layouts.Length];var debug=new List<object>();var done=new HashSet<int>();
        var far=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true);var mount=far.GetMethod("TryAppearanceSignalMountFrom",S);var arm=far.GetMethod("TryAppearanceArmMountFrom",S);
        for(int shape=0;shape<layouts.Length;shape++)for(int treatment=0;treatment<64;treatment++)for(int pass=0;pass<20;pass++)
        {
            var angles=layouts[shape];int legs=angles.Length;var b=new List<WireBank229>();var native=new List<Game.Objects.Transform>();
            float gap=360;for(int i=0;i<legs;i++)for(int j=i+1;j<legs;j++){float a=math.abs(angles[i]-angles[j]);gap=math.min(gap,math.min(a,360-a));}
            float maxWidth=12,depth=math.max(16,maxWidth/(float)Math.Tan(gap*math.PI/360)+2);
            Func<float3,float[]> A=x=>new[]{x.x,x.y,x.z};
            for(int i=0;i<legs;i++)
            {
                float theta=(angles[i]+pass*18)*math.PI/180;var d=new float2(math.cos(theta),math.sin(theta));var lateral=new float3(-d.y,0,d.x);
                float left=6+(i%3)*2+(((treatment>>i)&1)!=0?2:0),right=6+((i+1)%3)*2+(((treatment>>((i+1)%6))&1)!=0?1.5f:0);
                var mouth=new float3(d.x*depth,100,d.y*depth);var l=mouth+lateral*left;var r=mouth-lateral*right;
                b.Add(new WireBank229{left=A(l),right=A(r),outward=new[]{d.x,d.y},lanes=new[]{A(mouth+lateral*1.5f),A(mouth+lateral*4.5f)}});
                native.Add(new Game.Objects.Transform(r-lateral*.65f,quaternion.identity));
            }
            var roads=Roads(b.ToArray());var sources=Enumerable.Range(0,legs).ToList();
            foreach(byte style in new byte[]{1,3})
            {
                var proposed=new List<float3>();bool needsSeed=false;
                for(int i=0;i<legs;i++)
                {
                    var args0=new object[]{native[i],roads,i,style,null};bool ok=(bool)mount.Invoke(null,args0);var pose=(Game.Objects.Transform)args0[4];
                    if(!ok){var args1=new object[]{native[i],roads,i,proposed,style,null};ok=(bool)arm.Invoke(null,args1);pose=(Game.Objects.Transform)args1[5];}
                    if(!ok){pose=native[i];seeds++;needsSeed=true;}
                    proposed.Add(pose.m_Position);
                }
                var args=new object[]{proposed,roads,sources,style,null};bool fit=(bool)Call("TryFit",args);attempted++;
                if(fit){fits++;counts[shape]++;if(needsSeed)recovered++;var result=(List<float3>)args[4];Check((bool)Call("Valid",result,roads,sources,style),"Invalid skew accepted");}
                else
                {
                    if(failures.Count<100)failures.Add(new{shape,treatment,pass,style,needsSeed});
                    if(done.Add(shape))
                    {
                        var general=assembly.GetType("MedianGrassReplacement.Systems.WireJunctionLayoutPolicy",true);
                        var ga=new object[]{roads,null};bool built=(bool)general.GetMethod("Build",S).Invoke(null,ga);var g=ga[1];bool projected=false;var positions=new List<float2>();
                        if(built){positions=new List<float2>((List<float2>)g.GetType().GetField("Reference",F).GetValue(g));projected=(bool)general.GetMethod("Project",S).Invoke(null,new object[]{g,positions,-1,float2.zero});}
                        var world=positions.Select(x=>new float3(x.x,100,x.y)).ToList();
                        var valid=new List<object>();
                        if(projected)for(int r=0;r<legs;r++)for(int c=0;c<world.Count;c++)valid.Add(new{road=r,corner=c,far=(bool)far.GetMethod("IsFarSide",S).Invoke(null,new object[]{world[c],roads,r}),head=(bool)Call("HeadSpan",world,c,roads[r],style),baseClear=(bool)Call("ClearBase",world[c],roads)});
                        var planes=new List<object>();var spans=new List<object>();var refs=new List<float[]>();var curbs=new List<float[]>();
                        if(built)
                        {
                            foreach(var item in (IEnumerable)g.GetType().GetField("Planes",F).GetValue(g)){var t=item.GetType();var normal=(float2)t.GetField("Normal",F).GetValue(item);planes.Add(new{corner=t.GetField("Corner",F).GetValue(item),normal=new[]{normal.x,normal.y},minimum=t.GetField("Minimum",F).GetValue(item)});}
                            foreach(var item in (IEnumerable)g.GetType().GetField("Spans",F).GetValue(g)){var t=item.GetType();var normal=(float2)t.GetField("Direction",F).GetValue(item);spans.Add(new{a=t.GetField("A",F).GetValue(item),b=t.GetField("B",F).GetValue(item),direction=new[]{normal.x,normal.y}});}
                            foreach(var point in (List<float2>)g.GetType().GetField("Reference",F).GetValue(g))refs.Add(new[]{point.x,point.y});
                            foreach(var point in (List<float2>)g.GetType().GetField("Curbs",F).GetValue(g))curbs.Add(new[]{point.x,point.y});
                        }
                        debug.Add(new{shape,built,projected,positions=world.Select(A).ToArray(),valid,planes,spans,refs,curbs});
                    }
                }
            }
        }
        File.WriteAllText("SkewPipeline230Checks.json",new JavaScriptSerializer().Serialize(new{attempted,fits,seeds,recovered,unresolved=attempted-fits,failures,counts,debug,checks,scope="Distinct mixed-width synthetic T/4/5/6-arm geometries,64 asymmetric bank treatments,20 rotations,both wire styles. Native mount policy then joint planner; failed individual proposals may use untouched native pose as an offline seed. No actual ECS or native surface execution."}));
        Console.WriteLine("Skew pipeline: "+fits+"/"+attempted+" fitted; "+seeds+" failed individual proposals, "+recovered+" joint successes needing native seeds. Unresolved retained.");
    }
}
