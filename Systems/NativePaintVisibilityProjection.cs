using System.Collections.Generic;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    public partial class NativePaintProjectionSystem
    {
        private struct EmptyAppearance { internal Entity Source; internal bool AllHidden; }
        private readonly Dictionary<NativePaintRole, EmptyAppearance> emptyAppearance = new Dictionary<NativePaintRole, EmptyAppearance>();

        private bool SavedEmpty(Entity source)
            => EntityManager.HasComponent<MGRHiddenJunctionLine>(source)
                || EntityManager.HasComponent<RDCHiddenDecal>(source)
                || EntityManager.HasComponent<MGRReplacedPaint>(source)
                    && World.GetExistingSystemManaged<IntersectionPaintSystem>()?.HasEmptyPreviewPaint(source) == true;

        private void IndexEmptyAppearance(Entity owner, Entity source)
        {
            if (EntityManager.HasComponent<PedestrianLane>(source)) return;
            var key = new NativePaintRole(owner, Canonical(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab), default);
            bool hidden = !externalPaint.Contains(EntityManager, source) && SavedEmpty(source);
            if (emptyAppearance.TryGetValue(key, out var group))
            { group.AllHidden &= hidden; emptyAppearance[key] = group; }
            else emptyAppearance[key] = new EmptyAppearance { Source = source, AllHidden = hidden };
        }

        // Appearance consensus needs no invented lane identity. Only accept a
        // unanimous empty result for this exact committed owner and definition;
        // a single visible lane prevents borrowing another lane's hidden state.
        private Entity EmptyAppearanceSource(Entity actor, Entity original)
        {
            if (EntityManager.HasComponent<PedestrianLane>(actor)) return Entity.Null;
            var key = new NativePaintRole(original, Canonical(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab), default);
            return emptyAppearance.TryGetValue(key, out var group) && group.AllHidden ? group.Source : Entity.Null;
        }
    }
}
