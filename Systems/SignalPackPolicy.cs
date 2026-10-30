using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace GardenCitySignals
{
    [Serializable] internal sealed class SignalPackManifest
    {
        public string format=null,gameVersion=null;
        public int minimumCurbsideBuild=0;
        public string[] modules=null;
    }
    internal static class SignalPackPolicy
    {
        // The native manifest includes the offline file/hash catalog (553KB in Prepared-Pack-Test1).
        internal const int MaximumManifestBytes=2*1024*1024;
        internal static bool ValidManifestLength(long bytes)=>bytes>0&&bytes<=MaximumManifestBytes;
        internal static int CurrentBuild=>MedianGrassReplacement.BuildIdentity.Number;
        internal static bool ValidManifest(string format,int minimum,string[] modules)
            =>format=="Curbside.NativeSignalPack.v1"&&minimum>0&&minimum<=CurrentBuild&&modules!=null&&modules.Length==SignalModuleCatalog.Names.Length&&modules.Distinct(StringComparer.Ordinal).Count()==modules.Length&&SignalModuleCatalog.Names.All(n=>modules.Contains(n,StringComparer.Ordinal));
        internal static bool ReadLocalManifest(string path)
        {
            try
            {
                var file=new FileInfo(path);
                if(!file.Exists)return false;
                if(!ValidManifestLength(file.Length))throw new InvalidDataException("Manifest must be nonempty and no larger than "+MaximumManifestBytes+" bytes; actual="+file.Length+".");
                var data=JsonUtility.FromJson<SignalPackManifest>(File.ReadAllText(path));
                if(data==null||!ValidManifest(data.format,data.minimumCurbsideBuild,data.modules))throw new InvalidDataException("Unsupported format/build or incomplete module catalog.");
                Mod.Log.Info("Local prepared signal manifest accepted: game="+data.gameVersion+" minimumBuild="+data.minimumCurbsideBuild+". Native loader still validates assets; full file hashing is an offline check.");
                return true;
            }
            catch(Exception error){Mod.Log.Warn("Local prepared signal manifest rejected: "+error.Message);return false;}
        }
    }
}
