using System;
using System.Collections.Generic;
using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Merge identical filled runs vertically. Holes remain empty geometry,
    // independent of which alpha-test variants a shipped player contains.
    internal static class GlyphMaskGeometry
    {
        internal static List<int4> Rectangles(byte[] pixels,int width,int height,int4 bounds)
        {
            if(pixels==null||width<1||height<1||(long)width*height!=pixels.Length||bounds.x<0||bounds.y<0||bounds.z>width||bounds.w>height||bounds.z<=bounds.x||bounds.w<=bounds.y)throw new ArgumentException("Invalid glyph mask bounds");
            var output=new List<int4>();var previous=new Dictionary<long,int4>();var current=new Dictionary<long,int4>();
            for(int y=bounds.y;y<bounds.w;y++)
            {
                for(int x=bounds.x;x<bounds.z;)
                {
                    if(pixels[y*width+x]<128){x++;continue;}
                    int start=x++;while(x<bounds.z&&pixels[y*width+x]>=128)x++;
                    long key=((long)start<<32)|(uint)x;
                    if(previous.TryGetValue(key,out var rectangle)){previous.Remove(key);rectangle.w=y+1;}
                    else rectangle=new int4(start,y,x,y+1);
                    current.Add(key,rectangle);
                }
                foreach(var rectangle in previous.Values)output.Add(rectangle);
                var swap=previous;previous=current;current=swap;current.Clear();
            }
            foreach(var rectangle in previous.Values)output.Add(rectangle);
            return output;
        }
    }
}
