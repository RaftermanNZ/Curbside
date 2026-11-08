using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class MarkingPreviewPolicy
    {
        internal static bool Overlaps(Bezier4x3 paint, Bezier4x3 guide)
        {
            // A Bezier stays inside its control-point bounds. Most paint/guide
            // pairs can be rejected without doing any closest-point searches.
            if (Separated(paint.a.x, paint.b.x, paint.c.x, paint.d.x, guide.a.x, guide.b.x, guide.c.x, guide.d.x)
                || Separated(paint.a.y, paint.b.y, paint.c.y, paint.d.y, guide.a.y, guide.b.y, guide.c.y, guide.d.y)
                || Separated(paint.a.z, paint.b.z, paint.c.z, paint.d.z, guide.a.z, guide.b.z, guide.c.z, guide.d.z)) return false;
            // Check both ways so a dashed preview also hides a long solid line.
            return Follows(paint, guide) || Follows(guide, paint);
        }

        private static bool Separated(float a, float b, float c, float d, float e, float f, float g, float h)
        {
            float firstMin = math.min(math.min(a, b), math.min(c, d));
            float firstMax = math.max(math.max(a, b), math.max(c, d));
            float secondMin = math.min(math.min(e, f), math.min(g, h));
            float secondMax = math.max(math.max(e, f), math.max(g, h));
            return firstMin > secondMax + .85f || secondMin > firstMax + .85f;
        }

        internal static bool SameCurve(Bezier4x3 a, Bezier4x3 b)
            => a.a.Equals(b.a) && a.b.Equals(b.b) && a.c.Equals(b.c) && a.d.Equals(b.d);

        private static bool Follows(Bezier4x3 source, Bezier4x3 target)
        {
            for (int i = 1; i <= 3; i++)
            {
                float t = i * .25f;
                float3 point = MathUtils.Position(source, t);
                if (MathUtils.Distance(target, point, out float nearest) > .85f) continue;
                float2 a = math.normalizesafe(MathUtils.Tangent(source, t).xz);
                float2 b = math.normalizesafe(MathUtils.Tangent(target, nearest).xz);
                if (math.abs(math.dot(a, b)) > .9f) return true;
            }
            return false;
        }
    }

    // Tool systems request suppression. Apply it after paint producers finish,
    // then restore before the next modification pass or save preparation.
    public partial class MarkingPreviewSystem : GameSystemBase
    {
        private struct Guide
        {
            internal Entity Owner;
            internal Bezier4x3 Curve;
        }

        private struct Match
        {
            internal Entity Owner;
            internal Bezier4x3 Curve;
            internal bool Overlaps;
        }

        private readonly List<Guide> requests = new List<Guide>();
        private readonly List<Guide> previous = new List<Guide>();
        private Dictionary<Entity, Match> matches = new Dictionary<Entity, Match>();
        private Dictionary<Entity, Match> nextMatches = new Dictionary<Entity, Match>();
        private readonly HashSet<Entity> owners = new HashSet<Entity>();
        private readonly HashSet<Entity> hidden = new HashSet<Entity>();
        private readonly HashSet<Entity> candidates = new HashSet<Entity>();

        internal void Request(Entity owner, Bezier4x3 guide)
        {
            requests.Add(new Guide { Owner = owner, Curve = guide });
        }

        // Keep geometry matches between frames. Hidden is still restored before
        // tools and network updates so producers never mistake it for an upgrade.
        internal void BeginFrame()
        {
            requests.Clear();
            RestoreHidden();
        }

        internal void Restore()
        {
            BeginFrame();
            previous.Clear();
            matches.Clear();
            nextMatches.Clear();
        }

        private void RestoreHidden()
        {
            if (hidden.Count == 0) return;
            long started = ModDiagnostics.RuntimeCosts.Begin();
            try
            {
                using (var commands = new EntityCommandBuffer(Allocator.Temp))
                {
                    bool changed = false;
                    foreach (var entity in hidden)
                    {
                        if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity) || !EntityManager.HasComponent<Hidden>(entity)) continue;
                        commands.RemoveComponent<Hidden>(entity);
                        if (!EntityManager.HasComponent<BatchesUpdated>(entity)) commands.AddComponent<BatchesUpdated>(entity);
                        changed = true;
                    }
                    if (changed)
                    {
                        NativeVisualInvalidation.CompleteReaders(World);
                        commands.Playback(EntityManager);
                    }
                }
                hidden.Clear();
            }
            finally { ModDiagnostics.RuntimeCosts.End("MarkingPreviewRestore", started); }
        }

        internal bool Matches(Entity owner, Entity entity, Bezier4x3 curve)
        {
            if (matches.TryGetValue(entity, out var match) && match.Owner == owner && MarkingPreviewPolicy.SameCurve(match.Curve, curve))
            {
                nextMatches[entity] = match;
                return match.Overlaps;
            }
            bool overlaps = false;
            foreach (var guide in requests)
            {
                if (guide.Owner != owner || !MarkingPreviewPolicy.Overlaps(curve, guide.Curve)) continue;
                overlaps = true;
                break;
            }
            nextMatches[entity] = new Match { Owner = owner, Curve = curve, Overlaps = overlaps };
            return overlaps;
        }

        private void PrepareMatches()
        {
            bool same = requests.Count == previous.Count;
            for (int i = 0; same && i < requests.Count; i++)
                same = requests[i].Owner == previous[i].Owner && MarkingPreviewPolicy.SameCurve(requests[i].Curve, previous[i].Curve);
            if (!same)
            {
                matches.Clear();
                previous.Clear();
                previous.AddRange(requests);
            }
            nextMatches.Clear();
        }

        protected override void OnUpdate()
        {
            long started = ModDiagnostics.RuntimeCosts.Begin();
            try { UpdatePreview(); }
            finally { ModDiagnostics.RuntimeCosts.End("MarkingPreviewSystem", started); }
        }

        private void UpdatePreview()
        {
            if (Mod.Saving || UpgradeInputPolicy.PointerBlocked || requests.Count == 0) { Restore(); return; }
            PrepareMatches();
            Dependency.Complete();
            candidates.Clear();
            owners.Clear();
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var emission = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            var replacements = World.GetOrCreateSystemManaged<IntersectionPaintSystem>();
            foreach (var request in requests)
            {
                var owner = request.Owner;
                if (!owners.Add(owner) || !EntityManager.Exists(owner) || EntityManager.HasComponent<Deleted>(owner) || EntityManager.HasComponent<Temp>(owner)) continue;
                emission.CollectPreviewPaint(owner, this, candidates);
                if (!EntityManager.HasBuffer<Game.Net.SubLane>(owner)) continue;
                foreach (var item in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true))
                {
                    var lane = item.m_SubLane;
                    if (!EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<PrefabRef>(lane) || EntityManager.HasComponent<Deleted>(lane)) continue;
                    if (EntityManager.HasComponent<Game.Net.CarLane>(lane) || EntityManager.HasComponent<Game.Net.ParkingLane>(lane) || EntityManager.HasComponent<Game.Net.PedestrianLane>(lane) || EntityManager.HasComponent<Game.Net.TrackLane>(lane)) continue;
                    if (!prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab, out var prefab) || !NodeLinePolicy.IsLineGeometry(prefab)) continue;
                    var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    if (!Matches(owner, lane, curve)) continue;
                    candidates.Add(lane);
                    replacements.CollectPreviewPaint(lane, candidates);
                }
            }
            // Swap instead of growing a history of entities replaced by road edits.
            var oldMatches = matches;
            matches = nextMatches;
            nextMatches = oldMatches;
            nextMatches.Clear();
            // One command-buffer playback avoids a structural-change sync for
            // every Hidden and BatchesUpdated component on every paint entity.
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            {
                bool changed = false;
                foreach (var entity in candidates)
                {
                    if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity) || EntityManager.HasComponent<Hidden>(entity)) continue;
                    commands.AddComponent<Hidden>(entity);
                    if (!EntityManager.HasComponent<BatchesUpdated>(entity)) commands.AddComponent<BatchesUpdated>(entity);
                    hidden.Add(entity);
                    changed = true;
                }
                if (changed)
                {
                    NativeVisualInvalidation.CompleteReaders(World);
                    commands.Playback(EntityManager);
                }
            }
            requests.Clear();
            candidates.Clear();
        }
    }

    public partial class MarkingPreviewRestoreSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystemManaged<MarkingPreviewSystem>()?.BeginFrame();
        }
    }

    public partial class CurbLineEmissionSystem
    {
        internal void CollectPreviewPaint(Entity owner, MarkingPreviewSystem preview, HashSet<Entity> output)
        {
            CollectPreviewPaint(_marks, owner, preview, output);
            CollectPreviewPaint(_joins, owner, preview, output);
        }

        private static void CollectPreviewPaint(Dictionary<Entity, RoadMarks> entries, Entity owner, MarkingPreviewSystem preview, HashSet<Entity> output)
        {
            if (!entries.TryGetValue(owner, out var marks)) return;
            for (int i = 0; i < marks.Entities.Count && i < marks.Curves.Count; i++)
            {
                var mark = marks.Curves[i];
                if (!mark.Protected && preview.Matches(owner, marks.Entities[i], mark.Curve)) output.Add(marks.Entities[i]);
            }
        }
    }
}
