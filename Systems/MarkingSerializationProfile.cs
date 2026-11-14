using System;

namespace MedianGrassReplacement.Systems
{
    internal static class MarkingSerializationProfile
    {
        // The exclusion contract was traced in these installed modules. Unknown
        // builds use the original save cleanup until their native path is audited.
        internal static bool Matches(Guid game, Guid entities, Guid core)
        {
            return (game == new Guid("e15551e9-3dea-4855-8dde-1da3cea7d7ef")
            && entities == new Guid("3d6b23ae-b281-44a6-a5f4-cbd5fd3d75e6")
            && core == new Guid("b5ff7e4e-51b0-4c1f-a177-2afdfc22906b"))
                // Current installed game: serializer selection, native query
                // exclusion, writer ownership and save/GPU ordering re-audited.
                || (game == new Guid("46198354-d4f9-4d23-bf32-7e3c085d8b92")
                && entities == new Guid("bfb47e8c-9b6e-4acf-8c6d-c80a7bff1c41")
                && core == new Guid("4e1271d0-ee7e-4fff-afd8-c460d5ad5c2d"));
        }

        internal static readonly bool Supported = Matches(
            typeof(Game.Serialization.SerializerSystem).Module.ModuleVersionId,
            typeof(Unity.Entities.EntityQuery).Module.ModuleVersionId,
            typeof(Colossal.Serialization.Entities.IWriter).Module.ModuleVersionId);
    }
}
