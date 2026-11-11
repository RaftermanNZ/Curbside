using System;
using System.Reflection;
class ToolFamilyAuditChecks212
{
    static Assembly a = Assembly.LoadFrom("MedianGrassReplacement.dll");
    static int checks;
    static MethodInfo M(string t,string n) => a.GetType("MedianGrassReplacement.Systems."+t,true).GetMethod(n,BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic);
    static object Call(string t,string n,params object[] args) => M(t,n).Invoke(null,args);
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Main()
    {
        // Actual compiled family decisions; no game world/native renderer executed.
        foreach(byte s in new byte[]{1,2,3,4,5,6,7,8,9,10,11,12})
        {
            bool yellow=s==2||s==4||(s>=8&&s!=11);
            byte c=(byte)Call("LineShapePolicy","Center",s),e=(byte)Call("LineShapePolicy","Edge",s),d=(byte)Call("LineShapePolicy","Divider",s);
            Check((bool)Call("CenterlineUpgradePolicy","YellowPaint",c,false)==yellow,"Center style color");
            Check((bool)Call("EdgeUpgradePolicy","IsYellow",e)==yellow,"Edge style color");
            Check((bool)Call("LaneDividerPolicy","IsYellow",d)==yellow,"Divider style color");
            Check((bool)Call("LaneDividerPolicy","Applied",d),"Divider removal alias");
            Check((bool)Call("LineShapePolicy","Passing",s)==(s>=11),"Passing family");
            Check((bool)Call("LineShapePolicy","Dashed",s)==(s>=5&&s<=10),"Dashed family");
        }
        foreach(bool intent in new[]{false,true}) foreach(byte appearance in new byte[]{0,1,2})
        foreach(bool invisible in new[]{false,true}) foreach(bool hasStyle in new[]{false,true}) foreach(bool lined in new[]{false,true})
        {
            byte expected=intent?appearance:invisible?(byte)2:hasStyle&&lined?(byte)1:(byte)0;
            Check((byte)Call("CrosswalkPreviewChoicePolicy","Appearance",intent,appearance,invisible,hasStyle,lined)==expected,"Crosswalk choice priority");
        }
        var roadType=a.GetType("MedianGrassReplacement.Systems.MGRCrosswalkRoadStyle",true);
        var hiddenType=a.GetType("MedianGrassReplacement.Systems.MGRInvisibleCrosswalkStyle",true);
        foreach(bool firstLine in new[]{false,true}) foreach(bool secondLine in new[]{false,true})
        {
            object road=Activator.CreateInstance(roadType);
            M("MGRCrosswalkRoadStyle","Set").Invoke(road,new object[]{true,firstLine});
            M("MGRCrosswalkRoadStyle","Set").Invoke(road,new object[]{false,secondLine});
            foreach(bool start in new[]{false,true})
            {
                object[] args={start,false};
                Check((bool)M("MGRCrosswalkRoadStyle","TryGet").Invoke(road,args),"Crosswalk end override lost");
                Check((bool)args[1]==(start?firstLine:secondLine),"Opposite crossing contaminated");
            }
        }
        object hidden=Activator.CreateInstance(hiddenType);
        M("MGRInvisibleCrosswalkStyle","Set").Invoke(hidden,new object[]{true,true});
        Check((bool)M("MGRInvisibleCrosswalkStyle","Has").Invoke(hidden,new object[]{true}),"Hidden start lost");
        Check(!(bool)M("MGRInvisibleCrosswalkStyle","Has").Invoke(hidden,new object[]{false}),"Hidden choice spread to other end");
        M("MGRInvisibleCrosswalkStyle","Set").Invoke(hidden,new object[]{false,true});
        M("MGRInvisibleCrosswalkStyle","Set").Invoke(hidden,new object[]{true,false});
        Check((bool)M("MGRInvisibleCrosswalkStyle","Has").Invoke(hidden,new object[]{false}),"Restore start erased hidden end");
        foreach(bool pure in new[]{false,true}) foreach(bool arrow in new[]{false,true}) foreach(bool speed in new[]{false,true})
        foreach(string name in new string[]{null,"ordinary decal","Speed Limit 40","speed_limit_50","SPEED-LIMIT-60"})
        {
            bool named=name!=null&&name.Replace(" ","").Replace("_","").Replace("-","").IndexOf("speedlimit",StringComparison.OrdinalIgnoreCase)>=0;
            byte expected=!pure?(byte)0:arrow?(byte)1:speed||named?(byte)2:(byte)0;
            Check((byte)Call("DecalPolicy","Classify",pure,arrow,speed,name)==expected,"Arrow/speed/physical-sign classification");
        }
        for(int mask=0;mask<256;mask++) foreach(byte selection in new byte[]{0,1,2,3}) foreach(bool remove in new[]{false,true})
            Check((byte)Call("DecalPolicy","Apply",(byte)mask,selection,remove)==(byte)((remove?mask&~selection:mask|selection)&3),"Independent arrow/speed masks");
        foreach(string name in new[]{"TownRoadLane.Components.TRLSegmentLink","TownRoadLane.Components.TRLAreaLink","TownRoadLane.Components.MarkingLine"})
            Check((bool)Call("ExternalPaintPolicy","IsUserPaint",name),"Known third-party paint ownership");
        foreach(string name in new string[]{null,"OtherMod.Marking","TownRoadLane.Simulation.Road"})
            Check(!(bool)Call("ExternalPaintPolicy","IsUserPaint",name),"Unrelated foreign paint claimed");
        Console.WriteLine("PASS: "+checks+" compiled marking-family decisions. Native tool execution, renderer behavior and FPS not tested.");
    }
}
