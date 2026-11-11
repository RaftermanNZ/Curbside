using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;

class RefreshInterestChecks218
{
    static void Main()
    {
        var assembly = Assembly.LoadFrom("MedianGrassReplacement.dll");
        var type = assembly.GetType("MedianGrassReplacement.Systems.PreviewOwnerIndex", true);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var track = type.GetMethod("Track", flags);
        var remove = type.GetMethod("Remove", flags);
        var select = type.GetMethod("Select", flags);
        var maySelect = type.GetMethod("MaySelect", flags);
        var clear = type.GetMethod("Clear", flags);
        int checks = 0;
        Entity E(int id, int version = 1) => new Entity { Index = id, Version = version };
        for (int seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var index = Activator.CreateInstance(type, true);
            for (int step = 0; step < 80; step++)
            {
                var actor = E(1 + random.Next(30), 1 + random.Next(2));
                if (step % 7 == 0) remove.Invoke(index, new object[] { actor });
                else track.Invoke(index, new object[] { actor, E(50 + random.Next(10)), E(60 + random.Next(10)), Entity.Null, Entity.Null });
                if (step % 31 == 0) clear.Invoke(index, null);
                var keys = new List<Entity>();
                for (int id = 1; id < 85; id++) { keys.Add(E(id)); keys.Add(E(id, 2)); }
                keys.Add(Entity.Null);
                var filtered = keys.Where(key => (bool)maySelect.Invoke(index, new object[] { key })).ToList();
                var original = new List<Entity>(); var result = new List<Entity>();
                select.Invoke(index, new object[] { keys, original });
                select.Invoke(index, new object[] { filtered, result });
                checks++;
                if (!new HashSet<Entity>(original).SetEquals(result)) throw new Exception("Interest filtering lost selected actors: " + seed + "/" + step);
                foreach (var key in keys)
                {
                    var one = new List<Entity>();
                    select.Invoke(index, new object[] { new[] { key }, one });
                    checks++;
                    if ((bool)maySelect.Invoke(index, new object[] { key }) != (one.Count != 0)) throw new Exception("Interest disagrees with compiled Select");
                }
            }
        }
        Console.WriteLine("PASS: " + checks + " compiled managed-index checks; actor/source keys, reparenting, deletion, empty indexes and entity versions. No native World/jobs or FPS exercised.");
    }
}
