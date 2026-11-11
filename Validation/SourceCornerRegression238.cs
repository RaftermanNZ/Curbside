using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Web.Script.Serialization;
using Unity.Mathematics;
using Unity.Entities;
using Curbside.CornerStudy;
using Transform = Game.Objects.Transform;

class SourceCornerRegression238
{
    const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Assembly Mod = Assembly.LoadFrom("MedianGrassReplacement.dll");
    static readonly Type Road = Mod.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy").GetNestedType("Approach", BindingFlags.NonPublic);
    static readonly MethodInfo Legacy = Mod.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy").GetMethod("NearestForPhase", Static);
    static readonly MethodInfo Association = Mod.GetType("MedianGrassReplacement.Systems.SignalSourceAssociationPolicy")?.GetMethod("Find", Static);
    static int checks, fixedCases;
    static bool baseline;
    static void Check(bool value, string why)
    {
        checks++;
        if (!value) throw new Exception(why);
    }
    static object Mouth(float depth = 0, ushort mask = 1)
    {
        var road = Activator.CreateInstance(Road);
        void Set(string key, object value) => Road.GetField(key, Fields).SetValue(road, value);
        Set("Left", new float3(-20, 0, depth));
        Set("Right", new float3(20, 0, depth));
        Set("Outward", new float2(0, 1));
        Set("Incoming", new List<float3> { new float3(2, 0, depth) });
        Set("LaneMasks", new List<ushort> { mask });
        Set("VehicleMask", mask);
        return road;
    }
    static IList Roads(params object[] roads)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Road));
        foreach (var road in roads) list.Add(road);
        return list;
    }
    static int Find(float3 position, IList roads, ushort mask)
    {
        var source = new Transform { m_Position = position, m_Rotation = quaternion.identity };
        return (int)(Association ?? Legacy).Invoke(null, new object[] { source, roads, mask });
    }
    static void Main(string[] args)
    {
        baseline = Array.IndexOf(args, "--baseline") >= 0;
        Check(baseline || Association != null, "New committed source policy missing");
        var road = Mouth();
        foreach (float distance in new[] { .5f, 11.9f, 12f, 12.1f, 20f })
        {
            bool gap = distance >= 12;
            int actual = Find(new float3(-20 + distance, 0, 0), Roads(road), 1);
            Check(actual == (baseline && gap ? -1 : 0), "Native median source lost at " + distance);
            if (gap) fixedCases++;
        }
        if (!baseline)
        {
            Check(Find(new float3(0, 0, 0), Roads(road), 0) == -1, "Unmapped zero phase guessed");
            Check(Find(new float3(0, 0, 0), Roads(road), 3) == -1, "Partial phase accepted");
            Check(Find(new float3(0, 2.01f, 0), Roads(road), 1) == -1, "Other elevation accepted");
            Check(Find(new float3(0, 0, 3.01f), Roads(road), 1) == -1, "Unrelated longitudinal position accepted");
            Check(Find(new float3(0, 0, 0), Roads(road, Mouth(.25f)), 1) == -1, "Ambiguous road mouths accepted");
            Check(Find(new float3(0, 0, 0), Roads(Mouth(20, 2), road), 1) == 1, "Unique phase/road reordered incorrectly");
            var bad = Mouth();
            Road.GetField("Outward", Fields).SetValue(bad, float2.zero);
            Check(Find(float3.zero, Roads(bad), 1) == -1, "Zero native road direction accepted");
            bad = Mouth();
            Road.GetField("LaneMasks", Fields).SetValue(bad, new List<ushort> { 1, 2 });
            Check(Find(float3.zero, Roads(bad), 1) == -1, "Incomplete lane metadata accepted");
            Check(Find(new float3(float.NaN, 0, 0), Roads(road), 1) == -1, "Non-finite source accepted");
        }
        // Existing ordinary bank choices must remain byte-for-byte equivalent
        // to the old association, including unchanged zero-phase behavior.
        for (int i = 0; i < 100; i++)
        {
            var point = new float3(-19.5f + i * .01f, 0, i * .01f);
            var list = Roads(road, Mouth(30, 2));
            var pose = new Transform { m_Position = point, m_Rotation = quaternion.identity };
            int before = (int)Legacy.Invoke(null, new object[] { pose, list, (ushort)1 });
            Check(Find(point, list, 1) == before, "Existing near-curb association changed");
        }
        var helper = typeof(CornerRuleChecks235);
        var tee = (List<CurbSlice>)helper.GetMethod("Tee", Static).Invoke(null, new object[] { 3.5f, BoundaryKind.Curb });
        var t = CornerRule.Calculate(tee, .724f);
        Check(t.Count == 4 && t.TrueForAll(p => p.Fits), "Existing true-T layout changed");
        int retained = 0;
        for (int legs = 3; legs <= 6; legs++)
        for (int pass = 0; pass < 20; pass++)
        foreach (var kind in new[] { BoundaryKind.Curb, BoundaryKind.TrafficLaneEdge })
        {
            var input = (List<CurbSlice>)helper.GetMethod("Radial", Static).Invoke(null, new object[] { legs, pass, kind });
            // Native Outside vectors can be almost parallel although the actual
            // corner strip curves. The old classifier discarded these pairs.
            for (int i = 0; i < input.Count; i += 2)
            {
                var first = input[i]; var second = input[i + 1];
                var outward = math.normalizesafe(first.Curb.d);
                first.Outside = outward;
                second.Outside = outward;
                input[i] = first; input[i + 1] = second;
            }
            var corners = CornerRule.Calculate(input, .724f);
            Check(corners.Count == (baseline ? 0 : legs), "Nearly parallel normals discarded/duplicated a curved corner, legs=" + legs);
            foreach (var corner in corners)
            {
                Check(corner.Fits, "Recovered corner escaped its native strip");
                Check(math.abs(math.distance(corner.Position.xz, corner.CurbPoint.xz) - 1.5f) < .02f, "1.5m setback changed");
                retained++;
            }
            if (!baseline)
            {
                var missing = input.ToArray();
                for (int i = 0; i < missing.Length; i++) missing[i].HasBack = false;
                Check(CornerRule.Calculate(missing, .724f).TrueForAll(p => !p.Fits), "Missing supporting boundary bypassed");
            }
        }
        var report = new { baseline, checks, sourceGapCases = fixedCases, retainedCurvedCornerProposals = retained,
            scope = "Actual compiled source association and corner rule; 160 constructed curved-strip cases, true T, road/shoulder boundaries, phase ambiguity and invalid metadata. No native World, actual road prefab generation, renderer, save or FPS." };
        File.WriteAllText("Source-Corner-Regression238.json", new JavaScriptSerializer().Serialize(report));
        Console.WriteLine((baseline ? "BASELINE REPRODUCED: " : "PASS: ") + checks + " checks; retained curved corner proposals=" + retained + "; true-T behavior preserved.");
    }
}
