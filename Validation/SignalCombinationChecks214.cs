using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Objects;
using Unity.Mathematics;

class SignalCombinationChecks214
{
    static int checks, bindingCases, supportCases, evidenceCases;
    static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static void Check(bool ok, string label)
    {
        checks++;
        if (!ok) throw new Exception(label);
    }

    // Bit labels are assigned by the native/TTE initializer and carry no
    // geometric direction. Every fixture also runs under 32 bijective relabels.
    static ushort Relabel(ushort mask, int rotation, bool mirror)
    {
        int result = 0;
        for (int i = 0; i < 16; i++)
            if ((mask & (1 << i)) != 0)
                result |= 1 << (((mirror ? 15 - i : i) + rotation) & 15);
        return (ushort)result;
    }

    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var identity = assembly.GetType("MedianGrassReplacement.Systems.SignalPreviewIdentityPolicy", true);
        var binding = identity.GetMethod("SameFallbackBinding", Hidden);
        var nativePed = identity.GetMethod("NativePedestrianMask", Hidden);
        var score = identity.GetMethod("TrySecondaryScore", Hidden);
        var tie = identity.GetMethod("Tied", Hidden);
        var approachType = assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy+Approach", true);
        var sharing = assembly.GetType("MedianGrassReplacement.Systems.SignalSupportGroupPolicy", true).GetMethod("CanShare", Hidden);
        var garden = assembly.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy", true);
        var phaseApproach = garden.GetMethod("PhaseApproach", Hidden);
        var nearest = garden.GetMethod("NearestForPhase", Hidden);

        var bindings = new[] {
            // requested vehicle/pedestrian, source vehicle/pedestrian, accepted.
            new { Name="standalone crossing exact", V=0, P=1, SV=0, SP=1, Yes=true },
            new { Name="different standalone crossing", V=0, P=1, SV=0, SP=2, Yes=false },
            new { Name="protected turn exact", V=1, P=0, SV=1, SP=0, Yes=true },
            new { Name="protected versus through", V=1, P=0, SV=2, SP=0, Yes=false },
            new { Name="combined phase exact", V=3, P=4, SV=3, SP=4, Yes=true },
            new { Name="overlap loses movement", V=3, P=4, SV=1, SP=4, Yes=false },
            new { Name="overlap gains movement", V=1, P=4, SV=3, SP=4, Yes=false },
            new { Name="same vehicle different crossing", V=3, P=4, SV=3, SP=8, Yes=false },
            new { Name="known versus unknown vehicle", V=1, P=0, SV=0, SP=0, Yes=false },
            new { Name="unknown versus known vehicle", V=0, P=0, SV=1, SP=0, Yes=false },
            new { Name="known versus unknown crossing", V=0, P=1, SV=0, SP=0, Yes=false },
            new { Name="unknown versus known crossing", V=0, P=0, SV=0, SP=1, Yes=false },
            // Zero is not itself proof of identity: existing owner, role,
            // secondary/slot, unique native pose and claim guards still apply.
            new { Name="both zero still need identity evidence", V=0, P=0, SV=0, SP=0, Yes=true },
            new { Name="vehicle versus pedestrian role", V=1, P=0, SV=0, SP=1, Yes=false },
            new { Name="exclusive pedestrian phase", V=1, P=8, SV=1, SP=8, Yes=true },
            new { Name="pedestrian phase removed", V=1, P=0, SV=1, SP=8, Yes=false }
        };
        var supports = new[] {
            new { Name="protected plus through, separate lanes", L=new ushort[]{1,2}, P=new ushort[]{1,2}, Yes=true },
            new { Name="three independent heads", L=new ushort[]{1,2,4}, P=new ushort[]{1,2,4}, Yes=true },
            new { Name="one head, no sharing", L=new ushort[]{1}, P=new ushort[]{1}, Yes=false },
            new { Name="two heads same movement", L=new ushort[]{1,1}, P=new ushort[]{1,1}, Yes=false },
            new { Name="combined phase partly unmapped", L=new ushort[]{1,2}, P=new ushort[]{5,2}, Yes=false },
            new { Name="combined phase fully mapped", L=new ushort[]{1,4,2}, P=new ushort[]{5,2}, Yes=true },
            new { Name="shared turn lane needs separate layout", L=new ushort[]{3,2}, P=new ushort[]{1,2}, Yes=false },
            new { Name="overlapping phase heads", L=new ushort[]{1,2,4}, P=new ushort[]{3,6}, Yes=false },
            new { Name="uninitialized head", L=new ushort[]{1,2}, P=new ushort[]{0,2}, Yes=false },
            new { Name="uninitialized needed lane", L=new ushort[]{0,2}, P=new ushort[]{1,2}, Yes=false },
            new { Name="uncontrolled extra lane", L=new ushort[]{1,2,0}, P=new ushort[]{1,2}, Yes=true },
            new { Name="unmapped high phase", L=new ushort[]{1,2}, P=new ushort[]{32769,2}, Yes=false },
            new { Name="complete high phase", L=new ushort[]{32768,1,2}, P=new ushort[]{32769,2}, Yes=true },
            new { Name="two complete compound heads", L=new ushort[]{1,2,4,8}, P=new ushort[]{3,12}, Yes=true }
        };

        for (int rotation=0; rotation<16; rotation++)
        foreach (bool mirror in new[]{false,true})
        {
            ushort R(int value) => Relabel((ushort)value, rotation, mirror);
            foreach (var item in bindings)
            {
                var values = new object[]{R(item.V),R(item.P),R(item.SV),R(item.SP)};
                Check((bool)binding.Invoke(null,values)==item.Yes,item.Name); bindingCases++;
                // Reverse the two endpoints; association must be symmetric.
                Check((bool)binding.Invoke(null,new object[]{values[2],values[3],values[0],values[1]})==item.Yes,item.Name+" reverse"); bindingCases++;
            }
            foreach (var item in supports)
            foreach (bool reverseLanes in new[]{false,true})
            foreach (bool reverseHeads in new[]{false,true})
            {
                var lanes = new List<ushort>(); foreach (ushort value in item.L) lanes.Add(R(value));
                var heads = new List<ushort>(); foreach (ushort value in item.P) heads.Add(R(value));
                if(reverseLanes)lanes.Reverse(); if(reverseHeads)heads.Reverse();
                var approach = Activator.CreateInstance(approachType);
                var incoming = new List<float3>();
                for(int lane=0;lane<lanes.Count;lane++)incoming.Add(new float3(lane*3,0,0));
                approachType.GetField("Incoming",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(approach,incoming);
                approachType.GetField("LaneMasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(approach,lanes);
                Check((bool)sharing.Invoke(null,new object[]{approach,heads})==item.Yes,item.Name); supportCases++;
                // The single-head renderer must enforce the same completeness
                // boundary as support sharing, even when no sharing occurs.
                foreach(ushort phase in heads)
                {
                    object filtered=phaseApproach.Invoke(null,new object[]{approach,phase});
                    var kept=(List<float3>)approachType.GetField("Incoming",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(filtered);
                    bool complete=phase==0;
                    if(phase!=0)
                    {
                        complete=true;
                        for(int bit=0;bit<16;bit++)if((phase&(1<<bit))!=0)
                        {
                            bool witness=false;foreach(ushort lane in lanes)if((lane&(1<<bit))!=0)witness=true;
                            if(!witness)complete=false;
                        }
                    }
                    Check((kept.Count!=0)==complete,"partial/unknown phase rendered or complete phase lost: "+item.Name);
                }
            }
            // A custom composite broadens pedestrian mask 1 to 1|2. Native
            // fallback must still compare to 1; a later controller change to 4
            // must remain authoritative, and returning to native is harmless.
            Check((ushort)nativePed.Invoke(null,new object[]{R(3),R(1),R(3)})==R(1),"composite erased native crossing identity");
            Check((ushort)nativePed.Invoke(null,new object[]{R(4),R(1),R(3)})==R(4),"new timing-controller assignment ignored");
            Check((ushort)nativePed.Invoke(null,new object[]{R(1),R(1),R(3)})==R(1),"native restoration changed binding");
        }
        var missing = Activator.CreateInstance(approachType);
        Check(!(bool)sharing.Invoke(null,new object[]{missing,new List<ushort>{1,2}}),"missing lane evidence accepted");
        var mismatched = Activator.CreateInstance(approachType);
        approachType.GetField("Incoming",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mismatched,new List<float3>{float3.zero});
        approachType.GetField("LaneMasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mismatched,new List<ushort>{1,2});
        Check(!(bool)sharing.Invoke(null,new object[]{mismatched,new List<ushort>{1,2}}),"stale lane/mask snapshot accepted");
        foreach(var unknown in new[]{missing,mismatched})
        {
            object filtered=phaseApproach.Invoke(null,new object[]{unknown,(ushort)1});
            Check(((List<float3>)approachType.GetField("Incoming",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(filtered)).Count==0,"missing phase evidence borrowed whole road");
        }
        // A nearby mouth with one matching bit must not displace a fully mapped
        // farther mouth. Use the actual compiled phase-based approach selector.
        var mouths=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approachType));
        for(int i=0;i<2;i++)
        {
            var road=Activator.CreateInstance(approachType);
            var fieldFlags=BindingFlags.Instance|BindingFlags.NonPublic;
            approachType.GetField("Left",fieldFlags).SetValue(road,new float3(i*5,0,-1));
            approachType.GetField("Right",fieldFlags).SetValue(road,new float3(i*5,0,1));
            approachType.GetField("Incoming",fieldFlags).SetValue(road,new List<float3>{new float3(i*5,0,0)});
            approachType.GetField("LaneMasks",fieldFlags).SetValue(road,new List<ushort>{(ushort)(i==0?1:5)});
            approachType.GetField("VehicleMask",fieldFlags).SetValue(road,(ushort)(i==0?1:5));
            mouths.Add(road);
        }
        var probe=new Transform{m_Position=float3.zero,m_Rotation=quaternion.identity};
        Check((int)nearest.Invoke(null,new object[]{probe,mouths,(ushort)5})==1,"partial nearby approach stole the complete phase");
        Check((int)nearest.Invoke(null,new object[]{probe,mouths,(ushort)8})==-1,"unmapped controller chose another road");
        Check((int)nearest.Invoke(null,new object[]{probe,mouths,(ushort)0})==0,"mask-free native geometric lookup changed");

        var origin = new Transform{m_Position=float3.zero,m_Rotation=quaternion.identity};
        foreach(float x in new[]{0f,1f,11.9f,12f,100f,float.NaN,float.PositiveInfinity})
        {
            var actor = origin; actor.m_Position.x=x;
            object[] args={actor,origin,0f};
            Check((bool)score.Invoke(null,args)==(x>=0 && x<12),"secondary source distance must stay finite/bounded"); evidenceCases++;
        }
        Check((bool)tie.Invoke(null,new object[]{1f,1f}),"equal-distance ambiguity accepted"); evidenceCases++;
        Console.WriteLine("PASS: "+checks+" compiled combination decisions ("+bindingCases+" dual-role bindings, "+supportCases+" support layouts, "+evidenceCases+" bounded-pose/ambiguity checks). Policies only: native tools, TTE phase initialization, medians/LHT geometry, renderer, saves and FPS still need runtime validation.");
    }
}
