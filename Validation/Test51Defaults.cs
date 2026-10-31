using System;using System.Reflection;using System.Security.Cryptography;using System.Collections;using Colossal.Mathematics;using Unity.Mathematics;
class Test52Defaults
{
 static int n;static Assembly a;static BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
 static object Call(string type,string method,params object[] args)=>a.GetType("MedianGrassReplacement.Systems."+type,true).GetMethod(method,flags).Invoke(null,args);
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
 static void Main(){a=Assembly.LoadFrom("MedianGrassReplacement.dll");
  string[] names={"RoadSurface","SidewalkSurface"},hashes={"6f9a6b4f2e17bb1fb70c7cfe0dcb3ad1873b5c1c0474c47e6bfe70982b2bc05b","32752d49a30e16a140cce65737e3b410b57b62c12c2c0cb8e48539c384b8aac1"};
  for(int i=0;i<names.Length;i++)using(var stream=a.GetManifestResourceStream("MedianGrassReplacement."+names[i]+".png")){Check(stream!=null,"bundled default resource exists");Check(stream.Length<=128L*1024*1024,"bundled PNG fits surface file limit");using(var sha=SHA256.Create())Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()==hashes[i],"resized runtime texture hash retained");stream.Position=0;var header=new byte[24];stream.Read(header,0,24);Check((bool)Call("FileTextureLoader","PngHeaderFits",header,4096),"original PNG dimensions accepted before decode");Check(!(bool)Call("FileTextureLoader","PngHeaderFits",header,2047),"oversize image rejected by header preflight");header[0]=0;Check(!(bool)Call("FileTextureLoader","PngHeaderFits",header,4096),"invalid PNG signature rejected");}
  long texture=(long)4096*4096*16/3;Check((bool)Call("SurfaceUpgradePolicy","FitsBudget",4096,4096,texture),"both 4k defaults fit together");Check(!(bool)Call("SurfaceUpgradePolicy","FitsBudget",4096,4096,texture*3),"world texture budget remains bounded");
  foreach(float width in new[]{2f,3f,6f})foreach(float length in new[]{.6f,1f,2f}){var p=new Bezier4x3(new float3(0),new float3(0,0,length/3),new float3(0,0,length*2/3),new float3(0,0,length));Check(((IList)Call("ParkingHatchPolicy","Build",p,width,0f,1f)).Count==0,"square/short hatch fragments are not emitted");}
  var full=new Bezier4x3(new float3(0),new float3(0,0,7f/3),new float3(0,0,14f/3),new float3(0,0,7));Check(((IList)Call("ParkingHatchPolicy","Build",full,2f,0f,1f)).Count>4,"complete excluded bay renders hatch strokes");
  Console.WriteLine(n+" 2K runtime texture hashes, dual-default memory and elongated hatch checks passed.");
 }
}
