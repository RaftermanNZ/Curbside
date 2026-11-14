using System;
using System.Collections.Generic;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace MedianGrassReplacement.Systems
{
    // Replaces the existing three filtered collections. Local to one callback:
    // no polling, deferred frames, persistent native views or new update system.
    internal struct PaintNotificationBatch : IDisposable
    {
        private static readonly ComponentType[] GeometryChanges =
            { ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Composition>() };
        private static readonly bool Supported =
            typeof(EdgeGeometry).Module.ModuleVersionId == new Guid("46198354-d4f9-4d23-bf32-7e3c085d8b92")
            && typeof(EntityQuery).Module.ModuleVersionId == new Guid("bfb47e8c-9b6e-4acf-8c6d-c80a7bff1c41");

        private NativeList<ArchetypeChunk> geometry, lanes, nodes;
        private NativeArray<Entity> legacyGeometry, legacyLanes, legacyNodes;
        internal JobHandle Jobs { get; private set; }

        internal void Schedule(EntityQuery roads, EntityQuery junctions, JobHandle input)
        {
            try
            {
                roads.SetChangedVersionFilter(GeometryChanges);
                if (Supported)
                {
                    geometry = roads.ToArchetypeChunkListAsync(Allocator.TempJob, input, out var geometryJob);
                    Jobs = JobHandle.CombineDependencies(Jobs, geometryJob);
                    roads.SetChangedVersionFilter(ComponentType.ReadOnly<SubLane>());
                    lanes = roads.ToArchetypeChunkListAsync(Allocator.TempJob, input, out var laneJob);
                    Jobs = JobHandle.CombineDependencies(Jobs, laneJob);
                    nodes = junctions.ToArchetypeChunkListAsync(Allocator.TempJob, input, out var nodeJob);
                    Jobs = JobHandle.CombineDependencies(Jobs, nodeJob);
                }
                else
                {
                    // Unknown binaries retain the original synchronous path.
                    legacyGeometry = roads.ToEntityArray(Allocator.Temp);
                    roads.SetChangedVersionFilter(ComponentType.ReadOnly<SubLane>());
                    legacyLanes = roads.ToEntityArray(Allocator.Temp);
                    legacyNodes = junctions.ToEntityArray(Allocator.Temp);
                }
            }
            finally { roads.ResetFilter(); }
        }

        internal void CopyTo(HashSet<Entity> roadWork, List<Entity> nodeWork, EntityTypeHandle entities)
        {
            // Native filters run after their writers, not on racing main-thread
            // chunk metadata. Do not inspect Length/chunks before this fence.
            Jobs.Complete();
            if (Supported)
            {
                CopyRoads(geometry, roadWork, entities);
                CopyRoads(lanes, roadWork, entities);
                foreach (var chunk in nodes)
                    foreach (var entity in chunk.GetNativeArray(entities)) nodeWork.Add(entity);
            }
            else
            {
                foreach (var entity in legacyGeometry) roadWork.Add(entity);
                foreach (var entity in legacyLanes) roadWork.Add(entity);
                foreach (var entity in legacyNodes) nodeWork.Add(entity);
            }
        }

        private static void CopyRoads(NativeList<ArchetypeChunk> chunks, HashSet<Entity> work, EntityTypeHandle entities)
        {
            foreach (var chunk in chunks)
                foreach (var entity in chunk.GetNativeArray(entities)) work.Add(entity);
        }

        public void Dispose()
        {
            // Also covers partial scheduling and exceptions in the caller.
            Jobs.Complete();
            if (geometry.IsCreated) geometry.Dispose();
            if (lanes.IsCreated) lanes.Dispose();
            if (nodes.IsCreated) nodes.Dispose();
            if (legacyGeometry.IsCreated) legacyGeometry.Dispose();
            if (legacyLanes.IsCreated) legacyLanes.Dispose();
            if (legacyNodes.IsCreated) legacyNodes.Dispose();
            this = default;
        }
    }
}
