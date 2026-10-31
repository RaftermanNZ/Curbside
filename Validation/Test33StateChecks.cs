using System;using System.Reflection;using Unity.Entities;
class Test33StateChecks
{
 static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static void Main(){var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");var flags=BindingFlags.Static|BindingFlags.NonPublic;var policy=assembly.GetType("MedianGrassReplacement.Systems.SegmentUpgradePolicy");
 var hides=policy.GetMethod("Hides",flags,null,new[]{typeof(byte),typeof(bool),typeof(bool)},null);
 for(byte mask=0;mask<8;mask++)foreach(bool center in new[]{false,true})foreach(bool edge in new[]{false,true}){
 bool actual=(bool)hides.Invoke(null,new object[]{mask,center,edge});bool expected=(mask&1)!=0||!center&&((mask&2)!=0||edge&&(mask&4)!=0);Check(actual==expected,"visibility role matrix");
 if(center&&(mask&1)==0)Check(!actual,"lane/edge visibility must preserve center/median paint");
 if(mask==0)Check(!actual,"reset leaves baseline paint visible");}
 var select=policy.GetMethod("Selection",flags);var known=policy.GetMethod("Known",flags);
 foreach(string name in new[]{"All","Lanes","Edges","ResetLines","ResetParking","ResetAll"}){string asset=(string)policy.GetField(name,flags).GetRawConstantValue();Check((bool)known.Invoke(null,new object[]{asset}),"all new tools routed");byte selection=(byte)select.Invoke(null,new object[]{asset});Check((selection==0)==name.StartsWith("Reset"),"reset is action, visibility is persistent selection");}
 var centerPolicy=assembly.GetType("MedianGrassReplacement.Systems.CenterlineUpgradePolicy");var offset=centerPolicy.GetMethod("Offset",flags);
 Check((float)offset.Invoke(null,new object[]{(byte)1,0})==0,"single stripe centered");Check((float)offset.Invoke(null,new object[]{(byte)2,0})<0&&(float)offset.Invoke(null,new object[]{(byte)2,1})>0,"double choice produces distinct stripes around center");
 foreach(string name in new[]{"MGRSegmentLineVisibility","MGRCenterlineStyle","MGRStreetLightingPrevious"}){var type=assembly.GetType("MedianGrassReplacement.Systems."+name);Check(typeof(IComponentData).IsAssignableFrom(type),"intent is ECS component");Check(type.GetMethod("Serialize")!=null&&type.GetMethod("Deserialize")!=null,"intent survives save/load");}
 Check(assembly.GetType("MedianGrassReplacement.Systems.RoadAttachmentTransferSystem").GetNestedType("Selection",BindingFlags.NonPublic).GetField("LineVisibility",BindingFlags.Instance|BindingFlags.NonPublic)!=null,"visibility travels with road attachment state");
 Console.WriteLine(checks+" line-role, reset-action, stripe and persistence contract checks passed.");}
}
