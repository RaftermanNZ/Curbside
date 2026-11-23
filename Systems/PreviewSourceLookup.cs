using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    // Rebuilt inside the existing native-change batch. Exact owner + exact
    // millimetre-quantized marking key, followed by the existing 1 mm geometric
    // equivalence across cell boundaries. Duplicate matches remain ambiguous.
    internal sealed class PreviewSourceLookup
    {
        private struct Key : IEquatable<Key>
        {
            internal Entity Owner;
            internal MarkingKey Marking;
            public bool Equals(Key other) => Owner == other.Owner && Marking.Equals(other.Marking);
            public override bool Equals(object value) => value is Key key && Equals(key);
            public override int GetHashCode() => unchecked(Owner.GetHashCode() * 397 ^ Marking.GetHashCode());
        }

        private readonly HashSet<Entity> complete = new HashSet<Entity>();
        private readonly Dictionary<Key, Entity> candidates = new Dictionary<Key, Entity>();
        private struct Role : IEquatable<Role>
        {
            internal Entity Owner, Prefab;
            public bool Equals(Role other) => Owner == other.Owner && Prefab == other.Prefab;
            public override bool Equals(object value) => value is Role role && Equals(role);
            public override int GetHashCode() => unchecked(Owner.GetHashCode() * 397 ^ Prefab.GetHashCode());
        }
        private struct Candidate { internal Entity Actor; internal Bezier4x3 Curve; }
        private readonly NativePreviewSpatialIndex<Role, Candidate> nearby = new NativePreviewSpatialIndex<Role, Candidate>(.002);
        private readonly List<Candidate> scratch = new List<Candidate>();

        internal void Clear() { complete.Clear(); candidates.Clear(); nearby.Clear(); scratch.Clear(); }
        internal bool HasOwner(Entity owner) => complete.Contains(owner);
        internal void CompleteOwner(Entity owner) => complete.Add(owner);

        internal void Add(Entity owner, Entity actor, Entity prefab, Bezier4x3 curve)
        {
            if (!Valid(curve)) return;
            var key = new Key { Owner = owner, Marking = new MarkingKey(curve, prefab) };
            // Even repeated membership of the same actor is ambiguous in the
            // existing linear matcher. Do not silently choose its first copy.
            if (candidates.ContainsKey(key)) candidates[key] = Entity.Null;
            else candidates.Add(key, actor);
            nearby.Add(new Role { Owner = owner, Prefab = prefab }, curve.a, new Candidate { Actor = actor, Curve = curve });
        }

        internal Entity Resolve(Entity owner, Bezier4x3 curve, Entity prefab)
        {
            if (!Valid(curve)) return Entity.Null;
            if (candidates.TryGetValue(new Key { Owner = owner, Marking = new MarkingKey(curve, prefab) }, out var actor)) return actor;
            if (!nearby.Query(new Role { Owner = owner, Prefab = prefab }, curve.a, scratch)) return Entity.Null;
            Entity match = Entity.Null;
            foreach (var item in scratch)
            {
                var other = item.Curve;
                if (math.distancesq(curve.a, other.a) > .000001f || math.distancesq(curve.b, other.b) > .000001f
                    || math.distancesq(curve.c, other.c) > .000001f || math.distancesq(curve.d, other.d) > .000001f) continue;
                if (match != Entity.Null) return Entity.Null;
                match = item.Actor;
            }
            return match;
        }
        private static bool Valid(Bezier4x3 curve) => math.all(math.isfinite(curve.a)) && math.all(math.isfinite(curve.b))
            && math.all(math.isfinite(curve.c)) && math.all(math.isfinite(curve.d));
    }
}
