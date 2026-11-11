using System;

namespace MedianGrassReplacement.Systems
{
    internal static class MarkingSerializationProfile
    {
        // The exclusion contract was traced in these installed modules. Unknown
        // builds use the original save cleanup until their native path is audited.
        internal static bool Matches(Guid game, Guid entities, Guid core)
        {
            return game == new Guid("e15551e9-3dea-4855-8dde-1da3cea7d7ef")
            && entities == new Guid("3d6b23ae-b281-44a6-a5f4-cbd5fd3d75e6")
            && core == new Guid("b5ff7e4e-51b0-4c1f-a177-2afdfc22906b");
        }

        internal static readonly bool Supported = Matches(
            typeof(Game.Serialization.SerializerSystem).Module.ModuleVersionId,
            typeof(Unity.Entities.EntityQuery).Module.ModuleVersionId,
            typeof(Colossal.Serialization.Entities.IWriter).Module.ModuleVersionId);
    }
}
