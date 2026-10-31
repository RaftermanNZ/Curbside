using System;
using System.Collections.Generic;
using MedianGrassReplacement.Systems;

internal static class Trace
{
    internal static readonly List<string> Calls = new List<string>();
    internal static bool Fail;
    internal static void Assert(bool condition) { if (!condition) throw new Exception("Guard contract failed: " + string.Join(",", Calls)); }
}
namespace Unity.Entities
{
    public class World
    {
        public bool IsCreated = true;
        public readonly EntityManager EntityManager = new EntityManager();
        public readonly Dictionary<Type,object> Systems = new Dictionary<Type,object>();
        public T GetExistingSystemManaged<T>() where T:class => Systems.TryGetValue(typeof(T),out var s)?s as T:null;
    }
    public class EntityManager { public void CompleteAllTrackedJobs(){Trace.Calls.Add("entities");} }
}
namespace Game.Rendering
{
    public class ObjectInterpolateSystem
    {
        private bool enabled=true;
        public bool Enabled { get=>enabled; set {enabled=value;Trace.Calls.Add(value?"resume":"suspend");} }
    }
    public struct Fence { public string Name; public void Complete(){Trace.Calls.Add(Name);if(Trace.Fail)throw new InvalidOperationException("Injected fence failure");} }
    public class PreCullingSystem { public void GetCullingData(bool readOnly,out Fence fence){Trace.Assert(!readOnly);fence=new Fence{Name="culling"};} }
}
namespace Game.Effects { public class EffectControlSystem { public void GetEnabledData(bool readOnly,out Game.Rendering.Fence fence){Trace.Assert(!readOnly);fence=new Game.Rendering.Fence{Name="effects"};} } }
namespace MedianGrassReplacement { internal static class Mod { internal static readonly Logger Log=new Logger(); } internal class Logger { public void Info(string text){} } }

internal static class Program
{
    private static Unity.Entities.World World(bool enabled=true)
    {
        var w=new Unity.Entities.World();w.Systems[typeof(Game.Rendering.ObjectInterpolateSystem)]=new Game.Rendering.ObjectInterpolateSystem{Enabled=enabled};
        w.Systems[typeof(Game.Rendering.PreCullingSystem)]=new Game.Rendering.PreCullingSystem();w.Systems[typeof(Game.Effects.EffectControlSystem)]=new Game.Effects.EffectControlSystem();Trace.Calls.Clear();return w;
    }
    public static void Main()
    {
        var w=World();SignalSaveAnimationGuard.Begin(w);SignalSaveAnimationGuard.Begin(w);
        Trace.Assert(string.Join(",",Trace.Calls)=="suspend,culling,effects,entities");
        SignalSaveAnimationGuard.End();SignalSaveAnimationGuard.End();Trace.Assert(Trace.Calls.Count==5&&Trace.Calls[4]=="resume");
        w=World(false);SignalSaveAnimationGuard.Begin(w);SignalSaveAnimationGuard.End();Trace.Assert(!w.GetExistingSystemManaged<Game.Rendering.ObjectInterpolateSystem>().Enabled);
        w=World();Trace.Fail=true;bool failed=false;try{SignalSaveAnimationGuard.Begin(w);}catch(InvalidOperationException){failed=true;}Trace.Fail=false;
        Trace.Assert(failed&&w.GetExistingSystemManaged<Game.Rendering.ObjectInterpolateSystem>().Enabled);
        SignalSaveAnimationGuard.Begin(w);SignalSaveAnimationGuard.End();Trace.Assert(w.GetExistingSystemManaged<Game.Rendering.ObjectInterpolateSystem>().Enabled);
        w=World();SignalSaveAnimationGuard.Begin(w);w.IsCreated=false;int before=Trace.Calls.Count;SignalSaveAnimationGuard.End();Trace.Assert(Trace.Calls.Count==before);
        SignalSaveAnimationGuard.Begin(null);SignalSaveAnimationGuard.Begin(new Unity.Entities.World());SignalSaveAnimationGuard.End();
        Console.WriteLine("PASS: save guard fence order, duplicate callbacks, prior disabled state, exception recovery, disposed world and absent system.");
    }
}
