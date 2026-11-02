using Game.Common;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    internal static class UpgradePersistenceDiagnostics
    {
        // Explicit snapshot only. Never a per-frame city scan or save mutation.
        internal static void Record(World world)
        {
            if(world==null||!world.IsCreated)return;
            Mod.Log.Info("Persistence snapshot: preferences are global; entity upgrade intents are saved with the city. "+SettingsSaveSystem.PersistenceStatus);
            Count<MGRParkingSides>(world);Count<MGRCenterlineStyle>(world);Count<MGREdgePaint>(world);Count<MGRLaneDivider>(world);
            Count<MGRCrosswalkChoice>(world);Count<MGRHighwayLineIntent>(world);Count<MGRParkingHatching>(world);Count<MGRCurbDecalSides>(world);
            var lines=world.GetExistingSystemManaged<CurbLineEmissionSystem>();var paint=world.GetExistingSystemManaged<IntersectionPaintSystem>();
            Mod.Log.Info("Persistence visual systems: curbLines="+(lines!=null&&lines.Enabled)+" intersectionPaint="+(paint!=null&&paint.Enabled)+" saving="+Mod.Saving);
        }
        private static void Count<T>(World world) where T:struct
        {
            using(var query=world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>()))
                Mod.Log.Info("Saved intent owners "+typeof(T).Name+"="+query.CalculateEntityCount());
        }
    }
}
