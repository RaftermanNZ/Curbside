using System;
namespace MedianGrassReplacement.Systems
{
 internal static class NetworkTextureScope
 {
  internal static bool SharedParkingAtlas(string name,int width,int height)
   =>width==4096&&height==4096&&!string.IsNullOrEmpty(name)&&name.StartsWith("ParkingLane7_BaseColor",StringComparison.Ordinal);
 }
}
