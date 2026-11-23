using Game.Common;
using Game.Tools;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class NativePreviewOrigin
    {
        internal const int DepthLimit = 16;
        internal static Entity Resolve(EntityManager manager, Entity entity, bool rejectHidden = false)
        {
            for (int depth = 0; depth < DepthLimit; depth++)
            {
                if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity)) return Entity.Null;
                if (!manager.HasComponent<Temp>(entity)) return entity;
                var temp = manager.GetComponentData<Temp>(entity);
                if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0
                    || rejectHidden && (temp.m_Flags & TempFlags.Hidden) != 0
                    || temp.m_Original == entity) return Entity.Null;
                entity = temp.m_Original;
            }
            return Entity.Null;
        }
    }
}
