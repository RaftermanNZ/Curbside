using System;using System.Collections.Generic;using Curbside.CornerStudy;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;using Transform=Game.Objects.Transform;
// Geometry/appearance stubs deliberately throw: the production cache-hit or
// pending-controller branch must not enter any of them. Full real planner and
// appearance methods are exercised separately by captured-junction tests.
namespace Curbside.CornerStudy
{
 public struct CurbSlice{public Colossal.Mathematics.Bezier4x3 Curb;}
 public class CornerProposal{public bool Fits;public string Reason;public float3 Position;}
 public static class CornerRule{public const float Setback=1.5f;public static List<CornerProposal> Calculate(IList<CurbSlice> s,float envelope,object walking=null,float setback=Setback,float fraction=.5f){throw new Exception("Unexpected geometry build during cached binding");}}
}
namespace MedianGrassReplacement.Systems
{
 public struct MGRSignalStyle:IComponentData{public byte Value;}
 internal sealed class CornerSupportList:List<float3>{internal bool UsesCornerRule;}
 internal static class GardenCityLightPolicy
 {
  internal static bool CacheWireLayouts(IList<FarSideLightPolicy.Approach> roads,IList<float3> supports,IList<int> sources,byte style){throw new Exception("Unexpected head rebuild during cached binding");}
  internal static bool TryWireLayout(FarSideLightPolicy.Approach road,float3 pole,IList<float3> supports,byte style,out float3 inward,out float[] heads,out int target){throw new Exception("Unexpected head fitting during cached binding");}
  internal const string AssetName="garden",MastAsset="rigid",FloridaAsset="florida";
  internal static bool ReadyStyle(PrefabSystem p,Unity.Entities.EntityManager em,string asset)=>Ready(p,em);
  internal static bool DiscoveryAllowed,ModuleReady;internal static int DiscoveryCalls;
  internal static bool Ready(PrefabSystem p,Unity.Entities.EntityManager m){if(!DiscoveryAllowed)throw new Exception("Unexpected module discovery on a cached layout");DiscoveryCalls++;return ModuleReady;}
  internal static bool AppearanceCandidate(FarSideLightPolicy.Approach r,float3 p,byte style){throw new Exception("Unexpected appearance fit");}
  internal static bool TryLayout(IList<float3> lanes,float2 outward,float3 p,out float3 inward,out float[] heads){throw new Exception("Unexpected lane fit");}
  internal static int WireNeighborForLayout(IList<float3> p,int source,float3 inward,bool angled){throw new Exception("Unexpected span fit");}
  internal static bool HasRequiredSpan(List<int2> spans,int a,int b){throw new Exception("Unexpected span build");}
  internal static bool PedestrianCornerEnvelope(byte style,out float envelope){throw new Exception("Unexpected envelope calculation");}
 }
 public partial class FarSideLightSystem
 {
  internal sealed class TestWorld{internal bool AllowLookup;internal T GetOrCreateSystemManaged<T>(){if(!AllowLookup)throw new Exception("Unexpected native system lookup on a cached layout");return default(T);}}
  internal TestWorld World=new TestWorld();
  private List<Entity> _primarySignals=new List<Entity>();private Dictionary<Entity,Entity> _supportLeaders=new Dictionary<Entity,Entity>();
  private Dictionary<Entity,int> _sourceApproaches=new Dictionary<Entity,int>();private Dictionary<Entity,Transform> _plannedSignals=new Dictionary<Entity,Transform>();
  private List<float3> _supportPositions=new CornerSupportList();private List<int2> _requiredWireSpans=new List<int2>();private int _virtualSupport;
  private Dictionary<Entity,int> nativeSources=new Dictionary<Entity,int>();
  private int SourceApproach(Entity e,IList<FarSideLightPolicy.Approach> roads)=>nativeSources[e];
  private Transform OriginalTransform(Entity e)=>new Transform(new float3(0,123,0),quaternion.identity);
  internal bool UnsupportedFixtureAllowed;internal int CornerReads;
  private bool ReadCornerSlices(Entity n,IList<FarSideLightPolicy.Approach> roads,out List<CurbSlice> slices){if(!UnsupportedFixtureAllowed)throw new Exception("Unexpected native corner read");CornerReads++;slices=null;return false;}
  internal void FixturePlan(Entity node)
  {
   EntityManager.Put(node,new MGRSignalStyle{Value=1});
   var plan=new WireCornerPlan{Style=1,Supports=new[]{new float3(-10,123,-10),new float3(10,123,-10),new float3(10,123,10),new float3(-10,123,10)},Pairs=new[]{new int2(0,1),new int2(1,2),new int2(2,3),new int2(3,0)}};
   for(int i=0;i<FixtureRoads.Count;i++)plan.RoadSlots[FixtureRoads[i].Road]=i;
   signalLayouts[node]=new SignalLayoutEntry{Roads=FixtureRoads,Wire=plan,WireAttemptStyle=1};
  }
  internal void FixtureActors(int seed,int count)
  {
   _primarySignals.Clear();_supportLeaders.Clear();nativeSources.Clear();
   for(int i=0;i<count;i++){var actor=new Entity{Index=seed+i,Version=1};_primarySignals.Add(actor);_supportLeaders[actor]=actor;nativeSources[actor]=i;}
   _supportPositions.Clear();_requiredWireSpans.Clear();_plannedSignals.Clear();_sourceApproaches.Clear();deferSignalApplication=false;
  }
  internal bool FixtureBind(Entity node)=>TryPrepareWireLayout(node,FixtureRoads);
  internal bool Deferred=>deferSignalApplication;internal int Bound=>_plannedSignals.Count;internal int Supports=>_supportPositions.Count;
  internal long Reused=>wirePlansReused;internal long Built=>wirePlansBuilt;
  internal void FixtureFailure(Entity node){signalLayouts[node].Wire=null;signalLayouts[node].WireFailure="unsupported fixture geometry";}
  internal void FixturePending(Entity node){signalLayouts[node].Wire=null;signalLayouts[node].WireFailure=null;EntityManager.Put(node,new MGRSignalStyle{Value=1});}
  internal bool Failed(Entity node)=>signalLayouts[node].WireFailure!=null;
 }
}
class CachedWireBinding234
{
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static void Main()
 {
  var system=new MedianGrassReplacement.Systems.FarSideLightSystem();var node=new Entity{Index=1,Version=1};
  for(int i=0;i<4;i++)system.FixtureRoads.Add(new MedianGrassReplacement.Systems.FarSideLightPolicy.Approach{Road=new Entity{Index=10+i,Version=1},Incoming=new List<float3>{new float3(i,123,0)}});
  system.FixturePlan(node);
  for(int i=0;i<1000;i++){system.FixtureActors(100+i*10,4);Check(system.FixtureBind(node)&&!system.Deferred&&system.Bound==4&&system.Supports==4,"Cached actor rebind failed");}
  Check(system.Reused==1000&&system.Built==0,"Rebound actors refitted a complete layout");
  foreach(int count in new[]{0,1,2,3}){system.FixtureActors(20000,count);Check(system.FixtureBind(node)&&system.Deferred,"Intermediate controller set was published");}
  system.FixtureActors(30000,4);Check(system.FixtureBind(node)&&!system.Deferred&&system.Supports==4,"Complete native controllers did not recover the retained layout");
  system.FixturePending(node);system.FixtureActors(35000,4);system.World.AllowLookup=true;
  MedianGrassReplacement.Systems.GardenCityLightPolicy.DiscoveryAllowed=true;
  Check(system.FixtureBind(node)&&system.Deferred&&!system.Failed(node)&&system.CornerReads==0,"Pending native modules entered fitting or became a permanent geometry failure");
  Check(MedianGrassReplacement.Systems.GardenCityLightPolicy.DiscoveryCalls==1,"Initial wire-only node failed to initialize module discovery");
  MedianGrassReplacement.Systems.GardenCityLightPolicy.ModuleReady=true;system.UnsupportedFixtureAllowed=true;
  system.FixtureActors(36000,4);Check(system.FixtureBind(node)&&system.Deferred&&system.Failed(node)&&system.CornerReads==1,"Unsupported surface was not retained as a geometry result");
  system.World.AllowLookup=false;MedianGrassReplacement.Systems.GardenCityLightPolicy.DiscoveryAllowed=false;
  for(int i=0;i<1000;i++){system.FixtureActors(40000+i*10,4);Check(system.FixtureBind(node)&&system.Deferred,"Cached geometry failure retried or published");}
  Check(system.CornerReads==1,"Unsupported surface was read again without a network change");
  Console.WriteLine("PASS: production wire-binding source reused layout for 1000 actor generations; incomplete controllers deferred; first-use module discovery runs before fitting; module waits stay retryable; 1000 unchanged unsupported inputs skip refitting. Controlled storage, not native ECS execution.");
 }
}
