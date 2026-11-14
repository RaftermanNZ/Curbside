using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Invoked by IntersectionPaintSystem only; never registered as a game phase
    // callback. A skipped invocation must not advance LastSystemVersion.
    [DisableAutoCreation]
    public partial class PaintSourceDiscoverySystem : SystemBase
    {
        private EntityQuery sources;
        private List<Entity> destination;
        private bool collectAll;
        // Compatibility producers can leave shared types busy on every visit.
        // Do not starve their unannounced changes: at this existing callback
        // bound use the original safe filtered query and its required joins.
        internal const int MaximumDeferredVisits = 8;
        private int deferredVisits;
        private static readonly ComponentType[] Changes =
        {
            ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>()
        };

        protected override void OnCreate()
        {
            base.OnCreate();
            sources = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>(),
                    ComponentType.ReadOnly<Game.Net.CarLane>(), ComponentType.ReadOnly<Game.Net.ParkingLane>(),
                    ComponentType.ReadOnly<Game.Net.TrackLane>() }
            });
        }

        internal bool TryCollect(List<Entity> output, bool full)
        {
            output.Clear();
            if (!full)
            {
                var producers = sources.GetDependency();
                if (!producers.IsCompleted)
                {
                    if (++deferredVisits < MaximumDeferredVisits) return false;
                }
                else producers.Complete();
            }

            deferredVisits = 0;
            destination = output;
            collectAll = full;
            try
            {
                Update();
                return true;
            }
            finally
            {
                destination = null;
                collectAll = false;
            }
        }

        protected override void OnUpdate()
        {
            sources.ResetFilter();
            try
            {
                if (!collectAll) sources.SetChangedVersionFilter(Changes);
                using (var found = sources.ToEntityArray(Allocator.Temp))
                    foreach (var source in found) destination.Add(source);
            }
            finally
            {
                sources.ResetFilter();
            }
        }
    }
}
