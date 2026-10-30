using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game.Net;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    // Separate components preserve the byte layout of older saved upgrades.

    public struct MGRParkingSides : IComponentData, ISerializable

    {

        public byte Left, Right; // 0 native, 1 setback only, 2 boundary/T, 3 T, 4 dividers

        public void Serialize<T>(T writer) where T : IWriter { writer.Write(Left); writer.Write(Right); }

        public void Deserialize<T>(T reader) where T : IReader { reader.Read(out Left); reader.Read(out Right); }

    }

    public struct MGRCurbDecalSides : IComponentData, ISerializable

    {

        public byte Mask;

        public void Serialize<T>(T writer) where T : IWriter { writer.Write(Mask); }

        public void Deserialize<T>(T reader) where T : IReader { reader.Read(out Mask); }

    }

    public struct MGRCustomLineSides : IComponentData, ISerializable

    {

        public byte Mask;

        public void Serialize<T>(T writer) where T:IWriter {writer.Write(Mask);}

        public void Deserialize<T>(T reader) where T:IReader {reader.Read(out Mask);}

    }

    public struct RDCSideUpgrade : IComponentData, ISerializable

    {

        public byte Left, Right;

        public void Serialize<T>(T writer) where T : IWriter { writer.Write(Left); writer.Write(Right); }

        public void Deserialize<T>(T reader) where T : IReader { reader.Read(out Left); reader.Read(out Right); }

    }

    internal static class RoadSideUpgrades

    {

        // MGRCustomLineSides is retained only for old save compatibility.

        internal const string DecalAsset = "MGR Curbside Decals";

        internal static byte SideMask(bool left) => left ? (byte)1 : (byte)2;

        internal static byte SetMask(byte old, bool left, bool remove) => (byte)((remove ? old & ~SideMask(left) : old | SideMask(left)) & 3);

        internal static byte DecalTarget(bool left, bool both) => both ? (byte)3 : SideMask(left);

        internal static byte SelectDecals(byte old, bool left, bool remove, bool both)
        {
            byte target=DecalTarget(left,both);
            return (byte)((remove ? old & ~target : old | target) & 3);
        }

        internal static byte PreviewSides(string asset,bool left,bool bothDecals)
        {
            if(asset==DecalAsset)return DecalTarget(left,bothDecals);
            // Section-wide operations must not advertise a single roadside.
            if(asset==StreetLightingSystem.AssetName||asset==RoadFeatureInspector.AssetName||SegmentUpgradePolicy.Known(asset)||UpgradeAssetsSystem.Mask(asset)!=0||SurfaceUpgradePolicy.Slot(asset)!=0)return 3;
            return SideMask(left);
        }

        internal static bool IsLeft(EntityManager em, Entity road, float3 position)

        {

            if (em.HasComponent<EdgeGeometry>(road))

            {

                float lateral = RoadMarkingPolicy.Lateral(em.GetComponentData<EdgeGeometry>(road), position, 1);

                if (math.isfinite(lateral)) return lateral < 0;

            }

            var curve = em.GetComponentData<Curve>(road).m_Bezier;

            MathUtils.Distance(curve, position, out float t);

            return math.dot(MathUtils.Left(math.normalizesafe(MathUtils.Tangent(curve, t).xz)), position.xz - MathUtils.Position(curve, t).xz) > 0;

        }

        internal static MGRParkingSides Parking(EntityManager em, Entity road)

        {

            if (em.HasComponent<MGRParkingSides>(road)) return em.GetComponentData<MGRParkingSides>(road);

            if (!em.HasComponent<MGRParkingUpgrade>(road)) return default;

            byte mode = 1;

            if (em.GetComponentData<MGRParkingUpgrade>(road).Marked)

            {

                byte style = em.HasComponent<MGRParkingStyle>(road) ? em.GetComponentData<MGRParkingStyle>(road).Value : (byte)0;

                mode = (byte)(2 + math.min((int)style, 2));

            }

            return new MGRParkingSides { Left = mode, Right = mode };

        }

        internal static byte ParkingMode(EntityManager em, Entity road, float3 position)

        {

            var sides = Parking(em, road);

            return IsLeft(em, road, position) ? sides.Left : sides.Right;

        }

        internal static RDCSideUpgrade Decals(EntityManager em, Entity road)

        {

            if (em.HasComponent<RDCSideUpgrade>(road)) return em.GetComponentData<RDCSideUpgrade>(road);

            byte mask = em.HasComponent<RDCUpgrade>(road) ? em.GetComponentData<RDCUpgrade>(road).Mask : (byte)0;

            return new RDCSideUpgrade { Left = mask, Right = mask };

        }

        internal static RDCSideUpgrade SetDecals(RDCSideUpgrade sides, bool left, byte selection, bool remove)

        {

            if (left) sides.Left = DecalPolicy.Apply(sides.Left, selection, remove);

            else sides.Right = DecalPolicy.Apply(sides.Right, selection, remove);

            return sides;

        }

        internal static byte HiddenDecals(EntityManager em, Entity road, Entity obj)

        {

            var sides = Decals(em, road);

            if (!em.HasComponent<RDCSideUpgrade>(road)) return sides.Left;

            if (!em.HasComponent<Game.Objects.Transform>(obj) || !em.HasComponent<Curve>(road)) return 0;

            return IsLeft(em, road, em.GetComponentData<Game.Objects.Transform>(obj).m_Position) ? sides.Left : sides.Right;

        }

        internal static MGRParkingSides SetParking(MGRParkingSides sides, bool left, byte mode)

        {

            if (left) sides.Left = mode; else sides.Right = mode;

            return sides;

        }

        internal static Bezier4x3 Preview(EntityManager em, Entity road, bool left)

        {

            var center = em.GetComponentData<Curve>(road).m_Bezier;

            if (!em.HasComponent<EdgeGeometry>(road)) return center;

            var geometry = em.GetComponentData<EdgeGeometry>(road);

            float t = left ? .2f : .8f;

            var first = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, t);

            var last = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, t);

            return new Bezier4x3(first.a, first.b + (first.c-first.b)*.5f, last.c + (last.b-last.c)*.5f, last.d);

        }

    }

}

