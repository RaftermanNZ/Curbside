using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static class ParkingLaneReadPolicy
    {
        internal static bool NeedsPosition(byte left, byte right, byte banMask)
            => left != right || (banMask & 3) == 1 || (banMask & 3) == 2;

        internal static byte SelectMode(byte left, byte right, byte banMask, bool isLeft)
            => (banMask & (isLeft ? 1 : 2)) != 0 ? (byte)1 : isLeft ? left : right;

        internal static byte Mode(EntityManager em, Entity road, Entity lane)
        {
            var sides = RoadSideUpgrades.Parking(em, road);
            byte mask = em.HasComponent<MGRParkingBanSides>(road)
                ? em.GetComponentData<MGRParkingBanSides>(road).Mask : (byte)0;
            // Uniform choices need no lane curve or road-side geometry. For
            // asymmetric choices retain the original side test and its safe read.
            bool left = !NeedsPosition(sides.Left, sides.Right, mask)
                || RoadSideUpgrades.IsLeft(em, road,
                    MathUtils.Position(em.GetComponentData<Curve>(lane).m_Bezier, .5f));
            return SelectMode(sides.Left, sides.Right, mask, left);
        }
    }
}
