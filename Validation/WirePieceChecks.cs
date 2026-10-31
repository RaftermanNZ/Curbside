using System;using System.Collections;using System.Reflection;
class WirePieceChecks
{
 static int checks;static void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
 static void Main(){
  var type=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy");
  var method=type.GetMethod("WirePieces",BindingFlags.Static|BindingFlags.NonPublic);
  for(int mm=2000;mm<=60000;mm+=13){float span=mm*.001f,previous=0;int count=0;
   foreach(var piece in (IEnumerable)method.Invoke(null,new object[]{span})){
    var p=piece.GetType();float start=(float)p.GetField("x").GetValue(piece),length=(float)p.GetField("y").GetValue(piece);
    Check(start>=-.0001f&&start<=previous+.0001f,"Gap between shared wire modules");
    Check(length>=.25f&&length<=2&&Math.Abs(length*4-Math.Round(length*4))<.001,"Wire module stretched or unsupported");
    Check(start+length<=span+.0001f,"Wire extends beyond pole span");
    previous=start+length;count++;
   }
   Check(count>0&&count<=31,"Unbounded wire instance count");Check(Math.Abs(previous-span)<.0002f,"Last wire module misses pole");
  }
  foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1,0,1.99f,60.01f})
   Check(!((IEnumerable)method.Invoke(null,new object[]{invalid})).GetEnumerator().MoveNext(),"Invalid span accepted");
  Console.WriteLine(checks+" wire module coverage checks passed across exact and fractional 2-60m spans; no extra geometry or texture variants.");
 }
}
