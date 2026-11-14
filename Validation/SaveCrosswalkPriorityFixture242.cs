using System;
using System.Collections.Generic;
using MedianGrassReplacement.Systems;

namespace Unity.Entities
{
    public struct Entity:IEquatable<Entity>
    {
        public int Index,Version;
        public bool Equals(Entity other)=>Index==other.Index&&Version==other.Version;
        public override bool Equals(object value)=>value is Entity other&&Equals(other);
        public override int GetHashCode()=>Index*397^Version;
    }
}
namespace Game.Common { }
namespace Game.Net { public struct PedestrianLane { } }
namespace Game.Tools { public struct Temp { } }
namespace MedianGrassReplacement
{
    static class Mod {internal static LogSink Log=new LogSink();}
    class LogSink {public void Info(string message){}}
}
namespace MedianGrassReplacement.Systems
{
    public partial class IntersectionPaintSystem
    {
        private class Replacement{internal bool RoutedCrossing;}
        private readonly Dictionary<Unity.Entities.Entity,Replacement> _replacements=new Dictionary<Unity.Entities.Entity,Replacement>();
        private readonly HashSet<Unity.Entities.Entity> _processedClicks=new HashSet<Unity.Entities.Entity>();
        private readonly HashSet<Unity.Entities.Entity> alive=new HashSet<Unity.Entities.Entity>();
        private readonly List<Unity.Entities.Entity> reconciled=new List<Unity.Entities.Entity>();
        private readonly FixtureManager EntityManager=new FixtureManager();
        private bool Alive(Unity.Entities.Entity entity)=>alive.Contains(entity);
        private void Reconcile(Unity.Entities.Entity entity){if(Alive(entity))reconciled.Add(entity);}
        class FixtureManager
        {
            internal readonly HashSet<Unity.Entities.Entity> pedestrian=new HashSet<Unity.Entities.Entity>(),temporary=new HashSet<Unity.Entities.Entity>();
            internal bool HasComponent<T>(Unity.Entities.Entity entity)=>typeof(T)==typeof(Game.Net.PedestrianLane)?pedestrian.Contains(entity):temporary.Contains(entity);
        }
        static int assertions;
        static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
        static Unity.Entities.Entity E(int index,int version=1)=>new Unity.Entities.Entity{Index=index,Version=version};
        public static void Run()
        {
            foreach(int count in new[]{0,1,7,8,9,17,128,5075,5280})for(int pass=0;pass<20;pass++)
            {
                var system=new IntersectionPaintSystem();
                for(int i=0;i<count;i++)
                {var e=E(i);system._replacements[e]=new Replacement{RoutedCrossing=true};system.alive.Add(e);system.EntityManager.pedestrian.Add(e);}
                // Non-crosswalks and temporary sources must not enter this path.
                system._replacements[E(100001)]=new Replacement{RoutedCrossing=false};system.alive.Add(E(100001));
                system._replacements[E(100002)]=new Replacement{RoutedCrossing=true};system.alive.Add(E(100002));system.EntityManager.pedestrian.Add(E(100002));system.EntityManager.temporary.Add(E(100002));
                system.CaptureCrosswalkRestorePriority();system.CaptureCrosswalkRestorePriority();
                Check(system.savedCrossings.Count==count,"Save re-entry duplicated or admitted foreign work");
                system._replacements.Clear(); // Exact operation causing lost priority in old cleanup.
                int frames=0;
                while(system.savedCrossings.Count>0)
                {
                    system._processedClicks.Clear();int clicks=frames%9;int processed=clicks;
                    int before=system.savedCrossings.Count,submitted=system.reconciled.Count;
                    system.ProcessSavedCrosswalkPriority(ref processed);
                    Check(processed<=8,"Existing priority cap exceeded");
                    Check(before-system.savedCrossings.Count<=8-clicks,"User clicks lost their reserved slots");
                    Check(system.reconciled.Count-submitted<=8-clicks,"More than bounded work submitted");
                    Check(processed==clicks+before-system.savedCrossings.Count,"Work budget not charged");
                    frames++;Check(frames<count*2+10,"Priority queue stalled");
                }
                Check(system.reconciled.Count==count,"Missing/duplicate post-save source");
                int idle=0;system.ProcessSavedCrosswalkPriority(ref idle);Check(idle==0,"Idle queue does work");
                system.savedCrossings.Clear();system.CaptureCrosswalkRestorePriority();Check(system.savedCrossings.Count==0,"No-custom-crosswalk save did work");
            }
            var stale=new IntersectionPaintSystem();var old=E(1);var reused=E(1,2);
            stale._replacements[old]=new Replacement{RoutedCrossing=true};stale.alive.Add(old);stale.EntityManager.pedestrian.Add(old);stale.CaptureCrosswalkRestorePriority();
            stale.alive.Remove(old);stale.alive.Add(reused);int budget=0;stale.ProcessSavedCrosswalkPriority(ref budget);
            Check(stale.reconciled.Count==0,"Recycled entity index was targeted");
            Console.WriteLine("PASS: "+assertions+" production save-crosswalk priority checks; native rendering and serialization are not executed.");
        }
    }
}
class SaveCrosswalkPriorityFixture242 {static void Main()=>IntersectionPaintSystem.Run();}
