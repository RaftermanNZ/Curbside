using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        private readonly struct VanillaDividerSlot
        {
            internal readonly int Index;
            internal readonly Bezier4x3 First, Last, PaintFirst, PaintLast;
            internal readonly VanillaDividerPolicy.LanePosition Position;
            internal readonly bool DrawFirst, DrawLast;

            internal VanillaDividerSlot(int index, Bezier4x3 first, Bezier4x3 last, Bezier4x3 paintFirst, Bezier4x3 paintLast,
                VanillaDividerPolicy.LanePosition position, bool drawFirst, bool drawLast)
            {
                Index = index; First = first; Last = last; PaintFirst = paintFirst; PaintLast = paintLast;
                Position = position; DrawFirst = drawFirst; DrawLast = drawLast;
            }
        }

        internal bool RoadDividerChoices(Entity owner, byte style, bool visibility, List<HoverLineChoice> choices)
        {
            int first = choices.Count;
            IndividualDividerChoices(owner, style, visibility, choices);
            foreach (var slot in ReadVanillaDividerSlots(owner, choices, first))
            {
                bool placed = PlacedDividerIntent(owner, slot.First, slot.Last) != 0;
                if (visibility && !placed) continue;
                var guides = new List<Bezier4x3>(2);
                var paint = new List<Bezier4x3>(2);
                if (Valid(slot.First)) guides.Add(slot.First);
                if (Valid(slot.Last)) guides.Add(slot.Last);
                if (slot.DrawFirst) paint.AddRange(HighwayLinePaths(owner, slot.PaintFirst, style, 3));
                if (slot.DrawLast) paint.AddRange(HighwayLinePaths(owner, slot.PaintLast, style, 3));
                choices.Add(new HoverLineChoice
                {
                    Role = 4, Individual = true, Slot = slot.Index, DividerPosition = slot.Position,
                    FirstBoundary = slot.First, LastBoundary = slot.Last, Paths = paint, Guides = guides,
                    Placed = placed
                });
            }
            return choices.Count > first;
        }

        private List<VanillaDividerSlot> ReadVanillaDividerSlots(Entity road, List<HoverLineChoice> authored, int firstAuthored)
        {
            var result = new List<VanillaDividerSlot>();
            if (!RoadSurfacePolicy.IsPaved(EntityManager, road) || !EntityManager.HasComponent<Composition>(road)
                || !EntityManager.HasComponent<EdgeGeometry>(road)) return result;
            var composition = EntityManager.GetComponentData<Composition>(road).m_Edge;
            if (!EntityManager.HasComponent<NetCompositionData>(composition)) return result;
            float width = EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            if (!math.isfinite(width) || width <= 0f) return result;

            var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);
            var stops = ReadStopMarks(road);
            var boundaries = VanillaBoundaries(composition);
            for (int i = 0; i < boundaries.Count; i++)
            {
                var boundary = boundaries[i];
                float fraction = boundary.X / width + .5f;
                if (!(fraction > 0f && fraction < 1f)) continue;
                var first = DividerBoundary(geometry, boundary.Height, fraction, true);
                var last = DividerBoundary(geometry, boundary.Height, fraction, false);
                if (!Valid(first)) first = default;
                if (!Valid(last)) last = default;
                if (!Valid(first) && !Valid(last)) continue;
                bool authoredFirst = false, authoredLast = false;
                for (int n = firstAuthored; n < authored.Count; n++)
                {
                    var choice = authored[n];
                    var guide = choice.Guides[0];
                    if (!VanillaDividerPolicy.HasAuthoredMark(first, last, guide)) continue;
                    choice.DividerPosition = boundary.Position;
                    authoredFirst |= Valid(first) && VanillaDividerPolicy.UsesHalf(first, guide);
                    authoredLast |= Valid(last) && VanillaDividerPolicy.UsesHalf(last, guide);
                }
                if (authoredFirst) first = default;
                if (authoredLast) last = default;
                bool drawFirst = Trim(first, out var paintFirst);
                bool drawLast = Trim(last, out var paintLast);
                if (!drawFirst && !drawLast) continue;
                result.Add(new VanillaDividerSlot(i + 1, first, last, paintFirst, paintLast, boundary.Position, drawFirst, drawLast));
            }
            return result;

            bool Trim(Bezier4x3 path, out Bezier4x3 paint)
            {
                paint = default;
                if (!Valid(path)) return false;
                GetStopRange(path, stops, true, out float lower, out float upper);
                return VanillaDividerPolicy.TrimPaint(path, lower, upper, out paint);
            }
        }

        private List<(float X, float Height, VanillaDividerPolicy.LanePosition Position)> VanillaBoundaries(Entity composition)
        {
            var lanes = new List<VanillaDividerPolicy.Lane>();
            foreach (var lane in TrafficLanes(composition))
                lanes.Add(new VanillaDividerPolicy.Lane(lane.X, lane.Width, lane.Y, lane.Reverse));
            return VanillaDividerPolicy.Boundaries(lanes);
        }

        private bool IsAdditionalVanillaDivider(Entity road, Bezier4x3 path)
        {
            if (!EntityManager.HasComponent<Composition>(road) || !EntityManager.HasComponent<EdgeGeometry>(road)) return false;
            var composition = EntityManager.GetComponentData<Composition>(road).m_Edge;
            if (!EntityManager.HasComponent<NetCompositionData>(composition)) return false;
            float width = EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            if (!math.isfinite(width) || width <= 0f) return false;
            var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);
            foreach (var boundary in VanillaBoundaries(composition))
            {
                float fraction = boundary.X / width + .5f;
                if (!(fraction > 0f && fraction < 1f)) continue;
                if (VanillaDividerPolicy.HasAuthoredMark(
                    DividerBoundary(geometry, boundary.Height, fraction, true),
                    DividerBoundary(geometry, boundary.Height, fraction, false), path)) return true;
            }
            return false;
        }

        private byte PlacedDividerIntent(Entity road, Bezier4x3 first, Bezier4x3 last)
        {
            if (!EntityManager.HasBuffer<MGRIndividualDivider>(road)) return 0;
            foreach (var intent in EntityManager.GetBuffer<MGRIndividualDivider>(road, true))
                if (VanillaDividerPolicy.IsGenerated(intent.Style) && (IndividualDividerPolicy.Matches(intent, first)
                    || IndividualDividerPolicy.Matches(intent, last))) return intent.Style;
            return 0;
        }

        internal void ApplyPlacedDivider(Entity road, Bezier4x3 first, Bezier4x3 last, byte style, bool remove)
        {
            bool hasFirst = Valid(first), hasLast = Valid(last);
            if (Mod.Saving || (!hasFirst && !hasLast)) return;
            var authored = new List<HoverLineChoice>();
            IndividualDividerChoices(road, 1, visibility: true, authored);
            bool matches = false;
            foreach (var slot in ReadVanillaDividerSlots(road, authored, 0))
            {
                if (!GranularHighwayLinePolicy.Matches(slot.First, first) || !GranularHighwayLinePolicy.Matches(slot.Last, last)) continue;
                matches = true;
                break;
            }
            if (!matches) return;

            if (!remove && LineStylePrefab(style) == Entity.Null) return;
            if (remove && !EntityManager.HasBuffer<MGRIndividualDivider>(road)) return;
            World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
            CompleteDependency();
            if (!EntityManager.HasBuffer<MGRIndividualDivider>(road)) EntityManager.AddBuffer<MGRIndividualDivider>(road);
            var intents = EntityManager.GetBuffer<MGRIndividualDivider>(road);
            int reused = 0;
            for (int i = 0; i < intents.Length; i++)
            {
                var intent = intents[i];
                if (VanillaDividerPolicy.IsGenerated(intent.Style) && (IndividualDividerPolicy.Matches(intent, first)
                    || IndividualDividerPolicy.Matches(intent, last))) reused++;
            }
            int needed = (hasFirst ? 1 : 0) + (hasLast ? 1 : 0);
            if (!remove && intents.Length - reused + needed > 256) return;
            if (remove && reused == 0) return;
            for (int i = intents.Length - 1; i >= 0; i--)
            {
                if (VanillaDividerPolicy.IsGenerated(intents[i].Style) && (IndividualDividerPolicy.Matches(intents[i], first)
                    || IndividualDividerPolicy.Matches(intents[i], last))) intents.RemoveAt(i);
            }
            if (!remove)
            {
                if (hasFirst) intents.Add(IndividualDividerPolicy.Intent(first, VanillaDividerPolicy.EncodeGenerated(style)));
                if (hasLast) intents.Add(IndividualDividerPolicy.Intent(last, VanillaDividerPolicy.EncodeGenerated(style)));
            }

            QueueRoad(road);
            SegmentUpgradePolicy.QueuePaint(EntityManager, road);
            UpgradeFeedback.Report("Selected divider", remove ? "Placed divider removed." : "Divider placed.");
        }

        private void AddPlacedDividers(Entity road, List<MarkCurve> output)
        {
            if (!EntityManager.HasBuffer<MGRIndividualDivider>(road)) return;
            bool hasPlacement = false;
            foreach (var intent in EntityManager.GetBuffer<MGRIndividualDivider>(road, true))
                if (VanillaDividerPolicy.IsGenerated(intent.Style)) { hasPlacement = true; break; }
            if (!hasPlacement) return;
            var authored = new List<HoverLineChoice>();
            IndividualDividerChoices(road, 1, visibility: true, authored);
            foreach (var slot in ReadVanillaDividerSlots(road, authored, 0))
            {
                if (slot.DrawFirst) AddHalf(slot.First, slot.PaintFirst);
                if (slot.DrawLast) AddHalf(slot.Last, slot.PaintLast);
            }

            void AddHalf(Bezier4x3 boundary, Bezier4x3 paint)
            {
                byte intent = PlacedDividerIntent(road, boundary, default);
                if (intent == 0) return;
                byte style = VanillaDividerPolicy.DecodeGenerated(intent);
                var prefab = LineStylePrefab(style);
                if (prefab == Entity.Null) return;
                output.RemoveAll(mark => mark.DividerUpgrade && !mark.Protected
                    && VanillaDividerPolicy.HasAuthoredMark(boundary, default, mark.Curve));
                foreach (var stroke in HighwayLinePaths(road, paint, style, 3))
                    output.Add(new MarkCurve(stroke, prefab) { DividerUpgrade = true });
            }
        }
    }
}
