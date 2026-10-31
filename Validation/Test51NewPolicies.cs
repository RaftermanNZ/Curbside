using System;using System.Collections;using System.Reflection;using Game.Prefabs;using Colossal.Mathematics;using Unity.Mathematics;
class Test52NewPolicies
{
 static int n;static Assembly a;static BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 static MethodInfo Method(string type,string name)=>a.GetType("MedianGrassReplacement.Systems."+type).GetMethod(name,flags);
 static IList Lanes(float[] x,float[] widths,bool[] reverse,float[] heights=null)
 {
  var t=a.GetType("MedianGrassReplacement.Systems.NorthAmericanLinePolicy+Traffic");var result=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(t));
  for(int i=0;i<x.Length;i++)result.Add(Activator.CreateInstance(t,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{x[i],widths[i],heights==null?0:heights[i],reverse[i]},null));return result;
 }
 static void Main()
 {
  a=Assembly.LoadFrom("MedianGrassReplacement.dll");var boundaries=Method("LaneDividerPolicy","Boundaries");
  var four=(IList)boundaries.Invoke(null,new object[]{Lanes(new[]{-6f,-2,2,6},new[]{4f,4,4,4},new[]{true,true,false,false})});Check(four.Count==2,"four-lane road has two interior dividers");Check(((float2)four[0]).x==-4&&((float2)four[1]).x==4,"dividers exclude center and road edges");
  Check(((IList)boundaries.Invoke(null,new object[]{Lanes(new[]{-1.5f,1.5f},new[]{3f,3},new[]{true,false})})).Count==0,"two-way center uses centerline tool");
  var one=(IList)boundaries.Invoke(null,new object[]{Lanes(new[]{-1.5f,1.5f},new[]{3f,3},new[]{false,false})});Check(one.Count==1&&((float2)one[0]).x==0,"one-way interior divider is not a traffic-left edge");
  Check(((IList)boundaries.Invoke(null,new object[]{Lanes(new[]{-3f,3f},new[]{3f,3},new[]{false,false})})).Count==0,"divider does not cross a physical gap");
  foreach(float height in new[]{3f,float.NaN,float.PositiveInfinity})Check(((IList)boundaries.Invoke(null,new object[]{Lanes(new[]{-2f,2f},new[]{4f,4},new[]{false,false},new[]{0f,height})})).Count==0,"separate levels and invalid elevations rejected");
  var theme=Method("PaintThemePolicy","WhiteShape");
  string[] keys={"Yellow Solid Line Mesh","Yellow Double Solid Line Mesh","Yellow Dashed Line Mesh","Red Fire Line","White Double Solid Line Mesh",null};byte[] styles={1,2,3,0,0,0};for(int i=0;i<keys.Length;i++)Check((byte)theme.Invoke(null,new object[]{keys[i]})==styles[i],"European theme changes identified yellow patterns, not red/white");
  var hides=a.GetType("MedianGrassReplacement.Systems.SegmentUpgradePolicy").GetMethod("Hides",flags,null,new[]{typeof(byte),typeof(bool),typeof(bool)},null);
  foreach(bool center in new[]{false,true})foreach(bool edge in new[]{false,true})Check(!(bool)hides.Invoke(null,new object[]{(byte)8,center,edge}),"native hiding preserves generated center/lane/edge lines");
  var corner=Method("JunctionEdgePolicy","TryCorner");
  foreach(float radius in new[]{1.5f,3f,10f})
  {
   object[] args={new float3(0,0,radius),new float3(0,0,-1),new float3(radius,0,0),new float3(-1,0,0),default(Bezier4x3)};Check((bool)corner.Invoke(null,args),"curb corner accepted");var curve=(Bezier4x3)args[4];var center=new float2(radius);
   for(int i=0;i<=40;i++){var p=MathUtils.Position(curve,i/40f);Check(math.distance(p.xz,center)>=radius-.001f,"corner stays on pavement side of circular curb");}
   var middle=MathUtils.Position(curve,.5f);Check(math.distance(middle.xz,center)>radius+.01f,"fallback arc has clearance on the pavement side of the circular curb");
  }
  var split=Method("NodeSplitPolicy","TryPosition");var straight=new Bezier4x3(new float3(0,0,0),new float3(40f/3,0,0),new float3(80f/3,0,0),new float3(40,0,0));
  foreach(float x in new[]{0f,4,10,20,30,36,40}){object[] args={straight,new float3(x,0,0),6f,0f,default(float3)};Check((bool)split.Invoke(null,args)==(x>=.5f&&x<=39.5f),"split respects minimum distance from existing nodes");if(x>=.5f&&x<=39.5f)Check(math.abs(((float3)args[4]).x-x)<.01f,"split projects onto original road");}
  object[] wide={straight,new float3(10,0,0),40f,0f,default(float3)};Check((bool)split.Invoke(null,wide),"wide roads do not impose an artificial width-based split exclusion");
  foreach(float invalid in new[]{float.NaN,float.PositiveInfinity}){object[] args={straight,new float3(invalid,0,0),6f,0f,default(float3)};Check(!(bool)split.Invoke(null,args),"invalid split hit rejected");}
  Console.WriteLine(n+" lane-role, theme, additive visibility, curb-arc and split-clearance checks passed.");
 }
}
