using System;
using System.Reflection;
using Game.Objects;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

class NativeSignalPreviewChecks
{
    static int checks;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
    static void Main()
    {
        var mod=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var policy=mod.GetType("MedianGrassReplacement.Systems.SignalPreviewIdentityPolicy",true);
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var link=policy.GetMethod("WithOriginal",flags);
        var slot=policy.GetMethod("SameSlot",flags);
        var copy=policy.GetMethod("CopiesOriginalPose",flags);
        var custom=policy.GetMethod("Customized",flags);
        Temp Link(Temp t,Entity source)=>(Temp)link.Invoke(null,new object[]{t,source});
        bool Copy(bool isStatic,bool original,TempFlags f)=>(bool)copy.Invoke(null,new object[]{isStatic,original,f});
        var source=new Entity{Index=42,Version=3};
        var nativePrefab=new Entity{Index=8,Version=1};var styledPrefab=new Entity{Index=9,Version=1};
        var nativePose=new Transform{m_Position=new float3(2,0,3),m_Rotation=quaternion.identity};
        var placed=new Transform{m_Position=new float3(17,1,-12),m_Rotation=quaternion.RotateY(.4f)};
        Check((bool)custom.Invoke(null,new object[]{nativePrefab,nativePrefab,nativePose,placed}),"Position-only vanilla-model upgrade was excluded");
        Check((bool)custom.Invoke(null,new object[]{nativePrefab,styledPrefab,nativePose,placed}),"Custom signal style was excluded");
        Check(!(bool)custom.Invoke(null,new object[]{nativePrefab,nativePrefab,nativePose,nativePose}),"Unchanged vanilla signal was claimed");
        for(int i=0;i<1000;i++)
        {
            var before=new Temp{m_Original=Entity.Null,m_Flags=(TempFlags)(i&255),m_Cost=100+i,m_Value=-i,m_CurvePosition=i*.03125f};
            var linked=Link(before,source);
            Check(linked.m_Original==source,"Original identity not bridged");
            Check(linked.m_Flags==before.m_Flags&&linked.m_Cost==before.m_Cost&&linked.m_Value==before.m_Value&&linked.m_CurvePosition==before.m_CurvePosition,"Link changed tool validity/cost/curve data");
            // Native/other-mod updates after linking must survive cancellation,
            // save or apply cleanup: restore the field, not a stale Temp snapshot.
            linked.m_Cost+=7;linked.m_Value-=3;linked.m_CurvePosition+=.125f;linked.m_Flags|=TempFlags.Upgrade;
            var restored=Link(linked,before.m_Original);
            Check(restored.m_Original==before.m_Original&&restored.m_Flags==linked.m_Flags&&restored.m_Cost==linked.m_Cost
                &&restored.m_Value==linked.m_Value&&restored.m_CurvePosition==linked.m_CurvePosition,"Cleanup overwrote newer tool state");
        }
        for(int i=0;i<512;i++)
            foreach(bool isStatic in new[]{false,true})
                foreach(bool original in new[]{false,true})
                {
                    var f=(TempFlags)i;
                    bool expected=original&&(f&TempFlags.Dragging)==0&&(!isStatic||(f&(TempFlags.Create|TempFlags.Modify))==0);
                    Check(Copy(isStatic,original,f)==expected,"Policy differs from installed native renderer branch");
                }
        for(int i=0;i<256;i++)
        {
            Check((bool)slot.Invoke(null,new object[]{i,i}),"Matching aligned object slot rejected");
            Check(!(bool)slot.Invoke(null,new object[]{i,i+1}),"Different slot borrowed another light's identity");
        }
        Check(!(bool)slot.Invoke(null,new object[]{-1,-1}),"Unaligned/unknown objects were guessed");
        int nativeCopies=0;
        for(int i=0;i<600;i++)
        {
            var parent=new Transform{m_Position=new float3(i*.2f,0,-i*.3f),m_Rotation=quaternion.RotateY(i*.01f)};
            var proposed=ObjectUtils.LocalToWorld(parent,nativePose);
            Check(!proposed.Equals(placed),"Fixture lost cursor-driven displacement");
            var preview=new Temp{m_Original=Entity.Null,m_Flags=TempFlags.Select};
            Check(!Copy(true,false,preview.m_Flags),"Missing identity unexpectedly used committed pose");
            preview=Link(preview,source);
            var nativeResult=Copy(true,preview.m_Original!=Entity.Null,preview.m_Flags)?placed:proposed;
            Check(nativeResult.Equals(placed),"Native unchanged-preview branch did not retain committed position");
            nativeCopies++;
        }
        Check(!Copy(true,true,TempFlags.Modify),"Actual modification was forced into unchanged-preview rules");
        Check(!Copy(true,true,TempFlags.Create),"New signal creation flags were ignored");
        Check(!Copy(true,true,TempFlags.Dragging),"Dragging flags were ignored");
        Console.WriteLine("PASS: "+checks+" native signal preview decisions; "+nativeCopies+" native-pose fixtures; 1,000 link/cleanup rounds preserve tool flags/cost/value/curve state. Native ECS visuals, commit/save and FPS remain unverified.");
    }
}
