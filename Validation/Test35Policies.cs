using System;using System.Reflection;using Game.Prefabs;using Unity.Mathematics;
class Test35Policies
{
 static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static void Main(){var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var f=BindingFlags.Static|BindingFlags.NonPublic;
 var shared=a.GetType("MedianGrassReplacement.Systems.CenterlineUpgradePolicy").GetMethod("TrySharedLane",f);
 foreach(float x in new[]{-3f,0,4f})foreach(float y in new[]{0f,3f})foreach(var flags in new[]{LaneFlags.Road,LaneFlags.Road|LaneFlags.Twoway,LaneFlags.Road|LaneFlags.Twoway|LaneFlags.Master,LaneFlags.Road|LaneFlags.Twoway|LaneFlags.Underground,LaneFlags.Twoway}){object[] args={flags,new float3(x,y,0),0f,0f};bool accepted=(bool)shared.Invoke(null,args);Check(accepted==(flags==(LaneFlags.Road|LaneFlags.Twoway)),"only valid shared road lanes use alley fallback");if(accepted)Check((float)args[2]==x&&(float)args[3]==y,"shared marking uses actual composed lane center/elevation");}
 foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){object[] args={LaneFlags.Road|LaneFlags.Twoway,new float3(0,invalid,0),0f,0f};Check(!(bool)shared.Invoke(null,args),"invalid alley geometry rejected");}
 var spacing=a.GetType("MedianGrassReplacement.Systems.CenterlineUpgradePolicy").GetMethod("HalfSpacing",f);
 Check(math.abs((float)spacing.Invoke(null,new object[]{.45f,.15f})-.15f)<.0001f,"join stripe offsets follow native paired width minus single width");
 foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1f,0f,4f})Check((float)spacing.Invoke(null,new object[]{bad,.15f})==.15f,"invalid or implausible mesh width uses bounded spacing");
 var modes=a.GetType("MedianGrassReplacement.Systems.StreetLightingPolicy").GetMethod("SharedMode",f);
 Func<byte,byte,byte> combine=(x,y)=>(byte)modes.Invoke(null,new object[]{x,y});
 for(byte x=0;x<3;x++)for(byte y=0;y<3;y++)for(byte z=0;z<3;z++){byte expected=x==2||y==2||z==2?(byte)2:x==1||y==1||z==1?(byte)1:(byte)0;Check(combine(combine(x,y),z)==expected,"shared node removal wins");Check(combine(x,combine(y,z))==expected&&combine(x,y)==combine(y,x),"junction result independent of road iteration order");}
 var setting=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(a.GetType("MedianGrassReplacement.Setting"));var theme=setting.GetType().GetProperty("EuropeanMarkings");var color=setting.GetType().GetProperty("MarkingToolColor");foreach(bool white in new[]{true,false,true}){theme.SetValue(setting,white);Check((bool)theme.GetValue(setting)==white,"theme toggle roundtrip");Check((string)color.GetValue(setting)==(white?"White":"Yellow"),"theme colour independent of stripe count");}
 Console.WriteLine(checks+" shared-lane geometry, shared-lamp priority and theme-setting checks passed.");}
}
