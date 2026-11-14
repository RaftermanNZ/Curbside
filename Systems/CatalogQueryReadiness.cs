using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class CatalogQueryReadiness
    {
        // Metadata polling never joins an unfinished producer. Actual catalog
        // consumers still use the ordinary enabled, dependency-safe queries.
        internal static bool Ready(EntityQuery first)
        {
            if (!MarkingChangeNotifications.Supported) return true;
            var jobs = first.GetDependency();
            if (!jobs.IsCompleted) return false;
            jobs.Complete();
            return true;
        }

        internal static bool Ready(EntityQuery first, EntityQuery second)
        {
            if (!MarkingChangeNotifications.Supported) return true;
            var a = first.GetDependency();
            var b = second.GetDependency();
            if (!a.IsCompleted || !b.IsCompleted) return false;
            a.Complete(); b.Complete();
            return true;
        }
    }
}
