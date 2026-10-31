using System;using System.Collections;using System.Reflection;using Colossal.Mathematics;using Unity.Mathematics;
class Test52Surfaces
{
 static int n;static Assembly a;static BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
 static Type T(string name)=>a.GetType("MedianGrassReplacement.Systems."+name,true);
 static object Call(string type,string method,params object[] args)=>T(type).GetMethod(method,flags).Invoke(null,args);
 static void Main(){
  a=Assembly.LoadFrom("MedianGrassReplacement.dll");var slots=T("MGRSurfaceSlots");
  Check(slots.GetFields().Length==2,"saved surface intent has exactly two fields");foreach(var f in slots.GetFields())Check(f.FieldType==typeof(byte),"no saved private asset/entity references");
  foreach(byte road in new byte[]{0,1,2})foreach(byte walk in new byte[]{0,1,2}){
   object value=Activator.CreateInstance(slots);value=Call("SurfaceUpgradePolicy","Set",value,false,road);value=Call("SurfaceUpgradePolicy","Set",value,true,walk);
   Check((byte)slots.GetField("Road").GetValue(value)==road&&(byte)slots.GetField("Sidewalk").GetValue(value)==walk,"categories preserve each other");
   value=Call("SurfaceUpgradePolicy","Set",value,false,(byte)0);Check((byte)slots.GetField("Road").GetValue(value)==0&&(byte)slots.GetField("Sidewalk").GetValue(value)==walk,"right-click road restore preserves sidewalk");
  }
  foreach(var name in new[]{"MGR Road Surface A","MGR Road Surface B","MGR Sidewalk Surface A","MGR Sidewalk Surface B"})Check((byte)Call("SurfaceUpgradePolicy","Slot",name)==(name.EndsWith("A")?1:2),"four distinct slot mappings");
  foreach(var name in new[]{"grass asphalt","curb concrete","road marking","rail","water","gravel"})Check((int)Call("SurfaceUpgradePolicy","Kind",name,"Road",true)==0,"non-pavement material kept native");
  Check((int)Call("SurfaceUpgradePolicy","Kind","Asphalt","Road",true)==1,"road albedo recognized");Check((int)Call("SurfaceUpgradePolicy","Kind","Sidewalk Concrete","Road",true)==2,"sidewalk independently recognized");Check((int)Call("SurfaceUpgradePolicy","Kind","unknown","Road",true)==0,"unknown material retained");
  Check((bool)Call("SurfaceUpgradePolicy","FitsBudget",4096,4096,0L),"4096 texture fits world budget");foreach(int invalid in new[]{0,-1,4097,int.MaxValue})Check(!(bool)Call("SurfaceUpgradePolicy","FitsBudget",invalid,4096,0L),"invalid dimensions rejected");Check(!(bool)Call("SurfaceUpgradePolicy","FitsBudget",4096,4096,256L*1024*1024),"retired texture memory included in budget");
  var line=new Bezier4x3(new float3(0,0,0),new float3(0,0,10),new float3(0,0,20),new float3(0,0,30));
  foreach(float width in new[]{.5f,2f,6f})foreach(var range in new[]{new float2(0,1),new float2(.2f,.6f),new float2(.8f,1)}){
   var strokes=(IList)Call("ParkingHatchPolicy","Build",line,width,range.x,range.y);if(30*(range.y-range.x)<Math.Max(2f,width*1.5f)){Check(strokes.Count==0,"small box hatch is rejected");continue;}Check(strokes.Count>4&&strokes.Count<=132,"bounded hatch output");
   foreach(Bezier4x3 stroke in strokes)foreach(var point in new[]{stroke.a,stroke.b,stroke.c,stroke.d}){Check(math.all(math.isfinite(point)),"finite hatch geometry");Check(math.abs(point.x)<=width*.5f+.001f&&point.z>=30*range.x-.001f&&point.z<=30*range.y+.001f,"hatches remain inside native parking strip");}
  }
  foreach(float width in new[]{float.NaN,float.PositiveInfinity,0f,7f})Check(((IList)Call("ParkingHatchPolicy","Build",line,width,0f,1f)).Count==0,"invalid hatch rejected");
  var markers=Call("ParkingMarkPlacement","BayMarkers",new float[]{0,.25f,.5f,.75f,1},new[]{true,false,true,true},0f,1f);var style=T("ParkingBayStyle");
  var open=(IList)Call("ParkingMarkPlacement","BuildBayStyle",line,0f,1f,markers,true,Enum.ToObject(style,4),2f,false,false,false);
  var closed=(IList)Call("ParkingMarkPlacement","BuildBayStyle",line,0f,1f,markers,true,Enum.ToObject(style,4),2f,false,false,true);
  Check(closed.Count==open.Count+2,"entrance closure toggle adds only the two access terminals");
  Console.WriteLine(n+" surface-slot isolation, fallback/budget, hatch bounds and optional entrance-closure checks passed.");
 }
}
