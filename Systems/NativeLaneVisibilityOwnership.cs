using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class NativeLaneVisibilityQuery
    {
        private static bool Has(ComponentType[] values, ComponentType type)
        {
            if (values != null) foreach (var value in values) if (value.TypeIndex == type.TypeIndex) return true;
            return false;
        }

        private static ComponentType[] Plus(ComponentType[] values, params ComponentType[] extra)
        {
            var result = new List<ComponentType>(values ?? Array.Empty<ComponentType>());
            foreach (var item in extra) if (!Has(result.ToArray(), item)) result.Add(item);
            return result.ToArray();
        }

        private static void Add(List<EntityQueryDesc> result, EntityQueryDesc source,
            ComponentType[] all, ComponentType[] any, ComponentType[] none)
        {
            foreach (var item in all) if (Has(none, item)) return;
            if (any.Length != 0)
            {
                var possible = new List<ComponentType>(); bool satisfied = false;
                foreach (var item in any)
                {
                    if (Has(all, item)) { satisfied = true; break; }
                    if (!Has(none, item)) possible.Add(item);
                }
                if (satisfied) any = Array.Empty<ComponentType>();
                else if (possible.Count == 0) return;
                else any = possible.ToArray();
            }
            result.Add(new EntityQueryDesc { All = all, Any = any, None = none, Options = source.Options });
        }

        // Preserve native parent propagation, temporary/deleted actors, routed
        // pedestrian lanes and every existing foreign exclusion. Only committed
        // leaf marking sources with an explicit Curbside hide owner are omitted
        // from native "visible parent => reveal child" synchronization.
        internal static bool TryBuild(EntityQueryDesc[] original, ComponentType hidden, ComponentType lane,
            ComponentType children, ComponentType pedestrian, ComponentType replaced, ComponentType manuallyHidden,
            ComponentType temporary, ComponentType deleted, out EntityQueryDesc[] filtered)
        {
            filtered = null;
            if (original == null || original.Length == 0) return false;
            var result = new List<EntityQueryDesc>();
            foreach (var source in original)
            {
                if (source == null || !Has(source.All, hidden) || source.Any == null || source.Any.Length != 2
                    || !Has(source.Any, lane) || !Has(source.Any, children)
                    // Newer query forms must not silently lose a foreign filter.
                    || (source.Disabled != null && source.Disabled.Length != 0)
                    || (source.Absent != null && source.Absent.Length != 0)
                    || (source.Present != null && source.Present.Length != 0)) return false;
                // Any is satisfied by the added Lane/SubLane requirement; do
                // not duplicate a type across All and Any (Unity rejects that).
                Add(result, source, Plus(source.All, children), Array.Empty<ComponentType>(), Plus(source.None));
                Add(result, source, Plus(source.All, lane), new[] { temporary, deleted }, Plus(source.None, children));
                Add(result, source, Plus(source.All, lane), Array.Empty<ComponentType>(),
                    Plus(source.None, children, temporary, deleted, replaced, manuallyHidden));
                Add(result, source, Plus(source.All, lane, pedestrian), Array.Empty<ComponentType>(),
                    Plus(source.None, children, temporary, deleted, manuallyHidden));
            }
            if (result.Count == 0) return false;
            filtered = result.ToArray();
            return true;
        }
    }

    internal static class NativeLaneVisibilityOwnership
    {
        private static World owner;
        private static Game.Net.LaneHiddenSystem system;
        private static FieldInfo field;
        private static EntityQuery original, filtered;
        private static bool installed;
        internal static bool SupportsModule(Guid module, Guid entities)
            => (module == new Guid("e15551e9-3dea-4855-8dde-1da3cea7d7ef")
                && entities == new Guid("3d6b23ae-b281-44a6-a5f4-cbd5fd3d75e6"))
            || (module == new Guid("46198354-d4f9-4d23-bf32-7e3c085d8b92")
                && entities == new Guid("bfb47e8c-9b6e-4acf-8c6d-c80a7bff1c41"));

        internal static void Install(World world)
        {
            if (installed && owner == world) return;
            Restore();
            if (world == null || !world.IsCreated || !SupportsModule(typeof(Game.Net.LaneHiddenSystem).Module.ModuleVersionId, typeof(EntityQuery).Module.ModuleVersionId))
            { Mod.Log.Info("Native marking visibility ownership: unsupported game module; existing Curbside visibility maintenance retained."); return; }
            try
            {
                var target = world.GetOrCreateSystemManaged<Game.Net.LaneHiddenSystem>();
                if (!target.Enabled) { Mod.Log.Info("Native marking visibility ownership: native lane visibility is disabled by another owner; unchanged."); return; }
                var member = typeof(Game.Net.LaneHiddenSystem).GetField("m_HiddenQuery", BindingFlags.Instance | BindingFlags.NonPublic);
                if (member == null || member.FieldType != typeof(EntityQuery)) return;
                var before = (EntityQuery)member.GetValue(target);
                // Descriptions do not contain shared/change/order filters. A
                // foreign filter cannot be reconstructed from descriptions.
                if (before.HasFilter())
                { Mod.Log.Info("Native marking visibility ownership: native query has a foreign chunk filter; unchanged."); return; }
                if (!NativeLaneVisibilityQuery.TryBuild(before.GetEntityQueryDescs(), ComponentType.ReadOnly<Hidden>(),
                    ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<SubLane>(), ComponentType.ReadOnly<PedestrianLane>(),
                    ComponentType.ReadOnly<MGRReplacedPaint>(), ComponentType.ReadOnly<MGRHiddenJunctionLine>(),
                    ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), out var descriptions))
                { Mod.Log.Info("Native marking visibility ownership: query shape belongs to another implementation; unchanged."); return; }
                // Load/dispose boundary only. No query rewrite or scan runs in
                // an update loop, and no native job or visibility method changes.
                NativeVisualInvalidation.CompleteReaders(world);
                var next = world.EntityManager.CreateEntityQuery(descriptions);
                if (!((EntityQuery)member.GetValue(target)).Equals(before)) return;
                member.SetValue(target, next);
                owner = world; system = target; field = member; original = before; filtered = next; installed = true;
                Mod.Log.Info("Native marking visibility ownership installed: Curbside-hidden committed leaf sources excluded from native reveal; parents, pedestrians, temporary actors and foreign exclusions retained.");
            }
            catch (Exception error)
            { Mod.Log.Warn("Native marking visibility ownership not installed; existing maintenance retained: " + error.GetType().Name); }
        }

        internal static void Restore()
        {
            try
            {
                if (installed && owner != null && owner.IsCreated && field != null && system != null
                    && ((EntityQuery)field.GetValue(system)).Equals(filtered))
                {
                    NativeVisualInvalidation.CompleteReaders(owner);
                    field.SetValue(system, original);
                }
                // Queries remain owned by the World until its disposal. A later
                // mod may have retained a copied query handle; never invalidate
                // that handle while returning our own field to its old value.
            }
            finally { installed = false; owner = null; system = null; field = null; original = filtered = default; }
        }
    }
}
