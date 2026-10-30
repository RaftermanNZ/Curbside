using System;
using System.Linq;
namespace GardenCitySignals
{
 // A single required-model catalog keeps import, pack detection and UI readiness
 // in sync. Legacy monolithic arms are not used by current signal assemblies.
 internal static class SignalModuleCatalog
 {
  internal static readonly string[] Names = new[]{
   "CSGHeadBlack","CSGHeadYellow","CSGHeadWire","CSGPedHead","CSGWirePole",
   "CSGLampArm","CSGSignPlate","CSGGardenLuminaire","CSGRigidLuminaire",
   "CSGWireLuminaire","CSGWireTimber","CSGLowerHeadBlack","CSGLowerHeadYellow",
   "CSGModularGardenPole","CSGModularRigidPole"
  }.Concat(Enumerable.Range(1,19).SelectMany(i=>new[]{
   "CSGModularGardenArm"+i.ToString("00"),"CSGModularRigidArm"+i.ToString("00")
  })).Concat(Enumerable.Range(1,8).Select(i=>"CSGWireSegment"+i.ToString("00"))).ToArray();
 }
}
