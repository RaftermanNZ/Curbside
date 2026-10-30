using System;

using System.Collections;

using System.Collections.Generic;

using Colossal.Mathematics;

using Game.Net;

using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    internal static class RoadMarkingPolicy

    {

        private static object Read(object instance, string name)

        {

            if (instance == null) return null;

            var type = instance.GetType();

            var field = type.GetField(name);

            return field != null ? field.GetValue(instance) : type.GetProperty(name)?.GetValue(instance, null);

        }



        // Read Road Builder's own option metadata. Parking and driving groups

        // use opposite values, so a magic "1 means off" rule is incorrect.

        internal static bool NoMarkings(object component)

        {

            if (!(Read(component, "Combination") is IEnumerable combination)) return false;

            foreach (var selected in combination)

            {

                if (!string.Equals(Read(selected, "OptionName") as string, "Markings", StringComparison.Ordinal)) continue;

                string value = Read(selected, "Value") as string ?? string.Empty;

                if (!(Read(Read(component, "GroupPrefab"), "Options") is IEnumerable options)) return false;

                foreach (var option in options)

                {

                    if (!string.Equals(Read(option, "Name") as string, "Markings", StringComparison.Ordinal)) continue;

                    if (!(Read(option, "Options") is IEnumerable choices)) continue;

                    foreach (var choice in choices)

                        if (string.Equals(Read(choice, "Value") as string ?? string.Empty, value, StringComparison.Ordinal))

                            return (Read(choice, "ThumbnailUrl") as string ?? string.Empty).IndexOf("NoMarkings", StringComparison.OrdinalIgnoreCase) >= 0;

                }

            }

            return false;

        }



        internal static bool Suppressed(float lateral, List<float2> spans)

        {

            foreach (var span in spans)

                if (lateral >= span.x - 0.01f && lateral <= span.y + 0.01f) return true;

            return false;

        }



        internal static float Lateral(EdgeGeometry geometry, float3 point, float width)

        {

            float best = float.MaxValue, result = float.NaN;

            foreach (var segment in new[] { geometry.m_Start, geometry.m_End })

            {

                var center = MathUtils.Lerp(segment.m_Left, segment.m_Right, 0.5f);

                float distance = MathUtils.Distance(center, point, out float t);

                if (distance >= best) continue;

                var left = MathUtils.Position(segment.m_Left, t);

                var right = MathUtils.Position(segment.m_Right, t);

                float2 across = right.xz - left.xz;

                float lengthSquared = math.lengthsq(across);

                if (lengthSquared <= 0.01f) continue;

                result = (math.dot(point.xz - left.xz, across) / lengthSquared - 0.5f) * width;

                best = distance;

            }

            return result;

        }

    }

}

