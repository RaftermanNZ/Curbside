using System;

using Colossal.Mathematics;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal struct MarkingKey : IEquatable<MarkingKey>

    {

        private int3 a, b, c, d;

        private Entity prefab;

        internal MarkingKey(Bezier4x3 curve, Entity definition) { a = (int3)math.round(curve.a * 1000f); b = (int3)math.round(curve.b * 1000f); c = (int3)math.round(curve.c * 1000f); d = (int3)math.round(curve.d * 1000f); prefab = definition; }

        public bool Equals(MarkingKey other) => a.Equals(other.a) && b.Equals(other.b) && c.Equals(other.c) && d.Equals(other.d) && prefab == other.prefab;

        public override bool Equals(object value) => value is MarkingKey key && Equals(key);

        public override int GetHashCode() { unchecked { return ((((a.GetHashCode() * 31 + b.GetHashCode()) * 31 + c.GetHashCode()) * 31 + d.GetHashCode()) * 31 + prefab.GetHashCode()); } }

    }

}
