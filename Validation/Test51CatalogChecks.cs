using System;using System.Reflection;using System.Collections;using System.Collections.Generic;using Game.Net;using Unity.Entities;using Unity.Mathematics;
class Test52CatalogChecks {
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 static Entity E(int i)=>new Entity{Index=i,Version=1};
 static void Main(){
 var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var flags=BindingFlags.Static|BindingFlags.NonPublic;
 var inside=a.GetType("MedianGrassReplacement.Systems.MedianPaintPolicy").GetMethod("Inside",flags);
 foreach(float width in new[]{1f,2f,5f,12f}){var span=new float2(-width*.5f,width*.5f);foreach(float x in new[]{span.x,0,span.y})Check((bool)inside.Invoke(null,new object[]{x,span}),"median interior recognized independent of lane width");foreach(float x in new[]{span.x-.1f,span.y+.1f,float.NaN,float.PositiveInfinity})Check(!(bool)inside.Invoke(null,new object[]{x,span}),"outside/invalid median samples rejected");}
 Check(!(bool)inside.Invoke(null,new object[]{0f,new float2(1,-1)}),"inverted median span rejected");
 var share=a.GetType("MedianGrassReplacement.Systems.RoadTopologyPolicy").GetMethod("ShareEndpoint",flags);
 var edge=new Edge{m_Start=E(1),m_End=E(2)};
 foreach(var other in new[]{new Edge{m_Start=E(2),m_End=E(3)},new Edge{m_Start=E(3),m_End=E(1)},new Edge{m_Start=E(2),m_End=E(1)}})Check((bool)share.Invoke(null,new object[]{edge,other}),"connected roads may share pole");
 Check(!(bool)share.Invoke(null,new object[]{edge,new Edge{m_Start=E(3),m_End=E(4)}}),"unconnected nearby roads cannot share pole");Check(!(bool)share.Invoke(null,new object[]{default(Edge),default(Edge)}),"missing endpoints cannot imply connectivity");
 var panel=a.GetType("MedianGrassReplacement.Systems.CurbsidePanelSystem");var entries=(Array)panel.GetField("Entries",flags).GetValue(null);Check(entries.Length==52,"48 catalog entries include compatibility actions");var ids=new HashSet<string>();var groups=new HashSet<string>();
 foreach(var entry in entries){var t=entry.GetType();Func<string,string> f=name=>(string)t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry);Check(ids.Add(f("Asset")),"unique panel target");Check(Array.Exists(a.GetManifestResourceNames(),x=>x=="MedianGrassReplacement."+f("Icon")),"every panel icon embedded");Check(f("Hint").IndexOf("ight-click",StringComparison.Ordinal)>=0,"removal behavior explained");groups.Add(f("Group"));}
 Check(groups.SetEquals(new[]{"Markings","Visibility","Parking","Crosswalks","Traffic","Decoration","Restore"}),"seven tool groups");
 var names=(string[])a.GetType("MedianGrassReplacement.Systems.UpgradeAssetsSystem").GetField("Names",flags).GetValue(null);foreach(var name in names)Check(ids.Contains(name),"every section upgrade available in panel");
 Console.WriteLine(n+" median region, pole connectivity and panel coverage checks passed.");
 }}
