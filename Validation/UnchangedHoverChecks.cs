using System;
using System.Reflection;
using Colossal.Mathematics;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

class UnchangedHoverChecks
{
    static Assembly mod = Assembly.LoadFrom("MedianGrassReplacement.dll");
    static int count;
    static object Call(string t, string name, params object[] args)
        => mod.GetType("MedianGrassReplacement.Systems."+t,true).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
    static Entity E(int index,int version=1)=>new Entity{Index=index,Version=version};
    static Bezier4x3 Line(float3 a,float3 b)=>new Bezier4x3(a,math.lerp(a,b,1f/3f),math.lerp(a,b,2f/3f),b);
    static bool Binding(Entity source,Entity expectedSource,Entity owner,Entity expectedOwner,Entity prefab,Entity expectedPrefab,
        Bezier4x3 original,Bezier4x3 expectedOriginal,Bezier4x3 preview,Bezier4x3 expectedPreview)
        =>(bool)Call("CommittedHoverPolicy","SameBinding",source,expectedSource,owner,expectedOwner,prefab,expectedPrefab,original,expectedOriginal,preview,expectedPreview);

    static void Main()
    {
        var source=E(10);var owner=E(20);var prefab=E(30);
        for(int rotation=0;rotation<360;rotation+=15)foreach(float3 offset in new[]{float3.zero,new float3(10000,200,-20000)})
        {
            var q=quaternion.RotateY(math.radians(rotation));var direction=math.rotate(q,new float3(40,0,0));
            var line=Line(offset,offset+direction);
            Check(Binding(source,source,owner,owner,prefab,prefab,line,line,line,line),"Unchanged committed binding rejected");
            Check(!Binding(E(10,2),source,owner,owner,prefab,prefab,line,line,line,line),"Recycled source entity reused cache");
            Check(!Binding(source,source,E(20,2),owner,prefab,prefab,line,line,line,line),"Recycled preview owner reused cache");
            Check(!Binding(source,source,owner,owner,E(30,2),prefab,line,line,line,line),"Recycled definition reused cache");
            var moved=line;moved.b+=new float3(0,.01f,0);
            Check(!Binding(source,source,owner,owner,prefab,prefab,moved,line,line,line),"Changed original control point reused cache");
            Check(!Binding(source,source,owner,owner,prefab,prefab,line,line,moved,line),"Changed proposed curve reused cache");
            var pose=new Transform{m_Position=offset,m_Rotation=q};
            Check((bool)Call("DecalHoverPolicy","SamePose",pose,pose),"Same decal pose lost");
            var alias=pose;alias.m_Rotation.value=-alias.m_Rotation.value;
            Check((bool)Call("DecalHoverPolicy","SamePose",pose,alias),"Equivalent quaternion rejected");
            var changed=pose;changed.m_Position+=new float3(0,0,.01f);
            Check(!(bool)Call("DecalHoverPolicy","SamePose",pose,changed),"Moved decal inherited hover suppression");
            changed=pose;changed.m_Rotation=math.mul(q,quaternion.RotateY(.1f));
            Check(!(bool)Call("DecalHoverPolicy","SamePose",pose,changed),"Rotated decal inherited hover suppression");
        }
        var invalid=Line(float3.zero,new float3(10,0,0));invalid.a.x=float.NaN;
        Check(!Binding(source,source,owner,owner,prefab,prefab,invalid,invalid,invalid,invalid),"Nonfinite binding retained");
        var valid=Line(float3.zero,new float3(10,0,0));
        Check(!Binding(Entity.Null,Entity.Null,owner,owner,prefab,prefab,valid,valid,valid,valid),"Missing original retained");
        foreach(float v in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            var pose=new Transform{m_Position=new float3(v,0,0),m_Rotation=quaternion.identity};
            Check(!(bool)Call("DecalHoverPolicy","SamePose",pose,pose),"Invalid decal position accepted");
        }
        foreach(float scale in new[]{0f,.5f,2f,100f})
        {
            var pose=new Transform{m_Position=float3.zero,m_Rotation=quaternion.identity};pose.m_Rotation.value*=scale;
            Check(!(bool)Call("DecalHoverPolicy","SamePose",pose,pose),"Malformed rotation accepted");
        }
        for(int flags=0;flags<1024;flags++)foreach(byte kind in new byte[]{0,1,2})foreach(byte hidden in new byte[]{0,1,2,3})
        {
            bool active=(((TempFlags)flags)&(TempFlags.Delete|TempFlags.Cancel|TempFlags.Hidden))==0;
            bool expected=active&&kind!=0&&(kind&hidden)!=0;
            Check((bool)Call("DecalHoverPolicy","Hide",(TempFlags)flags,true,true,kind,hidden)==expected,"Temp mode/arrow/speed mask mismatch");
            Check(!(bool)Call("DecalHoverPolicy","Hide",(TempFlags)flags,false,true,kind,hidden),"Changed owner inherited hover suppression");
            Check(!(bool)Call("DecalHoverPolicy","Hide",(TempFlags)flags,true,false,kind,hidden),"Changed pose inherited hover suppression");
        }
        foreach(TempFlags flag in Enum.GetValues(typeof(TempFlags)))
        foreach(TempFlags combined in new[]{flag,flag|TempFlags.Delete,flag|TempFlags.Cancel,flag|TempFlags.Hidden})
        foreach(byte kind in new byte[]{1,2})
        {
            bool active=(combined&(TempFlags.Delete|TempFlags.Cancel|TempFlags.Hidden))==0;
            Check((bool)Call("DecalHoverPolicy","Hide",combined,true,true,kind,(byte)3)==active,"High-bit native preview flag bypassed cancellation/hiding rule");
        }
        for(int flags=0;flags<64;flags++)
        {
            bool alive=(flags&1)!=0,temp=(flags&2)!=0,want=(flags&4)!=0,hidden=(flags&8)!=0,owned=(flags&16)!=0,parent=(flags&32)!=0;
            int expected=!alive||!temp?0:want?(hidden?0:1):owned&&hidden&&!parent?-1:0;
            Check((int)Call("DecalHoverPolicy","VisibilityDelta",alive,temp,want,hidden,owned,parent)==expected,"Visibility ownership / apply / cancellation / parent protection mismatch");
        }
        // Repeated decisions against an already-matching flag request no new
        // writes. A real native refresh may clear the flag and require it again.
        // Cancellation releases only our temporary claim, not permanent/foreign state.
        int writes=0;bool currentHidden=false,ours=false;
        for(int i=0;i<10000;i++)
        {
            int delta=(int)Call("DecalHoverPolicy","VisibilityDelta",true,true,true,currentHidden,ours,false);
            if(delta==1){currentHidden=true;ours=true;writes++;}
        }
        Check(writes==1,"Identical hidden preview repeatedly writes flags");
        Check((int)Call("DecalHoverPolicy","VisibilityDelta",true,true,false,currentHidden,ours,false)==-1,"Cancel/apply/save fails to release owned hidden claim");
        Check((int)Call("DecalHoverPolicy","VisibilityDelta",true,true,false,true,false,false)==0,"Foreign hidden flag removed");
        Check((int)Call("DecalHoverPolicy","VisibilityDelta",true,false,false,true,true,false)==0,"Permanent actor mutated by cleanup");
        Console.WriteLine("PASS: "+count+" compiled unchanged-hover identity/pose/visibility decisions. Native ECS scheduling, renderer, saves and FPS not exercised.");
    }
}
