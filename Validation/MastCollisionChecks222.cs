using System;
using System.Reflection;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

class MastCollisionChecks222
{
    static int checks;
    static MethodInfo candidate, overlap;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static bool Candidate(Bounds3 a, Bounds3 b, float3 p, float3 q)
        => (bool)candidate.Invoke(null, new object[] { a, b, p, q });
    static bool Overlap(ObjectGeometryData geometry, Transform pose, Triangle2 triangle)
        => (bool)overlap.Invoke(null, new object[] { geometry, pose, triangle });
    static Bounds3 Bounds(float3 min, float3 max) => new Bounds3 { min = min, max = max };
    static Triangle2 Triangle(float2 a, float2 b, float2 c) => new Triangle2 { a = a, b = b, c = c };
    static float2 Move(float2 point, quaternion rotation, float3 position)
        => (math.rotate(rotation, new float3(point.x, 0, point.y)) + position).xz;

    static void Main()
    {
        var policy = Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.MastCollisionSnapshotPolicy", true);
        candidate = policy.GetMethod("Candidate", BindingFlags.Static | BindingFlags.NonPublic);
        overlap = policy.GetMethod("AreaOverlap", BindingFlags.Static | BindingFlags.NonPublic);
        var indices = policy.GetMethod("TriangleIndices", BindingFlags.Static | BindingFlags.NonPublic);
        Check((bool)indices.Invoke(null, new object[] { new int3(0, 1, 2), 3 }), "Valid triangle rejected");
        Check(!(bool)indices.Invoke(null, new object[] { new int3(0, 1, 3), 3 }), "Upper index boundary accepted");
        Check(!(bool)indices.Invoke(null, new object[] { new int3(-1, 1, 2), 3 }), "Negative triangle index accepted");
        Check(!(bool)indices.Invoke(null, new object[] { new int3(0), 0 }), "Empty triangle node buffer accepted");
        var small = Bounds(new float3(-.15f, 0, -.15f), new float3(.15f, 5, .15f));
        var large = Bounds(new float3(-100, 0, -100), new float3(100, 20, 100));
        Check(Candidate(small, large, float3.zero, new float3(90, 0, 90)), "Large intersecting geometry was excluded by origin distance");
        Check(Candidate(small, Bounds(new float3(-1, 100, -1), new float3(1, 110, 1)), float3.zero, new float3(0, 100, 0)), "2D ground-overlap candidate excluded by height");
        Check(Candidate(small, Bounds(new float3(10, 0, 0), new float3(11, 1, 1)), float3.zero, new float3(10, 0, 0)), "Nearby nonoverlap context missing");
        Check(!Candidate(small, Bounds(new float3(30, 0, 0), new float3(31, 1, 1)), float3.zero, new float3(30, 0, 0)), "Distant nonoverlap retained");
        Check(!Candidate(small, Bounds(new float3(float.NaN), new float3(1)), float3.zero, float3.zero), "NaN bounds accepted");
        Check(!Candidate(small, Bounds(new float3(1), new float3(-1)), float3.zero, float3.zero), "Inverted bounds accepted");
        Check(!Candidate(small, small, new float3(float.PositiveInfinity), float3.zero), "Invalid origin accepted");

        // Synthetic geometry: the width/height scale comes from the captured
        // assemblies, but these are NOT recovered runtime donor geometry.
        // This demonstrates a native route to test, not the user's crash or a
        // reproduction of the historical marker assignment.
        var original = new ObjectGeometryData { m_Bounds = small, m_Size = new float3(.3f, 5, .3f),
            m_Flags = Game.Objects.GeometryFlags.Standing | Game.Objects.GeometryFlags.CircularLeg | Game.Objects.GeometryFlags.Overridable,
            m_LegSize = new float3(.3f, 2.5f, .3f) };
        var custom = original;
        custom.m_Bounds = Bounds(new float3(-.28f, 0, -.28f), new float3(9.77f, 7.8f, .95f));
        custom.m_Size = Game.Objects.ObjectUtils.GetSize(custom.m_Bounds);
        for (int angle = 0; angle < 360; angle += 5)
            for (int offset = 0; offset < 5; offset++)
            {
                var rotation = quaternion.RotateY(math.radians(angle));
                var position = new float3(offset * 101.3f, offset * 2, -offset * 47.1f);
                var pose = new Transform(position, rotation);
                var triangle = Triangle(Move(new float2(7, -.1f), rotation, position),
                    Move(new float2(8, -.1f), rotation, position), Move(new float2(7.5f, .5f), rotation, position));
                Check(Overlap(custom, pose, triangle), "Native area footprint did not include the remote part");
                Check(!Overlap(original, pose, triangle), "Native donor control unexpectedly intersected remote area");
                var tallLeg = custom; tallLeg.m_LegSize.y = 7.8f;
                Check(Overlap(tallLeg, pose, triangle), "Area predicate incorrectly treated taller standing legs as a remedy");
                var clear = Triangle(Move(new float2(7, 3), rotation, position),
                    Move(new float2(8, 3), rotation, position), Move(new float2(7.5f, 4), rotation, position));
                Check(!Overlap(custom, pose, clear), "Disjoint area control intersected");
            }
        var identity = new Transform(float3.zero, quaternion.identity);
        var behind = Triangle(new float2(-8, 0), new float2(-7, 0), new float2(-7.5f, .5f));
        Check(!Overlap(custom, identity, behind), "Rectangular control expanded behind pole");
        custom.m_Flags |= Game.Objects.GeometryFlags.Circular;
        Check(Overlap(custom, identity, behind), "Circular donor effect was not distinguished");
        Console.WriteLine("PASS: " + checks + " offline broad-phase guards and native area-footprint scenarios. Synthetic geometry only; historical writer and live runtime inputs still require the diagnostic capture. No mast repair or FPS result is established.");
    }
}
