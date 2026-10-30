using Game;

using Game.Common;

using Unity.Collections;

using Unity.Entities;

namespace MedianGrassReplacement.Systems

{

    public partial class VisualPowerlineGuardSystem : GameSystemBase

    {

        private EntityQuery _removed;

        protected override void OnCreate()

        {

            base.OnCreate();_removed=GetEntityQuery(ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.ReadOnly<Deleted>());RequireForUpdate(_removed);

        }

        protected override void OnUpdate()

        {

            if(Mod.Saving) return;Dependency.Complete();var system=World.GetOrCreateSystemManaged<VisualPowerlineSystem>();

            using(var removed=_removed.ToEntityArray(Allocator.Temp)) foreach(var entity in removed) system.NotifyRemoved(entity);

        }

    }

}

