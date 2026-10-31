using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using Colossal.Mathematics;using Unity.Mathematics;
class Test52ParkingTransitions
{
 static int n;static Assembly a;static BindingFlags stat=BindingFlags.Static|BindingFlags.NonPublic,inst=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
 static MethodInfo M(string t,string m)=>a.GetType("MedianGrassReplacement.Systems."+t).GetMethod(m,stat);
 static object Markers(float[] t,bool[] available,float lower=0,float upper=1)=>M("ParkingMarkPlacement","BayMarkers").Invoke(null,new object[]{t,available,lower,upper});
 static float P(object b)=>(float)b.GetType().GetField("T",inst).GetValue(b);
 static bool F(object b,string field)=>(bool)b.GetType().GetField(field,inst).GetValue(b);
 static void Main()
 {
  a=Assembly.LoadFrom("MedianGrassReplacement.dll");var path=new Bezier4x3(new float3(0,0,0),new float3(0,0,28f/3),new float3(0,0,56f/3),new float3(0,0,28));
  var ts=new float[]{0,.25f,.5f,.75f,1};var all=(IList)Markers(ts,new[]{true,true,true,true});
  Check(all.Count==5,"complete four-bay run has five boundaries");
  Check(!F(all[0],"Before")&&F(all[0],"After"),"first boundary points forward into run");
  Check(F(all[4],"Before")&&!F(all[4],"After"),"last boundary points backward into run");
  for(int i=1;i<4;i++)Check(F(all[i],"Before")&&F(all[i],"After"),"interior boundary remains T");
  foreach(bool side in new[]{true,false})foreach(object marker in all)
  {
   object[] args={path,P(marker),side,F(marker,"Before"),F(marker,"After"),default(Bezier4x3),default(Bezier4x3)};
   Check((bool)M("ParkingMarkPlacement","TryBoundaryMarker").Invoke(null,args),"L/T marker fits including actual lane endpoints");
   var arm=(Bezier4x3)args[5];var stem=(Bezier4x3)args[6];var point=MathUtils.Position(path,P(marker));
   Check(math.abs(MathUtils.Length(arm)-(F(marker,"Before")&&F(marker,"After")?.6f:.3f))<.001f,"terminal arms are half a T");
   Check(math.distance(stem.a,point)<.001f&&math.abs(MathUtils.Length(stem)-.4f)<.001f,"stem keeps same bay alignment/length");
   Check(side?stem.d.x<point.x:stem.d.x>point.x,"parking-facing stem mirrors by roadside");
   Check(arm.a.z>=-.001f&&arm.d.z<=28.001f,"end L arms never extend past run");
  }
  var gap=(IList)Markers(ts,new[]{true,false,true,true});Check(gap.Count==5,"blocked bay creates two independent runs");
  Check(F(gap[1],"Before")&&!F(gap[1],"After")&&!F(gap[2],"Before")&&F(gap[2],"After"),"obstruction has inward facing Ls on both ends");
  var style=a.GetType("MedianGrassReplacement.Systems.ParkingBayStyle");
  var strokes=(IList)M("ParkingMarkPlacement","BuildBayStyle").Invoke(null,new object[]{path,0f,1f,gap,true,Enum.ToObject(style,0),2f,true,true,false});
  int baselines=0;foreach(Bezier4x3 line in strokes)if(math.abs(line.a.z-line.d.z)>1){baselines++;Check(line.d.z<=7.001f||line.a.z>=13.999f,"boundary never crosses blocked bay");}
  Check(baselines==2,"continuous boundary is clipped to each valid bay run");
  var clipped=(IList)Markers(ts,new[]{true,true,true,true},.1f,.9f);Check(clipped.Count==3&&P(clipped[0])==.25f&&P(clipped[2])==.75f,"partial bays at stop clipping are excluded");
  Check(((IList)Markers(new float[]{0,.5f,.5f},new[]{true,true})).Count==0,"duplicate slot positions rejected");
  Check(((IList)Markers(new float[]{0,float.NaN,1},new[]{true,true})).Count==0,"invalid slot positions rejected");
  Check(((IList)Markers(ts,new[]{false,false,false,false})).Count==0,"no paint on unavailable run");
  var links=M("CenterTransitionPolicy","Links");int[][] layouts={new[]{1},new[]{2},new[]{1,1},new[]{1,2},new[]{2,1},new[]{2,2}};
  foreach(var first in layouts)foreach(var last in layouts)
  {
   var pairs=(IList)links.Invoke(null,new object[]{first,last});int countA=0,countB=0;foreach(int w in first)countA+=w;foreach(int w in last)countB+=w;
   var seenA=new HashSet<int>();var seenB=new HashSet<int>();
   foreach(int2 pair in pairs){Check(pair.x>=0&&pair.x<countA&&pair.y>=0&&pair.y<countB,"transition indexes real stripes");seenA.Add(pair.x);seenB.Add(pair.y);}
   Check(seenA.Count==countA&&seenB.Count==countB,"single/double on either median side has no orphan stripe");
   foreach(int2 x in pairs)foreach(int2 y in pairs)Check(!(x.x<y.x&&x.y>y.y),"ordered transition never swaps sides or crosses");
  }
  Check(((IList)links.Invoke(null,new object[]{new[]{3},new[]{1}})).Count==0,"unsupported pattern is not guessed");
  var guide=new Bezier4x3(new float3(0,0,0),new float3(0,0,3),new float3(0,0,7),new float3(0,0,10));
  var unit=new Bezier4x3(new float3(1,0,0),new float3(1,0,3),new float3(1,0,7),new float3(1,0,10));
  object[] fit={guide,unit,new float3(2,0,0),new float3(4,0,10),0f,0f};
  Check((bool)M("JunctionEdgePolicy","TryGuideOffsets").Invoke(null,fit),"grass-expanded unequal curb offsets supported");
  Check((float)fit[4]==2&&(float)fit[5]==4,"offset measured from composed curb endpoints, not walking lane width");
  fit[2]=new float3(-2,0,0);Check(!(bool)M("JunctionEdgePolicy","TryGuideOffsets").Invoke(null,fit),"guide crossing rejected");
  fit[2]=new float3(float.NaN,0,0);Check(!(bool)M("JunctionEdgePolicy","TryGuideOffsets").Invoke(null,fit),"nonfinite curb rejected");
  for(byte kind=0;kind<8;kind++)Check((bool)M("HighwayLinePolicy","CenterCap").Invoke(null,new object[]{(int)kind})==(kind==2||kind==3),"highway accepts automatic and explicit center boundary roles only");
  var continuous=(IList)Markers(ts,new[]{true,true,true,true});
  foreach(int paintStyle in new[]{0,1,2,3,4})
  {
   var open=(IList)M("ParkingMarkPlacement","BuildBayStyle").Invoke(null,new object[]{path,0f,1f,continuous,true,Enum.ToObject(style,paintStyle),2f,false,false,false});
   int longitudinal=0,stems=0;foreach(Bezier4x3 line in open){if(math.abs(line.d.z-line.a.z)>1)longitudinal++;else if(math.abs(line.d.x-line.a.x)>1)stems++;Check(line.a.z>0&&line.a.z<28||math.abs(line.d.z-line.a.z)>1,"no end cap at ordinary segment bounds");}
   Check(longitudinal==((paintStyle==0||paintStyle==2)?1:0),"continuous parking boundary exists only in styles requesting it");
   if(paintStyle>=2)Check(stems==3,"full-depth dividers retained at all three interior bay boundaries");
  }
  var edge=a.GetType("MedianGrassReplacement.Systems.MGREdgePaint");var set=M("EdgeUpgradePolicy","Set");
  for(byte leftStyle=0;leftStyle<=5;leftStyle++)for(byte rightStyle=0;rightStyle<=5;rightStyle++)
  {
   object value=Activator.CreateInstance(edge);value=set.Invoke(null,new object[]{value,true,leftStyle});value=set.Invoke(null,new object[]{value,false,rightStyle});
   Check((byte)edge.GetField("Left").GetValue(value)==leftStyle&&(byte)edge.GetField("Right").GetValue(value)==rightStyle,"edge edit preserves opposite side for every style/removal combination");
  }
  Check(!(bool)a.GetType("MedianGrassReplacement.Systems.NodeSplitPolicy").GetProperty("Available",stat).GetValue(null),"unsafe native splitter gated before any new topology change");
  Console.WriteLine(n+" L/T terminal, blocked-run, clipping and ordered 1/2/4-stripe transition checks passed.");
 }
}
