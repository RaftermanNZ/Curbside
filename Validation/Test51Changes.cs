using System;using System.Collections;using System.Reflection;using Colossal.Mathematics;using Unity.Mathematics;
class Test52Changes
{
 static Assembly a;static int n;static BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
 static object Call(string type,string method,params object[] args)=>a.GetType("MedianGrassReplacement.Systems."+type,true).GetMethod(method,flags).Invoke(null,args);
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
 static void Main(){
  a=Assembly.LoadFrom("MedianGrassReplacement.dll");
  foreach(byte style in new byte[]{1,2,4,5,6,7})foreach(bool theme in new[]{false,true}){
   bool yellow=(bool)Call("CenterlineUpgradePolicy","YellowPaint",style,theme);Check(yellow==(style>=6||style<=2&&theme),"explicit colors survive theme changes");
   byte pattern=(byte)Call("CenterlineUpgradePolicy","Pattern",style);Check(pattern==(style==2||style==5||style==7?2:1),"saved legacy and explicit single/double shapes remain stable");
   string asset=(string)Call("CenterlineUpgradePolicy","Asset",style);Check((byte)Call("CenterlineUpgradePolicy","Style",asset)==style,"exact saved style round trip");
  }
  Check((byte)Call("CenterlineUpgradePolicy","Pattern",(byte)3)==0,"removed state never becomes a single line");
  foreach(float length in new[]{.2f,1f,10f,80f,300f})foreach(byte style in new byte[]{3,4}){
   var path=new Bezier4x3(new float3(0,0,0),new float3(0,0,length/3),new float3(0,0,length*2/3),new float3(0,0,length));
   var strokes=(IList)Call("LaneDividerPolicy","Strokes",path,style);Check(strokes.Count>0&&strokes.Count<=512,"custom dash geometry bounded");float previous=-1;
   foreach(Bezier4x3 stroke in strokes){float dash=MathUtils.Length(stroke);Check(dash>0&&dash<=(style==3?1:6)+.002f,"requested short/long length");Check(stroke.a.z>previous&&stroke.d.z<=length+.002f,"nonoverlapping dashes remain inside path");previous=stroke.d.z;}
  }
  foreach(byte style in new byte[]{0,1,2,3,4,255})Check((bool)Call("LaneDividerPolicy","Applied",style)==(style==1||style==3||style==4),"remove state distinguished from both new styles");
  var assemblyType=a.GetType("MedianGrassReplacement.Systems.UpgradeAssetsSystem");var names=(string[])assemblyType.GetField("Names",flags).GetValue(null);Check(!Array.Exists(names,name=>name.IndexOf("Split",StringComparison.OrdinalIgnoreCase)>=0),"unsafe splitter launcher removed");
  Console.WriteLine(n+" explicit center-color/shape, bounded dash geometry and removed-splitter checks passed.");
 }
}
