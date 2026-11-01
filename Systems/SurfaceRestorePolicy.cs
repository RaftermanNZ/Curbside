using System;
namespace MedianGrassReplacement.Systems
{
    internal static class SurfaceRestorePolicy
    {
        // FileTextureLoader's reserved runtime name, not arbitrary PNG filenames.
        internal static bool IsCurbsideTexture(string name)=>name!=null&&name.StartsWith("MGR::",StringComparison.Ordinal)&&name.Length>5;
        internal static bool CanRecover(bool areaBatch,bool registeredMaterial,bool registeredTexture,bool tracked,
            string currentName,bool hasOriginal,bool originalIsCurrent)
            =>areaBatch&&!registeredMaterial&&!registeredTexture&&!tracked&&IsCurbsideTexture(currentName)&&hasOriginal&&!originalIsCurrent;
    }
}
