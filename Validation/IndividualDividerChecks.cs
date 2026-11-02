using System;
using System.Reflection;
using Colossal.Mathematics;
using Unity.Mathematics;
class IndividualDividerChecks
{
 static int count;static void Check(bool value,string reason){count++;if(!value)throw new Exception(reason);}
 static void Main()
 {
  var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var type=a.GetType("MedianGrassReplacement.Systems.IndividualDividerPolicy",true);
  var flags=BindingFlags.Static|BindingFlags.NonPublic;var make=type.GetMethod("Intent",flags);var matches=type.GetMethod("Matches",flags);var covers=type.GetMethod("Covers",flags);
  for(int mirror=-1;mirror<=1;mirror+=2)for(int rotation=0;rotation<8;rotation++)foreach(float translate in new[]{-100f,0f,100f})
  {
   float angle=rotation*math.PI/4;
   float3 Point(float x,float z)=>new float3(translate+mirror*x*math.cos(angle)-z*math.sin(angle),2f,mirror*x*math.sin(angle)+z*math.cos(angle));
   var path=new Bezier4x3(Point(0,0),Point(0,10),Point(0,20),Point(0,30));var reverse=new Bezier4x3(path.d,path.c,path.b,path.a);
   for(byte style=1;style<=12;style++)
   {
    object intent=make.Invoke(null,new object[]{path,style});
    Check((bool)matches.Invoke(null,new object[]{intent,path}),"Saved selected path lost its style");
    Check((bool)matches.Invoke(null,new object[]{intent,reverse}),"Reversed road path lost its intent");
    var adjacent=new Bezier4x3(Point(2,0),Point(2,10),Point(2,20),Point(2,30));
    Check(!(bool)matches.Invoke(null,new object[]{intent,adjacent}),"Adjacent bike/car divider changed");
   }
   foreach(float offset in new[]{0f,.15f,.44f,.46f,2f})
   {
    var stroke=new Bezier4x3(Point(offset,5),Point(offset,8),Point(offset,11),Point(offset,14));
    Check((bool)covers.Invoke(null,new object[]{path,stroke})==(offset<.45f),"Legacy divider stroke coverage escaped its path");
   }
   var crossing=new Bezier4x3(Point(-2,15),Point(-1,15),Point(1,15),Point(2,15));Check(!(bool)covers.Invoke(null,new object[]{path,crossing}),"Crossing path was erased");
   var bad=path;bad.c.x=float.NaN;object value=make.Invoke(null,new object[]{path,(byte)1});Check(!(bool)matches.Invoke(null,new object[]{value,bad}),"Invalid curve matched saved intent");
  }
  var intentType=a.GetType("MedianGrassReplacement.Systems.MGRIndividualDivider",true);
  foreach(var f in intentType.GetFields()){Check(f.FieldType==typeof(float3)||f.FieldType==typeof(byte),"Saved intent holds an entity/native reference");}
  for(int style=0;style<256;style++)
  {var path=new Bezier4x3(new float3(0,0,0),new float3(0,0,1),new float3(0,0,2),new float3(0,0,3));object intent=make.Invoke(null,new object[]{path,(byte)style});Check((bool)matches.Invoke(null,new object[]{intent,path})==(style>=1&&style<=12||style>=254),"Invalid style became active");}
  Console.WriteLine("PASS: "+count+" independent divider path, reflected/rotated/translated selection, adjacent/crossing isolation and saved-data checks. Rendering/road regeneration still require game validation.");
 }
}
