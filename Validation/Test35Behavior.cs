using System;using System.Reflection;using Game.Prefabs;using Unity.Mathematics;using Colossal.Mathematics;
class Test35Behavior
{
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 static MethodInfo Method(Assembly a,string type,string name)=>a.GetType("MedianGrassReplacement.Systems."+type).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
 static void Main()
 {
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var clear=Method(a,"ParkingBanPolicy","ClearMask");
  for(byte mask=0;mask<4;mask++)foreach(bool left in new[]{false,true}){byte value=(byte)clear.Invoke(null,new object[]{mask,left});Check((value&(left?1:2))==0,"new bays supersede ban on selected side");Check((value&(left?2:1))==(mask&(left?2:1)),"other side preserved");}
  var raised=Method(a,"CenterlineUpgradePolicy","RaisedMedian");
  foreach(float height in new[]{0f,.01f,.02f,.05f})Check(!(bool)raised.Invoke(null,new object[]{NetPieceFlags.HasMesh,height,0f,0f}),"flat painted Median section accepts an explicit centerline");
  Check((bool)raised.Invoke(null,new object[]{NetPieceFlags.BlockTraffic,0f,0f,0f}),"blocking physical median rejected");
  Check((bool)raised.Invoke(null,new object[]{NetPieceFlags.HasMesh,.2f,0f,0f}),"raised mesh rejected");
  Check(!(bool)raised.Invoke(null,new object[]{NetPieceFlags.HasMesh,.2f,-.3f,0f}),"below-pavement mesh not mistaken for raised median");
  var hide=a.GetType("MedianGrassReplacement.Systems.SegmentUpgradePolicy").GetMethod("Hides",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(byte),typeof(bool),typeof(bool)},null);
  foreach(byte mask in new byte[]{2,4}){Check(!(bool)hide.Invoke(null,new object[]{mask,true,true}),"lane/edge hiding preserves traffic-left center role");Check((bool)hide.Invoke(null,new object[]{mask,false,true}),"outer alley edge hides");}
  Check(!(bool)hide.Invoke(null,new object[]{(byte)4,false,false}),"parking bay paint kept by edge hiding");
  var corner=Method(a,"JunctionEdgePolicy","TryCorner");
  for(int i=0;i<4;i++)
  {
   float angle=i*math.PI*.5f;Func<float3,float3> rotate=p=>new float3(p.x*math.cos(angle)-p.z*math.sin(angle),p.y,p.x*math.sin(angle)+p.z*math.cos(angle));
   var first=rotate(new float3(3,0,6));var last=rotate(new float3(6,0,3));object[] args={first,rotate(new float3(0,0,-1)),last,rotate(new float3(-1,0,0)),default(Bezier4x3)};
   Check((bool)corner.Invoke(null,args),"valid corner connects adjacent curbs");var curve=(Bezier4x3)args[4];Check(math.distance(curve.a,first)<.001f&&math.distance(curve.d,last)<.001f,"corner endpoints retain configured offset");
   for(int sample=0;sample<=10;sample++){var p=MathUtils.Position(curve,sample/10f);Check(math.all(math.isfinite(p)),"finite corner");Check(math.length(p.xz)>=3,"corner never cuts across junction center");}
  }
  foreach(var bad in new[]{new float3(float.NaN,0,0),new float3(0,20,0),new float3(100,0,100)}){object[] args={new float3(3,0,6),new float3(0,0,-1),bad,new float3(-1,0,0),default(Bezier4x3)};Check(!(bool)corner.Invoke(null,args),"unsafe corner rejected");}
  Console.WriteLine(n+" parking precedence, median, visibility and junction geometry checks passed.");
 }
}
