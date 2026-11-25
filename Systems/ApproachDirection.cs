using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class ApproachDirection
    {
        internal const float MinimumAlignment = .85f;
        internal const float TieTolerance = .001f;

        internal static float Alignment(float3 direction, float3 approach)
            => math.dot(math.normalizesafe(direction.xz), math.normalizesafe(approach.xz));

        internal static bool Opposite(float3 first, float3 second) => Alignment(first, second) < -MinimumAlignment;

        internal struct Match
        {
            private float _best, _runnerUp;
            private Entity _choice;

            internal void Consider(Entity road, float score)
            {
                if (!(score > MinimumAlignment)) return;
                if (score > _best)
                {
                    _runnerUp = _best;
                    _best = score;
                    _choice = road;
                }
                else if (score > _runnerUp) _runnerUp = score;
            }

            internal Entity Result => _best - _runnerUp > TieTolerance ? _choice : Entity.Null;
        }
    }
}
