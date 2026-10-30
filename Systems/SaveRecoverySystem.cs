using System.Threading.Tasks;
using Colossal;
using Game;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class SaveRecoveryPolicy
    {
        internal static bool CanRecover(bool saving,bool observedQueue,bool completed,bool sameWorld)
            =>saving&&observedQueue&&completed&&sameWorld;
    }

    // Observe the supported SaveLoadGame queue completion, never a time limit.
    // Only managed Tasks are retained. Recovery runs on the game's UI thread.
    public partial class SaveRecoverySystem:GameSystemBase
    {
        private Task completion;
        internal void Observe()
        {
            completion=null;
            foreach(var name in TaskManager.instance.taskQueue)
                if(name=="SaveLoadGame")
                {
                    var task=TaskManager.instance.taskQueue.Complete(name);
                    if(!task.IsCompleted)completion=task;
                    break;
                }
        }
        internal void Reset()=>completion=null;
        protected override void OnUpdate()
        {
            if(!Mod.Saving){completion=null;return;}
            if(!SaveRecoveryPolicy.CanRecover(Mod.Saving,completion!=null,completion?.IsCompleted??false,World.IsCreated&&World==Unity.Entities.World.DefaultGameObjectInjectionWorld))return;
            completion=null;
            Mod.Log.Warn("Native SaveLoadGame queue completed without Curbside's completion event. Recovering suspension; save success is not inferred.");
            try{Recover(World);}
            catch(System.Exception error){Mod.Log.Error(error,"Save recovery could not safely join native work. Suspension retained; reload the city before further changes.");}
        }
        internal static void Recover(World world)
        {
            if(world==null||!world.IsCreated)return;
            // The native task has ended, but join any remaining readers/writer
            // handles before restoring interpolation after its exceptional path.
            world.EntityManager.CompleteAllTrackedJobs();
            var writer=world.GetExistingSystemManaged<Game.Serialization.WriteSystem>();
            if(writer!=null)writer.writeDependency.Complete();
            var effects=world.GetExistingSystemManaged<Game.Effects.EffectControlSystem>();
            if(effects!=null){effects.GetEnabledData(false,out var readers);readers.Complete();}
            var culling=world.GetExistingSystemManaged<Game.Rendering.PreCullingSystem>();
            if(culling!=null){culling.GetCullingData(false,out var readers);readers.Complete();}
            Mod.ResetSaveGuard();
            world.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.EndSaveWindow();
        }
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){Reset();base.OnGamePreload(purpose,mode);}
        protected override void OnDestroy(){Reset();base.OnDestroy();}
    }
}
