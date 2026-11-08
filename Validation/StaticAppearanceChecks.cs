using System;
using System.Linq;
using System.Reflection;
using Colossal.Mathematics;
using Game.Net;
using LaneFlags = Game.Prefabs.LaneFlags;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

class StaticAppearanceChecks
{
    static int checks;
    const BindingFlags Methods = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; }
    static Bezier4x3 Line(float3 a, float3 b) => new Bezier4x3(a, math.lerp(a,b,1f/3), math.lerp(a,b,2f/3), b);
    static void Main()
    {
        var mod = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var policy = mod.GetType("MedianGrassReplacement.Systems.CrosswalkPreviewChoicePolicy", true);
        var appearance = policy.GetMethod("Appearance", Methods);
        byte Choice(bool intent, byte saved, bool hidden, bool style, bool lined)
            => (byte)appearance.Invoke(null, new object[] { intent, saved, hidden, style, lined });
        foreach (byte saved in new byte[]{0,1,2})
            foreach (bool hidden in new[]{false,true})
                foreach (bool style in new[]{false,true})
                    foreach (bool lined in new[]{false,true})
                        Check(Choice(true,saved,hidden,style,lined)==saved,"Saved approach intent lost to older road defaults");
        Check(Choice(false,0,false,false,false)==0,"Uncustomized road lost vanilla crossing");
        Check(Choice(false,0,false,true,false)==0,"Explicit native road end was painted");
        Check(Choice(false,0,false,true,true)==1,"Saved lined road end lost its paint");
        Check(Choice(false,0,true,true,true)==2,"Hidden crossing regained visible paint");

        var score = mod.GetType("MedianGrassReplacement.Systems.CrosswalkApproachPlacement",true).GetMethod("TryScore",Methods);
        var marking = mod.GetType("MedianGrassReplacement.Systems.MarkingKey",true);
        var keyCtor = marking.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(c=>c.GetParameters().Length==2);
        var prefab = new Entity{Index=15,Version=1};
        for (int angle=0;angle<360;angle+=15)
            foreach (var shift in new[]{float3.zero,new float3(1500,25,-800)})
            {
                var rotation=quaternion.RotateY(math.radians(angle));
                float3 P(float x,float z)=>shift+math.mul(rotation,new float3(x,0,z));
                var segment=new Segment{m_Left=Line(P(-5,0),P(-5,30)),m_Right=Line(P(5,0),P(5,30))};
                var geometry=new EdgeGeometry{m_Start=segment,m_End=segment};
                var original=Line(P(-5,0),P(5,0));
                var refreshed=Line(P(-5,2),P(5,2));
                var originalKey=keyCtor.Invoke(new object[]{original,prefab});
                var refreshedKey=keyCtor.Invoke(new object[]{refreshed,prefab});
                Check(!originalKey.Equals(refreshedKey),"Fixture no longer reproduces exact-curve identity loss");
                object[] args={geometry,true,refreshed,0f};
                Check((bool)score.Invoke(null,args),"Rebuilt crossing could not bind to the connected preview road end");
                Check(math.abs((float)args[3]-2)<.01f,"Crossing scored against wrong road end");
                Check(Choice(true,1,false,false,false)==1,"Rebuilt lane without original identity lost saved lined appearance");
                Check(Choice(true,2,false,false,false)==2,"Rebuilt lane without original identity lost hidden appearance");
                args=new object[]{geometry,true,Line(P(0,0),P(0,10)),0f};
                Check(!(bool)score.Invoke(null,args),"Longitudinal pedestrian lane matched a crosswalk mouth");
                args=new object[]{geometry,true,Line(P(-5,0)+new float3(0,10,0),P(5,0)+new float3(0,10,0)),0f};
                Check(!(bool)score.Invoke(null,args),"Different elevation borrowed the crossing choice");
            }
        var tied=policy.GetMethod("Tied",Methods);
        Check((bool)tied.Invoke(null,new object[]{1f,1.005f}),"Ambiguous approach tie was accepted");
        Check(!(bool)tied.Invoke(null,new object[]{1f,1.5f}),"Distinct approaches were treated as tied");

        // Validate the reason for the native-key restoration against the actual
        // installed game's private value type, without a Unity world or game run.
        var laneKey=typeof(Lane).Assembly.GetType("Game.Net.LaneSystem+LaneKey",true);
        var laneCtor=laneKey.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single();
        var alias=new Entity{Index=16,Version=1};
        object Key(Entity pf)=>laneCtor.Invoke(new object[]{default(Lane),pf,default(LaneFlags)});
        Check(!Key(prefab).Equals(Key(alias)),"Native lane matching no longer includes prefab identity");
        Check(Key(prefab).Equals(Key(prefab)),"Restoring original definition did not restore the native key");

        var place=mod.GetType("MedianGrassReplacement.Systems.PreviewVisualPolicy",true).GetMethod("Place",Methods);
        var native=new Transform{m_Position=new float3(2,0,3),m_Rotation=quaternion.identity};
        var applied=new Transform{m_Position=new float3(8,1,12),m_Rotation=quaternion.RotateY(.7f)};
        for(int i=0;i<1000;i++)
        {
            var cursor=new Transform{m_Position=new float3(i,10,-i),m_Rotation=quaternion.RotateY(i*.1f)};
            object[] args={native,applied,cursor,default(Transform)};
            Check((bool)place.Invoke(null,args)&&((Transform)args[3]).Equals(applied),"Cursor/proposed road moved a placed light");
        }
        Console.WriteLine("PASS: "+checks+" static appearance checks; reproduced changed-curve identity loss and native prefab-key mismatch. Live flicker and FPS require in-game testing.");
    }
}
