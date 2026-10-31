using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Game.Settings;

class SettingsContractChecks
{
    static readonly Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");
    static readonly Type T=A.GetType("MedianGrassReplacement.Setting",true);
    static int count;
    static void Check(bool ok,string text){count++;if(!ok)throw new Exception(text);}
    static object Fresh()
    {
        // Exercise the actual settings accessors without starting a Unity world.
        var s=FormatterServices.GetUninitializedObject(T);
        foreach(var f in T.GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
            if(f.FieldType.Name=="DecalNumberDraft")f.SetValue(s,Activator.CreateInstance(f.FieldType,true));
        Reset(s);return s;
    }
    static void Reset(object s)
    {
        T.GetMethod("SetDefaultValues",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{System.IO.Path.GetFullPath("C:/Curbside-Settings-Fixture")});
        // Unity's persistent-data path needs a running player. Supply the path
        // fixture for that derived getter; do not mock any setter/validation.
        T.GetField("_decalFolder",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(s,System.IO.Path.GetFullPath("C:/Curbside-Settings-Fixture/Decals"));
    }
    static object Get(object s,string p)=>T.GetProperty(p).GetValue(s);
    static void Set(object s,string p,object value)=>T.GetProperty(p).SetValue(s,value);
    static bool Attr(PropertyInfo p,string name)=>p.GetCustomAttributesData().Any(a=>a.AttributeType.Name==name);
    static void Main()
    {
        var s=Fresh();
        Check((bool)Get(s,"UnlimitedDecals"),"Road wear must have no city-wide cap");
        Set(s,"UnlimitedDecals",false);Check((bool)Get(s,"UnlimitedDecals"),"Legacy settings re-enabled city-wide cap");
        Check(Attr(T.GetProperty("UnlimitedDecals"),"SettingsUIHiddenAttribute")&&Attr(T.GetProperty("CurbDecalLimit"),"SettingsUIHiddenAttribute")&&Attr(T.GetProperty("ExactDecalLimit"),"SettingsUIHiddenAttribute"),"Retired decal limit controls are visible");
        Check(!(bool)Get(s,"AlleyWhiteEdges"),"Alley edges default off");
        Check(!(bool)Get(s,"DecalsBothSides"),"Road wear must default to one roadside");
        Check(!(bool)Get(s,"PreserveVanillaJunctionLines"),"Existing settings without the new option must retain previous marking behavior");
        Check((string)Get(s,"Version")==A.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.Split('+')[0],"About must report the candidate version");
        int controls=0,sliders=0,callbacks=0;
        foreach(var p in T.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
        {
            if(!Attr(p,"SettingsUISectionAttribute")||Attr(p,"SettingsUIHiddenAttribute"))continue;
            controls++;
            foreach(var a in p.GetCustomAttributesData())
            {
                if(!new[]{"SettingsUIHideByConditionAttribute","SettingsUIDisableByConditionAttribute","SettingsUIDropdownAttribute","SettingsUIValueVersionAttribute","SettingsUISetterAttribute"}.Contains(a.AttributeType.Name))continue;
                var type=a.ConstructorArguments.FirstOrDefault(x=>x.ArgumentType==typeof(Type)).Value as Type;
                var method=a.ConstructorArguments.FirstOrDefault(x=>x.ArgumentType==typeof(string)).Value as string;
                Check(type!=null&&method!=null,p.Name+": missing callback metadata");
                var m=type.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
                Check(m!=null,p.Name+": unresolved callback "+method);callbacks++;
                if(a.AttributeType.Name.Contains("Condition"))Check(m.ReturnType==typeof(bool)&&m.GetParameters().Length==0,p.Name+": invalid condition signature");
                if(a.AttributeType.Name=="SettingsUIValueVersionAttribute")Check(m.ReturnType==typeof(int)&&m.GetParameters().Length==0,p.Name+": invalid revision callback");
                if(a.AttributeType.Name=="SettingsUIDropdownAttribute")Check(m.ReturnType.IsArray&&m.GetParameters().Length==0,p.Name+": invalid dropdown callback");
                if(a.AttributeType.Name=="SettingsUISetterAttribute")Check(m.ReturnType==typeof(void)&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==p.PropertyType,p.Name+": invalid setter callback");
            }
            if(p.CanRead&&p.CanWrite&&p.PropertyType==typeof(bool)&&!Attr(p,"ExcludeAttribute"))
            {
                bool old=(bool)p.GetValue(s);p.SetValue(s,!old);Check((bool)p.GetValue(s)==!old,p.Name+": checkbox did not retain edit");p.SetValue(s,old);
            }
            var range=p.GetCustomAttribute<SettingsUISliderAttribute>();
            if(range==null)continue;sliders++;
            double initial=Convert.ToDouble(p.GetValue(s));Check(initial>=range.min&&initial<=range.max,p.Name+": default outside UI range");
            var inputs=p.PropertyType==typeof(float)?new double[]{float.NaN,float.NegativeInfinity,float.PositiveInfinity,-1e6,1e6}:new double[]{int.MinValue,int.MaxValue};
            foreach(var v in inputs)
            {
                p.SetValue(s,Convert.ChangeType(v,p.PropertyType));double actual=Convert.ToDouble(p.GetValue(s));
                Check(!double.IsNaN(actual)&&!double.IsInfinity(actual)&&actual>=range.min&&actual<=range.max,p.Name+": invalid number escaped its range");
            }
            p.SetValue(s,Convert.ChangeType(initial,p.PropertyType));
        }
        // Persisted opt-outs must not erase chosen textures or alter unrelated controls.
        Set(s,"GlobalRoadTexture","C:/Example/road.png");Set(s,"GlobalSidewalkTexture","C:/Example/walk.png");Set(s,"SelectedTexture","grass.png");
        Set(s,"UnlimitedDecals",true);
        foreach(string p in new[]{"DisableAutomaticMarkings","DisableAutomaticHighwayMarkings","DisableAutomaticLineAdjustments","DisableTextureReplacement","PreserveVanillaJunctionLines"})Set(s,p,true);
        var properties=T.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
            .Where(p=>p.CanRead&&p.CanWrite&&!Attr(p,"ExcludeAttribute")&&(p.PropertyType==typeof(bool)||p.PropertyType==typeof(float)||p.PropertyType==typeof(int)||p.PropertyType==typeof(string))).ToArray();
        var snapshot=properties.ToDictionary(p=>p.Name,p=>p.GetValue(s));
        Set(s,"PreserveVanillaJunctionLines",false);
        foreach(var p in properties.Where(p=>p.Name!="PreserveVanillaJunctionLines"))Check(Equals(snapshot[p.Name],p.GetValue(s)),p.Name+": new opt-out altered another saved setting");
        Set(s,"PreserveVanillaJunctionLines",true);
        foreach(var p in properties.Where(p=>p.Name!="PreserveVanillaJunctionLines"))Check(Equals(snapshot[p.Name],p.GetValue(s)),p.Name+": new opt-in altered another saved setting");
        var json=new JavaScriptSerializer();var decoded=json.Deserialize<Dictionary<string,object>>(json.Serialize(snapshot));var loaded=Fresh();
        foreach(var p in properties)p.SetValue(loaded,decoded[p.Name]==null?null:Convert.ChangeType(decoded[p.Name],p.PropertyType));
        foreach(var p in properties)Check(Equals(p.GetValue(s),p.GetValue(loaded)),p.Name+": settings replay changed a saved value");
        Check((bool)Get(loaded,"UnlimitedDecals"),"Explicit existing unlimited choice overwritten");
        foreach(string p in new[]{"DisableAutomaticMarkings","DisableAutomaticHighwayMarkings","DisableAutomaticLineAdjustments","DisableTextureReplacement","PreserveVanillaJunctionLines"})Check((bool)Get(loaded,p),p+": saved opt-out/opt-in lost");
        Reset(s);var defaults=Fresh();
        foreach(var p in properties)Check(Equals(p.GetValue(s),p.GetValue(defaults)),p.Name+": reset is not deterministic");
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach(string locale in new[]{"en-US","fr-FR","de-DE","sv-SE"})
            {
                System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(locale);
                var exact=Fresh();float before=(float)Get(exact,"CurbDecalWidth");
                Set(exact,"ExactDecalWidth",19.25f.ToString(System.Globalization.CultureInfo.CurrentCulture));
                Set(exact,"ExactDecalRotation","not a number");Set(exact,"ApplyExactDecalValues",true);
                Check((float)Get(exact,"CurbDecalWidth")==before,"Invalid exact edit partially applied: "+locale);
                Set(exact,"ExactDecalRotation","-45");Set(exact,"ApplyExactDecalValues",true);
                Check((float)Get(exact,"CurbDecalWidth")==19.25f,"Localized exact decimal rejected: "+locale);
                Set(exact,"ExactDecalWidth","17.125");Set(exact,"ApplyExactDecalValues",true);
                Check((float)Get(exact,"CurbDecalWidth")==17.125f,"Invariant decimal fallback rejected: "+locale);
                Set(exact,"ExactDecalWidth","NaN");Set(exact,"ApplyExactDecalValues",true);
                Check((float)Get(exact,"CurbDecalWidth")==17.125f,"Non-finite exact edit applied: "+locale);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture=culture; }
        Check(controls>40&&sliders>15&&callbacks>10,"Settings inventory unexpectedly incomplete");
        Console.WriteLine("PASS: "+count+" settings contract checks; "+controls+" controls, "+sliders+" bounded sliders, "+callbacks+" callbacks. Offline accessor/replay checks; native file persistence and live controls require in-game testing.");
    }
}
