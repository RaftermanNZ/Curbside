using System;
using System.Reflection;
class PreviewVisibilityChecks
{
 static void Main(){var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var policy=a.GetType("MedianGrassReplacement.Systems.PaintPreviewVisibilityPolicy",true);var method=policy.GetMethod("Suppress",BindingFlags.Static|BindingFlags.NonPublic);int count=0;
 for(int bits=0;bits<16;bits++){bool exists=(bits&1)!=0,owned=(bits&2)!=0,simulation=(bits&4)!=0,external=(bits&8)!=0;
  bool result=(bool)method.Invoke(null,new object[]{exists,owned,simulation,external});
  if(result!=(bits==3))throw new Exception("Preview suppression escaped exact owned/non-simulation scope: "+bits);count++;}
 var system=a.GetType("MedianGrassReplacement.Systems.PaintPreviewVisibilitySystem",true);
 foreach(var field in system.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
 {var name=field.FieldType.FullName??"";if(name.Contains("NativeArray")||name.Contains("DynamicBuffer")||field.FieldType.IsPointer)throw new Exception("Retained borrowed/native preview data");count++;}
 Console.WriteLine("PASS: "+count+" preview visibility truth-table and managed lifetime checks; preview-only runtime behavior still requires game validation.");}
}
