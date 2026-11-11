using System;using System.Reflection;
class FloridaR19Checks217{
 static readonly Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");static int checks;
 static object Call(string t,string m,params object[] args)=>A.GetType("GardenCitySignals."+t,true).GetMethod(m,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,args);
 static void Check(bool x,string s){checks++;if(!x)throw new Exception(s);}
 static void Main(){
 for(int v=0;v<256;v++)Check((byte)Call("FloridaCatalogPolicy","NormalizeStyle",(byte)v)==(v<=4?v:0),"saved style byte");
 for(int mask=0;mask<4;mask++)Check((bool)Call("FloridaCatalogPolicy","Visible",(mask&1)!=0,(mask&2)!=0)==(mask==3),"original and Florida catalogs required");
 string[] owners={"165366","165366","165366","165366"};
 Check((bool)Call("FloridaCatalogPolicy","Accept",4,false,true,false,"165366",owners),"published owner");
 for(int n=0;n<4;n++)Check(!(bool)Call("FloridaCatalogPolicy","Accept",n,false,true,false,"165366",owners),"partial modules");
 Check(!(bool)Call("FloridaCatalogPolicy","Accept",4,true,true,true,null,owners),"duplicates");
 Check(!(bool)Call("FloridaCatalogPolicy","Accept",4,false,false,true,null,owners),"missing shared originals");
 Check(!(bool)Call("FloridaCatalogPolicy","Accept",4,false,true,false,null,owners),"unowned original");
 for(int i=0;i<4;i++){var mixed=(string[])owners.Clone();mixed[i]="other";Check(!(bool)Call("FloridaCatalogPolicy","Accept",4,false,true,false,"165366",mixed),"mixed owners");}
 Check((bool)Call("FloridaCatalogPolicy","Accept",4,false,true,true,null,new string[4]),"local native combined manifest");
 Check(!(bool)Call("FloridaCatalogPolicy","Accept",4,false,true,false,"165366",new string[4]),"loose files cannot impersonate published modules");
 Check(!(bool)Call("FloridaCatalogPolicy","Expired",29d,0d,false)&&(bool)Call("FloridaCatalogPolicy","Expired",30d,0d,false),"absent probe bounded");
 Check(!(bool)Call("FloridaCatalogPolicy","Expired",119d,0d,true)&&(bool)Call("FloridaCatalogPolicy","Expired",120d,0d,true),"partial probe bounded");
 // Florida2 deliberately removed the old Florida-only placement veto. Keep
 // catalog/save coverage, but do not require the retired spacing/yaw policy.
 // Shared fitting is covered by the intersection fixtures and the full IL
 // comparison against the installed Florida2 Select implementation.
 Check(A.GetType("GardenCitySignals.FloridaSignalPlacement",false)==null,"retired Florida-only veto returned");
 for(byte style=0;style<4;style++){Check((bool)Call("SignalMountClearance","AllowHead",style,false,1.5f),"legacy placement");Check((float)Call("SignalSignPlacement","Along",style,true)==(style==2?1.2f:1.05f),"legacy sign along");Check((float)Call("SignalSignPlacement","Height",style,true)==(style==2?5.77f:5.95f),"legacy sign height");}
 Console.WriteLine("PASS: "+checks+" compiled Florida2 catalog checks: stored style bytes, coherent ownership, bounded startup, retired Florida-only veto absent and legacy anchors. Shared fitting/Select verified separately. Live phases/tools/save/FPS remain unverified.");
 }}
