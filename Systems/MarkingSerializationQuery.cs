using System;
using System.Reflection;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class MarkingSerializationQuery
    {
        private static readonly FieldInfo QueryField = typeof(Game.Serialization.SerializerSystem)
            .GetField("m_Query", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static bool ExcludesDisabled(EntityQueryDesc[] descriptions)
        {
            if (descriptions == null || descriptions.Length == 0) return false;
            foreach (var query in descriptions)
            {
                if (query == null || (query.Options & EntityQueryOptions.IncludeDisabledEntities) != 0) return false;
                // Explicit inclusion can cause Unity to infer IncludeDisabled.
                if (NamesDisabled(query.All) || NamesDisabled(query.Any) || NamesDisabled(query.Present)
                    || NamesDisabled(query.Disabled)) return false;
            }
            return true;
        }

        private static bool NamesDisabled(ComponentType[] values)
        {
            if (values != null)
                foreach (var value in values)
                    if (value.TypeIndex == ComponentType.ReadOnly<Disabled>().TypeIndex) return true;
            return false;
        }

        // Save-start event only, before any exclusion or native GPU-frame wait.
        internal static bool Supports(World world)
        {
            try
            {
                var serializer = world?.GetExistingSystemManaged<Game.Serialization.SerializerSystem>();
                return serializer != null && serializer.Enabled && QueryField != null && QueryField.FieldType == typeof(EntityQuery)
                    && ExcludesDisabled(((EntityQuery)QueryField.GetValue(serializer)).GetEntityQueryDescs());
            }
            catch (Exception) { return false; }
        }
    }
}
