using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // A native output boundary restores our render claims before native matching.
    // Only those already tracked actors need a one-shot survival check afterward.
    // Never enumerate the city or acquire a replacement entity by proximity.
    internal sealed class NativePreviewRefreshQueue
    {
        private struct Binding { internal Entity Owner, Original; }
        private readonly Dictionary<Entity, Binding> entries = new Dictionary<Entity, Binding>();
        internal int Count => entries.Count;
        internal void Clear() => entries.Clear();
        private static bool Read(EntityManager manager, Entity actor, out Binding value)
        {
            value = default;
            if (!manager.Exists(actor) || manager.HasComponent<Deleted>(actor) || !manager.HasComponent<Temp>(actor)) return false;
            value.Original = manager.GetComponentData<Temp>(actor).m_Original;
            value.Owner = manager.HasComponent<Owner>(actor) ? manager.GetComponentData<Owner>(actor).m_Owner : Entity.Null;
            return true;
        }
        internal void Capture(EntityManager manager, List<Entity> actors)
        {
            foreach (var actor in actors) if (Read(manager, actor, out var value)) entries[actor] = value;
        }
        private static bool Current(EntityManager manager, Entity actor, Binding expected)
            => Read(manager, actor, out var current) && current.Owner == expected.Owner && current.Original == expected.Original;
        internal void AppendCurrent(EntityManager manager, List<Entity> actors)
        {
            foreach (var pair in entries) if (Current(manager, pair.Key, pair.Value)) actors.Add(pair.Key);
        }
        internal void Drain(EntityManager manager, HashSet<Entity> actors)
        {
            foreach (var pair in entries) if (Current(manager, pair.Key, pair.Value)) actors.Add(pair.Key);
            entries.Clear();
        }
    }

    public partial class NativeSignalProjectionSystem
    {
        private readonly NativePreviewRefreshQueue refresh = new NativePreviewRefreshQueue();
        private readonly List<Entity> refreshActors = new List<Entity>();
        private uint refreshPass;
        private bool awaitRefresh;
        internal void BeforeNativeOutput()
        {
            if (!Enabled || !Allowed() || Mod.Saving) { ReleaseAndReset(); return; }
            ownerLeases.CopyActors(refreshActors);
            refresh.AppendCurrent(EntityManager, refreshActors);
            ReleaseAndReset();
            // Capture the restored native identity, not our temporary visual link.
            refresh.Capture(EntityManager, refreshActors);
            refreshPass = nativeBoundary.LastSystemVersion; awaitRefresh = refresh.Count != 0;
            refreshActors.Clear();
        }
    }

    public partial class NativePaintProjectionSystem
    {
        private readonly NativePreviewRefreshQueue refresh = new NativePreviewRefreshQueue(), refreshOwners = new NativePreviewRefreshQueue();
        private readonly List<Entity> refreshActors = new List<Entity>(), refreshOwnerActors = new List<Entity>();
        private uint refreshPass;
        private bool awaitRefresh;
        internal void BeforeNativeOutput()
        {
            if (!Enabled || !Allowed() || Mod.Saving) { ReleaseAndReset(); return; }
            leases.CopyActors(refreshActors);
            paintOwnerWatch.CopyActors(dependents);
            refreshActors.AddRange(dependents);
            refresh.AppendCurrent(EntityManager, refreshActors);
            ownerLeases.CopyActors(refreshOwnerActors);
            refreshOwners.AppendCurrent(EntityManager, refreshOwnerActors);
            ReleaseAndReset();
            refresh.Capture(EntityManager, refreshActors);
            refreshOwners.Capture(EntityManager, refreshOwnerActors);
            refreshPass = nativeBoundary.LastSystemVersion; awaitRefresh = refresh.Count != 0 || refreshOwners.Count != 0;
            refreshActors.Clear(); refreshOwnerActors.Clear();
        }
    }
}
