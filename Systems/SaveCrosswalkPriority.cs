using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class IntersectionPaintSystem
    {
        // Original source IDs only. No visual actor, definition, pose, native
        // buffer or renderer record survives through this queue.
        private readonly ManagedWorkQueue<Entity> savedCrossings = new ManagedWorkQueue<Entity>();

        private void CaptureCrosswalkRestorePriority()
        {
            int before=savedCrossings.Count;
            foreach(var pair in _replacements)
                if(pair.Value.RoutedCrossing && Alive(pair.Key)
                    && EntityManager.HasComponent<PedestrianLane>(pair.Key)
                    && !EntityManager.HasComponent<Game.Tools.Temp>(pair.Key))
                    savedCrossings.Add(pair.Key);
            if(savedCrossings.Count>before)
                Mod.Log.Info("Save crosswalk priority: queued "+savedCrossings.Count
                    +" original sources for existing reconciliation; frame budget unchanged.");
        }

        private void ProcessSavedCrosswalkPriority(ref int processed)
        {
            // Reconcile reads current owner, geometry, style and visibility.
            // A stale or changed source is never repaired from saved snapshots.
            while(processed<8&&savedCrossings.Take(out var source))
            {
                if(!_processedClicks.Contains(source))
                {Reconcile(source);_processedClicks.Add(source);}
                processed++;
            }
        }
    }
}
