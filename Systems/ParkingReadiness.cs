using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FixedParkingSystem
    {
        // Runtime IDs only. No saved component, tag on a foreign prefab, timer,
        // native view, cursor check or per-waiter recovery scan is introduced.
        private readonly HashSet<Entity> waitingParkingInputs = new HashSet<Entity>();
        private readonly ManagedWorkIndex<Entity, Entity> waitingParkingDefinitions = new ManagedWorkIndex<Entity, Entity>();
        private readonly List<Entity> readyParkingLanes = new List<Entity>();
        private bool parkingCatalogChecked;

        private void DeferParkingInputs(Entity lane)
        {
            _waiting.Add(lane);
            waitingParkingDefinitions.Remove(lane);
            waitingParkingInputs.Add(lane);
        }

        private void DeferParkingDefinition(Entity lane, Entity definition)
        {
            _waiting.Add(lane);
            waitingParkingInputs.Remove(lane);
            waitingParkingDefinitions.Add(definition, lane);
        }

        private void ForgetParkingReadiness(Entity lane)
        {
            _waiting.Remove(lane);
            waitingParkingInputs.Remove(lane);
            waitingParkingDefinitions.Remove(lane);
        }

        private void ClearParkingReadiness()
        {
            _waiting.Clear();
            waitingParkingInputs.Clear();
            waitingParkingDefinitions.Clear();
            readyParkingLanes.Clear();
        }

        private void CollectReadyParking(HashSet<Entity> pending)
        {
            if (_waiting.Count == 0) return;
            // Order filters report structural component availability, not Curve,
            // Owner or PrefabRef payload writes. A missing Curve/parking data
            // becoming present is admitted even without a native Updated tag.
            // Only indexed deferred IDs are forwarded. Empty batches do not
            // inspect any waiting object or synchronize those data writers.
            if (waitingParkingInputs.Count != 0)
            {
                try
                {
                    _lanes.SetOrderVersionFilter();
                    using (var lanes = _lanes.ToEntityArray(Allocator.Temp))
                        foreach (var lane in lanes)
                            if (waitingParkingInputs.Contains(lane)) pending.Add(lane);
                }
                finally { _lanes.ResetFilter(); }
            }
            if (waitingParkingDefinitions.Count == 0) return;
            using (var definitions = _readyParkingDefinitions.ToEntityArray(Allocator.Temp))
                foreach (var definition in definitions)
                {
                    readyParkingLanes.Clear();
                    waitingParkingDefinitions.CopyOwner(definition, readyParkingLanes);
                    foreach (var lane in readyParkingLanes) pending.Add(lane);
                }
            readyParkingLanes.Clear();
        }
    }
}
