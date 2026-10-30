using System;using Colossal.Serialization.Entities;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public struct MGRSurfaceSlots:IComponentData,ISerializable
    {
        public byte Road,Sidewalk;
        public void Serialize<T>(T w)where T:IWriter{w.Write(Road);w.Write(Sidewalk);}
        public void Deserialize<T>(T r)where T:IReader{r.Read(out Road);r.Read(out Sidewalk);}
    }
    internal static class SurfaceUpgradePolicy
    {
        internal const string RoadA="MGR Road Surface A",RoadB="MGR Road Surface B",WalkA="MGR Sidewalk Surface A",WalkB="MGR Sidewalk Surface B";
        internal static byte Slot(string name)=>name==RoadA||name==WalkA?(byte)1:name==RoadB||name==WalkB?(byte)2:(byte)0;
        internal static bool Sidewalk(string name)=>name==WalkA||name==WalkB;
        internal static MGRSurfaceSlots Set(MGRSurfaceSlots value,bool walk,byte slot){if(slot>2)slot=0;if(walk)value.Sidewalk=slot;else value.Road=slot;return value;}
        internal static int Kind(string surface,string piece,bool roadLanes)
        {
            string key=(surface??"").ToLowerInvariant();string mesh=(piece??"").ToLowerInvariant();
            foreach(string excluded in new[]{"grass","vegetation","curb","kerb","marking","paint","decal","water","rail","gravel","dirt"})if(key.Contains(excluded))return 0;
            if(key.Contains("sidewalk")||key.Contains("pavement")||key.Contains("paver")||!roadLanes&&(key.Contains("concrete")||key.Contains("tile")))return 2;
            if(key.Contains("asphalt")||key.Contains("tarmac")||roadLanes&&(key.Contains("road")||key.Contains("concrete")))return 1;
            if(mesh.Contains("sidewalk")&&(key.Contains("stone")||key.Contains("brick")))return 2;
            return 0;
        }
        internal static bool FitsBudget(int width,int height,long used)=>width>0&&height>0&&width<=4096&&height<=4096&&used+(long)width*height*16/3<=256L*1024*1024;
        internal static int NativeKind(string surface,string piece,bool roadLanes,bool surfaceLayer)
        {
            string material=(surface??"").ToLowerInvariant(),mesh=(piece??"").ToLowerInvariant();
            foreach(var excluded in new[]{"grass","vegetation","curb","kerb","marking","paint","decal","water","rail","gravel","dirt"})if(material.Contains(excluded)||mesh.Contains(excluded))return 0;
            int known=Kind(surface,piece,roadLanes);if(known!=0)return known;
            if(!surfaceLayer)return 0;
            if(mesh.Contains("sidewalk")||mesh.Contains("pedestrian")||mesh.Contains("footpath")||mesh.Contains("pavement"))return 2;
            if(roadLanes||mesh.Contains("road")||mesh.Contains("street")||mesh.Contains("carriageway")||mesh.Contains("asphalt")||mesh.Contains("tarmac"))return 1;
            return 0;
        }
    }
}
