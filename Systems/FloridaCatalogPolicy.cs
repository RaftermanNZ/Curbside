using System;
namespace GardenCitySignals
{
 internal static class FloridaCatalogPolicy
 {
  internal static byte NormalizeStyle(byte value)=>value<=4?value:(byte)0;
  internal static bool Visible(bool originalReady,bool floridaReady)=>originalReady&&floridaReady;
  internal static bool Expired(double now,double start,bool expected)=>now-start>=(expected?120:30);
  internal static bool Accept(int count,bool ambiguous,bool originalReady,bool localCombined,string originalOwner,string[] owners)
  {
   if(count!=4||ambiguous||!originalReady||owners==null||owners.Length!=4)return false;
   if(localCombined)return true;
   if(string.IsNullOrWhiteSpace(originalOwner))return false;
   foreach(var owner in owners)if(!string.Equals(owner,originalOwner,StringComparison.Ordinal))return false;
   return true;
  }
 }
}
