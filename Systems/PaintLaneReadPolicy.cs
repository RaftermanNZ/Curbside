using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal sealed class PaintLaneReadPolicy
    {
        private readonly PrefabSystem prefabs;
        internal PaintLaneReadPolicy(PrefabSystem prefabs) { this.prefabs = prefabs; }

        internal bool IsNonPaint(Entity entity)
            => prefabs.TryGetPrefab<NetLanePrefab>(entity, out var prefab)
                && prefab != null && !(prefab is NetLaneGeometryPrefab);
    }
}
