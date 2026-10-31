using System;using System.Reflection;using System.Collections;using System.Collections.Generic;using Colossal.Mathematics;using Unity.Mathematics;
class Test52Hover {
 static Assembly a;static int n;static BindingFlags f=BindingFlags.Static|BindingFlags.NonPublic;
 static object Call(string type,string name,params object[] args)=>a.GetType("MedianGrassReplacement.Systems."+type).GetMethod(name,f).Invoke(null,args);
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
 static Bezier4x3 Line(float3 p,float3 q)=>new Bezier4x3(p,math.lerp(p,q,1f/3),math.lerp(p,q,2f/3),q);
 static void Main(){a=Assembly.LoadFrom("MedianGrassReplacement.dll");
 var choiceType=a.GetType("MedianGrassReplacement.Systems.HoverLineChoice");var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType));
 foreach(float x in new[]{-6f,6f,0f}){var choice=Activator.CreateInstance(choiceType,true);choiceType.GetField("Paths",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(choice,new List<Bezier4x3>{Line(new float3(x,0,0),new float3(x,0,100))});list.Add(choice);}
 foreach(float z in new[]{1f,30f,99f})foreach(var c in new[]{new float2(-5.8f,0),new float2(5.8f,1),new float2(.2f,2)})Check((int)Call("HoverLinePolicy","Pick",list,new float3(c.x,0,z))==(int)c.y,"same style selects either edge or center by pointer position");
 Check((int)Call("HoverLinePolicy","Pick",list,new float3(20,0,20))==-1,"distant cursor cannot unexpectedly apply an edge");Check((int)Call("HoverLinePolicy","Pick",list,new float3(float.NaN,0,20))==-1,"invalid pointer rejected");
 Check(!(bool)Call("HoverLinePolicy","Closer",0f,.02f),"near-equal highway edge/center candidates retain stable priority");
 foreach(byte style in new byte[]{1,2,3,4}){byte center=(byte)Call("HoverLinePolicy","Center",style);Check((byte)Call("CenterlineUpgradePolicy","Pattern",center)==(style>=3?2:1),"same solid shape retained between center/edge");Check((bool)Call("CenterlineUpgradePolicy","YellowPaint",center,false)==(style==2||style==4),"same explicit solid color retained");}
 foreach(byte mode in new byte[]{1,3,4,5,6,7}){Check((bool)Call("LaneDividerPolicy","Applied",mode),"six saved dash styles supported");Check((bool)Call("LaneDividerPolicy","IsYellow",mode)==(mode>=5),"divider color independent of theme");byte pattern=(byte)Call("LaneDividerPolicy","Pattern",mode);var strokes=(IList)Call("LaneDividerPolicy","Strokes",Line(new float3(0),new float3(0,0,50)),mode);Check(strokes.Count>0&&strokes.Count<=512,"dash generation bounded");foreach(Bezier4x3 dash in strokes)Check(MathUtils.Length(dash)<=(pattern==3?1:pattern==4?6:3)+.005f,"white/yellow dash lengths match selected style");}
 var rail=Line(new float3(-4,0,0),new float3(4,0,0));var guides=new List<Bezier4x3>{Line(new float3(-5,0,-4),new float3(-5,0,4)),Line(new float3(5,0,-4),new float3(5,0,4))};object[] args={rail,guides,default(Bezier4x3)};Check((bool)Call("CrosswalkPlacement","TryJoinEdgeEnds",args),"inner rail can connect to edge guides");var joined=(Bezier4x3)args[2];Check(math.abs(joined.a.x+5)<.01f&&math.abs(joined.d.x-5)<.01f,"inner endpoints meet edge line centers");
 guides[1]=Line(new float3(9,0,-4),new float3(9,0,4));args=new object[]{rail,guides,default(Bezier4x3)};Check(!(bool)Call("CrosswalkPlacement","TryJoinEdgeEnds",args),"unsafe extension beyond two metres is rejected");
 Console.WriteLine(n+" hover targeting, explicit dash-color/length, bounded crosswalk edge joins and alley-default checks passed.");
 }
}
