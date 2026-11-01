using System;

using System.IO;

using UnityEngine;



namespace MedianGrassReplacement.Systems

{

    internal static class FileTextureLoader

    {

        private const long MaxFileBytes = 64L * 1024L * 1024L;

        private const int MaxDimension = 4096; // Bound a single RGBA+mips upload to about85MiB.
        internal static bool PngHeaderFits(byte[] header,int limit)
        {
            if(header==null||header.Length<24||limit<1)return false;
            byte[] signature={137,80,78,71,13,10,26,10};for(int i=0;i<8;i++)if(header[i]!=signature[i])return false;
            if(header[8]!=0||header[9]!=0||header[10]!=0||header[11]!=13||header[12]!=73||header[13]!=72||header[14]!=68||header[15]!=82)return false;
            uint width=(uint)header[16]<<24|(uint)header[17]<<16|(uint)header[18]<<8|header[19],height=(uint)header[20]<<24|(uint)header[21]<<16|(uint)header[22]<<8|header[23];
            return width>0&&height>0&&width<=limit&&height<=limit;
        }



        internal static Texture2D LoadPng(string path, bool linear, out string error, bool keepReadable = false,long maxFileBytes=MaxFileBytes,int dimensionLimit=MaxDimension)

        {

            error = string.Empty;
            Texture2D texture=null;

            try

            {

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))

                {

                    error = "File was not found.";

                    return null;

                }



                var info = new FileInfo(path);

                maxFileBytes=Math.Min(128L*1024*1024,Math.Max(1,maxFileBytes));dimensionLimit=Math.Min(MaxDimension,Math.Max(1,dimensionLimit));
                if (info.Length <= 0 || info.Length > maxFileBytes)

                {

                    error = $"File size must be between 1 byte and {maxFileBytes / (1024 * 1024)} MB.";

                    return null;

                }



                using(var header=File.OpenRead(path))
                {
                    var bytesHeader=new byte[24];if(header.Read(bytesHeader,0,24)!=24||!PngHeaderFits(bytesHeader,dimensionLimit)){error="Invalid PNG header or dimensions exceed "+dimensionLimit+" pixels per side.";return null;}
                }
                byte[] bytes = File.ReadAllBytes(path);

                texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)

                {

                    name = $"MGR::{Path.GetFileNameWithoutExtension(path)}",

                    wrapMode = TextureWrapMode.Repeat,

                    filterMode = FilterMode.Trilinear,

                    anisoLevel = 8

                };



                if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: !keepReadable))

                {

                    UnityEngine.Object.Destroy(texture);

                    error = "Unity could not decode the PNG.";

                    return null;

                }



                if (texture.width <= 0 || texture.height <= 0

                    || texture.width > dimensionLimit || texture.height > dimensionLimit)

                {

                    int width = texture.width;

                    int height = texture.height;

                    UnityEngine.Object.Destroy(texture);

                    error = $"Texture dimensions {width}x{height} exceed the supported limit of {dimensionLimit}x{dimensionLimit}.";

                    return null;

                }



                texture.wrapMode = TextureWrapMode.Repeat;

                texture.filterMode = FilterMode.Trilinear;

                texture.anisoLevel = 8;

                return texture;

            }

            catch (Exception ex)

            {
                if(texture!=null)UnityEngine.Object.Destroy(texture);

                error = ex.Message;

                return null;

            }

        }



        internal static Texture2D LoadBundledRoadWear(out string error)
        {
            error=string.Empty; Texture2D texture=null;
            try
            {
                using(var stream=BundledAssets.Open("MedianGrassReplacement.RoadWear.png"))
                {
                    if(stream==null||stream.Length<=0||stream.Length>MaxFileBytes){error="Included road-wear texture is unavailable.";return null;}
                    using(var buffer=new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name="MGR::RoadWear",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
                        if(!ImageConversion.LoadImage(texture,buffer.ToArray(),true)||texture.width>MaxDimension||texture.height>MaxDimension)
                        {UnityEngine.Object.Destroy(texture);error="Included road-wear PNG could not be decoded.";return null;}
                        return texture;
                    }
                }
            }
            catch(Exception ex){if(texture!=null)UnityEngine.Object.Destroy(texture);error=ex.Message;return null;}
        }

        internal static bool SameDimensions(Texture a, Texture b)

        {

            return a == null || b == null || (a.width == b.width && a.height == b.height);

        }

    }

}
