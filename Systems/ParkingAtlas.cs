using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedianGrassReplacement.Systems
{
    internal static class ParkingAtlasPolicy
    {
        internal const int Size=2048;
        internal const int RoadFirst=0,RoadLast=704,WalkFirst=1114,WalkLast=1542;
        // Native mesh UV evidence: parking tread U=0..0.171875 at y=-0.2;
        // raised walk U=0.271973..0.376221 at y=0. Curbs between stay native.
        // Pixel-aligned conservative ranges in the original 4096-wide atlas.
        internal static int Region(int x)=>x>=RoadFirst&&x<RoadLast?1:x>=WalkFirst&&x<WalkLast?2:0;
        internal static long Bytes=>(long)Size*Size*16/3;
    }

    internal static class ParkingAtlas
    {
        // One shared, non-readable texture per current selection, never per road.
        // No native mesh/buffer edits, source asset loading or CPU readback.
        internal static Texture2D Compose(Texture source,Texture road,Texture walk,float roadScale,float walkScale)
        {
            if(source==null||!NetworkTextureScope.SharedParkingAtlas(source.name,source.width,source.height))return null;
            const CopyTextureSupport required=CopyTextureSupport.Basic|CopyTextureSupport.RTToTexture;
            if((SystemInfo.copyTextureSupport&required)!=required)return null;
            RenderTexture target=null;Texture2D result=null;
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite,pushed=false;
            try
            {
                target=new RenderTexture(ParkingAtlasPolicy.Size,ParkingAtlasPolicy.Size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                {hideFlags=HideFlags.DontSave,useMipMap=true,autoGenerateMips=false,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
                if(!target.Create())return null;
                GL.sRGBWrite=true;Graphics.Blit(source,target);RenderTexture.active=target;
                GL.PushMatrix();pushed=true;GL.LoadPixelMatrix(0,ParkingAtlasPolicy.Size,ParkingAtlasPolicy.Size,0);
                void Band(Texture texture,int first,int last,float scale)
                {
                    if(texture==null)return;
                    float left=first*.5f,width=(last-first)*.5f;
                    Graphics.DrawTexture(new Rect(left,0,width,ParkingAtlasPolicy.Size),texture,
                        new Rect(0,0,scale*width/ParkingAtlasPolicy.Size,scale),0,0,0,0,new Color(.5f,.5f,.5f,.5f));
                }
                Band(road,ParkingAtlasPolicy.RoadFirst,ParkingAtlasPolicy.RoadLast,32f*GlobalSurfacePolicy.RoadUv(roadScale));
                Band(walk,ParkingAtlasPolicy.WalkFirst,ParkingAtlasPolicy.WalkLast,GlobalSurfacePolicy.SidewalkTiling(walkScale));
                GL.PopMatrix();pushed=false;target.GenerateMips();
                result=new Texture2D(ParkingAtlasPolicy.Size,ParkingAtlasPolicy.Size,target.graphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
                {name="MGR::ParkingAtlas-v1",hideFlags=HideFlags.DontSave,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
                // RTs are not subject to global texture mip limits. Keep the
                // destination's physical mip sizes identical for this GPU copy.
                result.ignoreMipmapLimit=true;
                // Drop CPU storage before the GPU copy; Apply after it would
                // overwrite copied pixels with the initial CPU image.
                result.Apply(false,true);
                for(int mip=0;mip<result.mipmapCount;mip++)Graphics.CopyTexture(target,0,mip,result,0,mip);
                var completed=result;result=null;return completed;
            }
            finally
            {
                if(pushed)GL.PopMatrix();RenderTexture.active=previous;GL.sRGBWrite=srgb;
                if(result!=null)UnityEngine.Object.Destroy(result);
                if(target!=null)UnityEngine.Object.Destroy(target);
            }
        }
    }
}
