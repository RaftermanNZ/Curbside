using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Pure copied values only: never retain an ECS buffer, lookup or native view.
    // This caches side geometry, not visibility intent or owner association.
    internal struct DecalSideInput
    {
        internal Entity Actor, Road;
        internal float3 Position;
        internal Bezier4x3 Curve;
        internal bool HasGeometry;
        internal EdgeGeometry Geometry;

        internal bool Same(DecalSideInput other)
        {
            return Actor == other.Actor && Road == other.Road
                && math.all(Position == other.Position)
                && SameCurve(Curve, other.Curve)
                && HasGeometry == other.HasGeometry
                && (!HasGeometry || (
                    SameCurve(Geometry.m_Start.m_Left, other.Geometry.m_Start.m_Left)
                    && SameCurve(Geometry.m_Start.m_Right, other.Geometry.m_Start.m_Right)
                    && SameCurve(Geometry.m_End.m_Left, other.Geometry.m_End.m_Left)
                    && SameCurve(Geometry.m_End.m_Right, other.Geometry.m_End.m_Right)));
        }

        private static bool SameCurve(Bezier4x3 a, Bezier4x3 b)
        {
            // Non-finite NaN inputs deliberately miss rather than hide a change.
            return math.all(a.a == b.a) && math.all(a.b == b.b)
                && math.all(a.c == b.c) && math.all(a.d == b.d);
        }
    }

    internal sealed class DecalSideCache
    {
        // Direct mapping bounds memory and makes eviction constant-time.
        // A collision is a cache miss, never a different actor's answer.
        private const int Capacity = 1024;
        private struct Entry
        {
            internal bool Valid, Left;
            internal DecalSideInput Input;
        }
        private readonly Entry[] entries = new Entry[Capacity];

        private static int Slot(Entity actor) => actor.GetHashCode() & (Capacity - 1);

        internal bool TryGet(DecalSideInput input, out bool left)
        {
            var entry = entries[Slot(input.Actor)];
            left = entry.Left;
            return entry.Valid && entry.Input.Same(input);
        }

        internal void Store(DecalSideInput input, bool left)
        {
            entries[Slot(input.Actor)] = new Entry { Valid = true, Input = input, Left = left };
        }

        internal void Forget(Entity actor)
        {
            int slot = Slot(actor);
            if (entries[slot].Valid && entries[slot].Input.Actor == actor)
                entries[slot] = default;
        }

        internal void Clear() => System.Array.Clear(entries, 0, entries.Length);
    }

    public partial class DecalVisibilitySystem
    {
        private readonly DecalSideCache sideCache = new DecalSideCache();

        private byte HiddenDecalMask(Entity road, Entity actor)
        {
            // Intent is read on every existing classification request. Only the
            // expensive geometric left/right answer may be reused.
            var sides = RoadSideUpgrades.Decals(EntityManager, road);
            if (!EntityManager.HasComponent<RDCSideUpgrade>(road)) return sides.Left;
            if (!EntityManager.HasComponent<Game.Objects.Transform>(actor)
                || !EntityManager.HasComponent<Curve>(road)) return 0;
            // Side selection cannot affect the answer for a two-sided choice.
            // Keep the readiness checks above: missing pose/curve still means 0.
            if (sides.Left == sides.Right) return sides.Left;

            var input = new DecalSideInput
            {
                Actor = actor,
                Road = road,
                Position = EntityManager.GetComponentData<Game.Objects.Transform>(actor).m_Position,
                Curve = EntityManager.GetComponentData<Curve>(road).m_Bezier,
                HasGeometry = EntityManager.HasComponent<EdgeGeometry>(road)
            };
            if (input.HasGeometry) input.Geometry = EntityManager.GetComponentData<EdgeGeometry>(road);
            if (!sideCache.TryGet(input, out bool left))
            {
                // Keep the established algorithm, including its curve fallback.
                left = RoadSideUpgrades.IsLeft(EntityManager, road, input.Position);
                sideCache.Store(input, left);
            }
            return left ? sides.Left : sides.Right;
        }
    }
}
