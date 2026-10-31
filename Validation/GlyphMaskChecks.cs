using System;using System.Collections.Generic;using System.IO;using System.Reflection;using Unity.Mathematics;
class GlyphMaskChecks
{
 static int checks;
 static readonly MethodInfo Method=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.GlyphMaskGeometry").GetMethod("Rectangles",BindingFlags.Static|BindingFlags.NonPublic);
 static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
 static int Verify(byte[] mask,int width,int height,int4 bounds)
 {
  var rectangles=(List<int4>)Method.Invoke(null,new object[]{mask,width,height,bounds});var covered=new byte[mask.Length];
  foreach(var r in rectangles){Check(r.x>=bounds.x&&r.y>=bounds.y&&r.z<=bounds.z&&r.w<=bounds.w&&r.z>r.x&&r.w>r.y,"rectangle outside glyph");for(int y=r.y;y<r.w;y++)for(int x=r.x;x<r.z;x++){Check(covered[y*width+x]==0,"overlapping glyph rectangles");covered[y*width+x]=1;}}
  for(int y=bounds.y;y<bounds.w;y++)for(int x=bounds.x;x<bounds.z;x++)Check((covered[y*width+x]!=0)==(mask[y*width+x]>=128),"letter hole filled or stroke missing");
  return rectangles.Count;
 }
 static void Main(string[] args)
 {
  var random=new System.Random(76);
  for(int size=2;size<=48;size+=2){var mask=new byte[size*size];random.NextBytes(mask);Verify(mask,size,size,new int4(0,0,size,size));}
  var solid=new byte[32*32];for(int i=0;i<solid.Length;i++)solid[i]=255;Check(Verify(solid,32,32,new int4(0,0,32,32))==1,"solid stem did not merge");
  var ring=(byte[])solid.Clone();for(int y=5;y<27;y++)for(int x=5;x<27;x++)ring[y*32+x]=0;Check(Verify(ring,32,32,new int4(0,0,32,32))==4,"letter O not represented as four strips");
  var atlas=File.ReadAllBytes(args[0]);int rectangles=0;
  for(int y=0;y<1024;y+=32)for(int x=0;x<1024;x+=32)rectangles+=Verify(atlas,1024,1024,new int4(x,y,x+32,y+32));
  Console.WriteLine(checks+" glyph mask checks passed, including native exported font atlas, internal holes, no overlapping faces, and merged stems. Native atlas rectangles="+rectangles);
 }
}
