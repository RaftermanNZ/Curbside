using System;using System.Reflection;
class MedianShoulderChecks
{
 static int count;static void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
 static void Main()
 {
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var f=BindingFlags.Static|BindingFlags.NonPublic;
  var policy=a.GetType("MedianGrassReplacement.Systems.CenterlineUpgradePolicy",true);
  var fit=policy.GetMethod("MedianShoulder",f);var pattern=policy.GetMethod("Pattern",f);var center=policy.GetMethod("MedianCenter",f);
  Func<float,float,float,float,float,bool> accepts=(x,l,r,c,w)=>(bool)fit.Invoke(null,new object[]{x,l,r,c,w});
  foreach(float shift in new[]{-100f,0f,100f})foreach(float sign in new[]{-1f,1f})
  {
   float curb=shift+sign*1.5f,lane=shift+sign*3.4f,left=shift+(sign<0?-5.5f:1.5f),right=shift+(sign<0?-1.5f:5.5f);
   foreach(byte style in new byte[]{1,2,4,5,6,7,8,9,10,11,12,13,14,15})
   {
    byte shape=(byte)pattern.Invoke(null,new object[]{style});float x=(float)center.Invoke(null,new object[]{curb,sign<0,style,.3f,.15f});
    Check(accepts(x,left,right,lane,3f),"Manual median style rejected on verified paved shoulder: "+style);
    if(shape==1)Check(Math.Abs(x-lane)>1.5f,"Counterexample must lie outside the driving lane, unlike double's center");
   }
  }
  Check(!accepts(1.4f,1.5f,5.5f,3.4f,3f),"Must not cross the physical pavement boundary");
  Check(!accepts(.5f,0,5.5f,3.4f,3f),"Must not bridge an arbitrary gap to a distant lane");
  Check(!accepts(1.8f,1.5f,5.5f,-3.4f,3f),"Opposite carriageway cannot authorize the shoulder");
  Check(!accepts(1.5f,1.5f,5.5f,3.4f,3f),"Boundary itself is excluded");
  foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})Check(!accepts(invalid,1.5f,5.5f,3.4f,3f),"Nonfinite target accepted");
  Check(!accepts(1.8f,5.5f,1.5f,3.4f,3f)&&!accepts(1.8f,1.5f,5.5f,3.4f,0),"Invalid width/envelope accepted");
  Console.WriteLine("PASS: "+count+" median shoulder/color-style geometry cases, reflected sides, translations and unsafe-gap rejection. Native in-game placement still requires confirmation.");
 }
}
