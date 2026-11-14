using System;
using System.Linq;
using System.Reflection;

class ReleaseVersionChecks
{
    static readonly Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");
    const BindingFlags F=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Call(Type t,string method,params object[] args)=>t.GetMethod(method,F).Invoke(null,args);
    static object Get(Type t,string property)=>t.GetProperty(property,F).GetValue(null);
    static void Main()
    {
        var identity=A.GetType("MedianGrassReplacement.BuildIdentity",true);
        Check((string)identity.GetField("Version",F).GetValue(null)=="0.6.2","Public version");
        Check((string)Get(identity,"Label")=="0.6.2","No test label");
        Check((int)identity.GetField("Number",F).GetValue(null)==243,"Independent compatibility build");
        Check(A.GetName().Version.ToString()=="0.5.1.0","Preserved assembly identity");
        Check(A.GetCustomAttribute<AssemblyFileVersionAttribute>().Version=="0.6.2.0","File version");
        var policy=A.GetType("GardenCitySignals.SignalPackPolicy",true);
        var modules=(string[])A.GetType("GardenCitySignals.SignalModuleCatalog",true).GetField("Names",F).GetValue(null);
        var florida=(string[])A.GetType("GardenCitySignals.FloridaModuleCatalog",true).GetField("Names",F).GetValue(null);
        var combined=modules.Concat(florida).ToArray();
        Check((int)Get(policy,"CurrentBuild")==243,"Manifest compatibility independent of SemVer patch");
        Check((bool)Call(policy,"ValidManifest","Curbside.NativeSignalPack.v1",224,combined),"R23 manifest accepted");
        Check((bool)Call(policy,"ValidManifest","Curbside.NativeSignalPack.v1",214,modules),"Original manifest accepted");
        Check(!(bool)Call(policy,"ValidManifest","Curbside.NativeSignalPack.v1",244,combined),"Future manifest rejected");
        Check(!(bool)Call(policy,"ValidManifest","wrong",224,combined),"Unknown format rejected");
        Check(!(bool)Call(policy,"ValidManifest","Curbside.NativeSignalPack.v1",224,combined.Take(68).ToArray()),"Partial catalog rejected");
        Check(!(bool)Get(policy,"UseRigidSignLayout")&&!(bool)Get(policy,"UseLowerRigidSignLayout")&&!(bool)Get(policy,"IncludesFlorida"),"Legacy published pack remains legacy until Florida proof");
        Call(policy,"AdoptPublishedFloridaLayout");
        Check((bool)Get(policy,"UseRigidSignLayout")&&(bool)Get(policy,"UseLowerRigidSignLayout")&&(bool)Get(policy,"IncludesFlorida"),"Owned published Florida adopts R23");
        var placement=A.GetType("GardenCitySignals.SignalSignPlacement",true);
        Check(Math.Abs((float)Call(placement,"Height",(byte)2,true)-5.52f)<.00001f,"Published R23 sign clearance");
        Check(Math.Abs((float)Call(placement,"Height",(byte)2,false)-5.95f)<.00001f,"Legacy sign placement unchanged");
        Call(policy,"AdoptPublishedFloridaLayout");
        Check(Math.Abs((float)Call(placement,"Height",(byte)2,true)-5.52f)<.00001f,"Adoption idempotent");
        Console.WriteLine("PASS: "+checks+" release identity and published-catalog policy checks. Native asset loading is a separate check.");
    }
}
