using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    internal struct TemporaryPreviewWrite
    {
        internal Entity Prefab;
        internal Transform Pose;
        internal bool HasPose;
        internal bool HasOriginal;
        internal Entity Original;
    }

    internal static class PreviewVisualPolicy
    {
        internal static bool Active(TempFlags flags)
            => (flags & (TempFlags.Delete | TempFlags.Cancel | TempFlags.Hidden)) == 0;

        internal static byte Delta(Entity current, Transform pose, Entity target, Transform desired, bool hasPose)
            => (byte)((current != target ? 1 : 0) | (hasPose && !pose.Equals(desired) ? 2 : 0));

        internal static bool Place(Transform native, Transform applied, Transform preview, out Transform result)
        {
            // Preview input never moves an already placed signal. Native actors
            // can be rebuilt, but their target remains the committed visual pose.
            result = applied;
            return math.all(math.isfinite(result.m_Position)) && math.all(math.isfinite(result.m_Rotation.value));
        }
    }

    // Temporary actors only. One native-reader join and one playback for a set
    // of actual changes; identical targets do not invalidate lights or batching.
    internal static class TemporaryPreviewWrites
    {
        internal static bool Ready(EntityManager manager, Entity actor, Entity prefab)
        {
            if (!manager.Exists(actor) || manager.HasComponent<Deleted>(actor)
                || !manager.HasComponent<Temp>(actor) || !manager.HasComponent<PrefabRef>(actor)
                || !manager.Exists(prefab)) return false;
            if (!manager.HasComponent<Transform>(actor)) return manager.HasComponent<NetLaneGeometryData>(prefab);
            if (!manager.HasComponent<ObjectData>(prefab) || !manager.HasComponent<ObjectGeometryData>(prefab)
                || !manager.HasBuffer<SubMesh>(prefab)) return false;
            if (manager.HasBuffer<Emissive>(actor) != manager.HasBuffer<LightState>(actor)) return false;
            foreach (var mesh in manager.GetBuffer<SubMesh>(prefab, true))
                if (!manager.Exists(mesh.m_SubMesh)
                    || manager.HasBuffer<ProceduralLight>(mesh.m_SubMesh) && !manager.HasBuffer<Emissive>(actor)) return false;
            return !manager.HasBuffer<Game.Prefabs.Effect>(prefab)
                || manager.GetBuffer<Game.Prefabs.Effect>(prefab, true).Length == 0
                || manager.HasBuffer<Game.Effects.EnabledEffect>(actor);
        }

        internal static int Apply(World world, Dictionary<Entity, TemporaryPreviewWrite> requests)
        {
            if (requests.Count == 0) return 0;
            var manager = world.EntityManager;
            int changed = 0;
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            {
                foreach (var pair in requests)
                {
                    var actor = pair.Key;
                    var write = pair.Value;
                    if (!Ready(manager, actor, write.Prefab)) continue;
                    var pose = manager.HasComponent<Transform>(actor) ? manager.GetComponentData<Transform>(actor) : default;
                    byte delta = PreviewVisualPolicy.Delta(manager.GetComponentData<PrefabRef>(actor).m_Prefab,
                        pose, write.Prefab, write.Pose, write.HasPose);
                    var temp = write.HasOriginal ? manager.GetComponentData<Temp>(actor) : default;
                    bool identityChanged = write.HasOriginal && temp.m_Original != write.Original;
                    if (delta == 0 && !identityChanged) continue;
                    if ((delta & 1) != 0)
                    {
                        commands.SetComponent(actor, new PrefabRef(write.Prefab));
                        if (manager.HasBuffer<Emissive>(actor) && !manager.HasComponent<MGRLightReset>(actor))
                            commands.AddComponent<MGRLightReset>(actor);
                    }
                    if ((delta & 2) != 0) commands.SetComponent(actor, write.Pose);
                    if (identityChanged) commands.SetComponent(actor, SignalPreviewIdentityPolicy.WithOriginal(temp, write.Original));
                    // An original-link update is consumed directly by native
                    // visibility/rendering. It does not change a mesh, effect,
                    // light allocation or pose and requests no extra refresh.
                    if (delta != 0)
                    {
                        if (!manager.HasComponent<Updated>(actor)) commands.AddComponent<Updated>(actor);
                        if (!manager.HasComponent<BatchesUpdated>(actor)) commands.AddComponent<BatchesUpdated>(actor);
                        if (!manager.HasComponent<EffectsUpdated>(actor)) commands.AddComponent<EffectsUpdated>(actor);
                    }
                    changed++;
                }
                if (changed != 0)
                {
                    NativeVisualInvalidation.CompleteReaders(world);
                    commands.Playback(manager);
                }
            }
            requests.Clear();
            return changed;
        }
    }
}
