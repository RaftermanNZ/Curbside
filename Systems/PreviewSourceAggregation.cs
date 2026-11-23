using Game.Net;

namespace MedianGrassReplacement.Systems
{
    public partial class PreviewPaintInheritanceSystem
    {
        private void UpdateIdenticalSources()
        {
            if (sourceNotifications.TryUpdate(EntityManager, groupCurveReaders, PaintInputReadScope.Supported,
                UnityEngine.Time.frameCount, identicalSources)) return;
            identicalSources.Clear();
            // Do not impose the copied-curve cache on unique/lightly shared
            // mappings. The reverse notification index already has this count.
            int unique = sourceNotifications.KeyCount;
            if (unique != 0 && unique <= sources.Count / 4)
            {
                sharedSourcePaths.Clear();
                try
                {
                    foreach (var pair in sources)
                        sharedSourcePaths.Include(EntityManager, pair.Value, pair.Key);
                    sharedSourcePaths.CopyTo(identicalSources);
                }
                finally { sharedSourcePaths.Clear(); }
                return;
            }
            foreach (var pair in sources)
            {
                var source = pair.Value;
                bool same = Alive(source) && EntityManager.HasComponent<Curve>(source)
                    && PreviewPaintGeometry.SamePath(EntityManager.GetComponentData<Curve>(source).m_Bezier,
                        EntityManager.GetComponentData<Curve>(pair.Key).m_Bezier);
                identicalSources[source] = (!identicalSources.TryGetValue(source, out var priorSame) || priorSame) && same;
            }
        }
    }
}
