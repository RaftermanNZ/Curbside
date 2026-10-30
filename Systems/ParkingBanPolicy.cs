using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRParkingBanSides : IComponentData, ISerializable
    {
        public byte Mask;
        public void Serialize<T>(T writer) where T:IWriter {writer.Write(Mask);}
        public void Deserialize<T>(T reader) where T:IReader {reader.Read(out Mask);Mask&=3;}
    }
    internal static class ParkingBanPolicy
    {
        internal const string AssetName="MGR Ban Parking Side";
        internal static byte ClearMask(byte mask,bool left)=>(byte)(mask&~RoadSideUpgrades.SideMask(left));
        internal static void ClearSide(EntityManager em,Entity road,bool left)
        {
            if(!em.HasComponent<MGRParkingBanSides>(road))return;
            byte mask=ClearMask(em.GetComponentData<MGRParkingBanSides>(road).Mask,left);
            if(mask==0)em.RemoveComponent<MGRParkingBanSides>(road);else em.SetComponentData(road,new MGRParkingBanSides{Mask=mask});
        }
        internal static bool Banned(EntityManager em,Entity road,float3 position)
        {
            return em.HasComponent<MGRParkingBanSides>(road)&&(em.GetComponentData<MGRParkingBanSides>(road).Mask&RoadSideUpgrades.SideMask(RoadSideUpgrades.IsLeft(em,road,position)))!=0;
        }
    }
}
