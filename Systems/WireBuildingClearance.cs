using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;
using ObjectLight = Game.Objects.TrafficLight;

namespace MedianGrassReplacement.Systems
{
    internal static partial class GardenCityLightPolicy
    {
        // Predict the same simple RenderPrefab union consumed by native object
        // initialization. This is read-only planning data, never a replacement
        // for the registered prefab's full rendering/collision bounds.
        internal static bool DescribeGeometry(ObjectGeometryData source, IList<ObjectMeshInfo> meshes,
            bool standing, out ObjectGeometryData geometry)
        {
            geometry = source;
            if (!standing) geometry.m_Flags &= ~(Game.Objects.GeometryFlags.Standing | Game.Objects.GeometryFlags.CircularLeg);
            if (meshes == null || meshes.Count == 0) return false;
            var bounds = new Bounds3(new float3(float.MaxValue), new float3(float.MinValue));
            foreach (var part in meshes)
            {
                if (!(part?.m_Mesh is RenderPrefab render)) return false;
                var b = Game.Objects.ObjectUtils.CalculateBounds(part.m_Position, part.m_Rotation, render.bounds);
                if ((geometry.m_Flags & Game.Objects.GeometryFlags.Standing) != 0) b.max.y = math.max(b.max.y, geometry.m_LegSize.y + .1f);
                if (!math.all(math.isfinite(b.min)) || !math.all(math.isfinite(b.max)) || !math.all(b.min <= b.max)) return false;
                bounds.min = math.min(bounds.min, b.min); bounds.max = math.max(bounds.max, b.max);
            }
            geometry.m_Bounds = bounds;
            geometry.m_Size = Game.Objects.ObjectUtils.GetSize(bounds);
            if ((geometry.m_Flags & Game.Objects.GeometryFlags.Circular) != 0)
                geometry.m_Size.xz = math.cmax(geometry.m_Size.xz);
            return true;
        }

        internal static List<ObjectMeshInfo> ClearancePedestrian(byte style)
            => new List<ObjectMeshInfo> { Mesh(IsWireStyle(style) ? "PedHeadWire" : "PedHead",
                new float3(0, 2.55f, PedestrianOffset), quaternion.identity) };

        internal static List<ObjectMeshInfo> ClearanceLuminaire(byte style)
            => new List<ObjectMeshInfo> { Mesh(IsMetalWireStyle(style) ? "MetalWireLuminaire" : "WireLuminaire",
                float3.zero, quaternion.identity) };
    }

}
