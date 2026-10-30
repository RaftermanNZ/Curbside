using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal enum NodePaintMovement { Unknown, Through, LeftTurn, RightTurn, UTurn }

    // Classifies paint connectors only. Native vehicle lanes and turn restrictions are untouched.
    internal static class NodeMovementPolicy
    {
        internal static NodePaintMovement Classify(bool sameApproach, float2 outwardFrom, float2 outwardTo)
        {
            if (!math.all(math.isfinite(outwardFrom)) || !math.all(math.isfinite(outwardTo))
                || math.lengthsq(outwardFrom) < .01f || math.lengthsq(outwardTo) < .01f)
                return NodePaintMovement.Unknown;
            if (sameApproach) return NodePaintMovement.UTurn;
            var incoming = -math.normalizesafe(outwardFrom);
            var outgoing = math.normalizesafe(outwardTo);
            float dot = math.dot(incoming, outgoing);
            if (dot >= .7071067f) return NodePaintMovement.Through;
            if (dot <= -.7071067f) return NodePaintMovement.Unknown; // Ambiguous parallel approaches.
            return incoming.x * outgoing.y - incoming.y * outgoing.x > 0
                ? NodePaintMovement.LeftTurn : NodePaintMovement.RightTurn;
        }
    }
}
