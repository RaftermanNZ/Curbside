using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Unity.Entities;



namespace MedianGrassReplacement.Systems

{

    /// <summary>

    /// Narrow event source for lazily-instantiated RoadGrass.

    ///

    /// This system observes only committed road edges whose selected network

    /// composition is in the transient BatchesUpdated renderer-refresh state.

    /// It covers both roads created with grass already configured and existing

    /// roads later upgraded to add grassy medians/sidewalks.

    ///

    /// IMPORTANT: this system never searches Unity materials and performs no

    /// filesystem I/O. It only reports render-composition activity to

    /// MedianMaterialSystem, which coalesces an entire burst before allowing one

    /// material lookup.

    /// </summary>

    public partial class RoadGrassCompositionTriggerSystem : GameSystemBase

    {

        private EntityQuery _compositionBatchQuery;

        private bool _gameOrEditorActive;



        protected override void OnCreate()

        {

            base.OnCreate();



            _compositionBatchQuery = GetEntityQuery(new EntityQueryDesc

            {

                All = new[]

                {

                    ComponentType.ReadOnly<Edge>(),

                    ComponentType.ReadOnly<Composition>(),

                    ComponentType.ReadOnly<BatchesUpdated>()

                },

                None = new[]

                {

                    ComponentType.ReadOnly<Deleted>(),

                    ComponentType.ReadOnly<Game.Tools.Temp>()

                }

            });



            // When no committed composition is in BatchesUpdated, ECS skips this

            // system completely. There is no normal per-frame road query work.

            RequireForUpdate(_compositionBatchQuery);

        }



        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            _gameOrEditorActive = (mode & GameMode.GameOrEditor) != GameMode.None;

        }



        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdatePerformanceCore();}finally{ModDiagnostics.RuntimeCosts.End("RoadGrassCompositionTriggerSystem",started);}
        }
        private void UpdatePerformanceCore()
        {

            if(_gameOrEditorActive)GlobalSurfaceMaterialSystem.NotifyRenderActivity();

            if (!_gameOrEditorActive || !MedianMaterialSystem.NeedsDiscovery)

            {

                return;

            }



            // Do not probe here. BatchesUpdated may persist across many rendering

            // frames while one road operation settles. Repeated notifications

            // simply move the debounce boundary forward.

            MedianMaterialSystem.NotifyCompositionRenderActivity(

                "road composition reached BatchesUpdated after BatchRendererSystem");

        }

    }

}
