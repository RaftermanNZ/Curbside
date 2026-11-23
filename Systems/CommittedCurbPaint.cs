using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        private bool TryReadPaintApproach(Entity road, Entity node, out EdgeNodeGeometry geometry,
            out NetCompositionData data, out List<float3> anchors, out bool start)
        {
            geometry = default; data = default; anchors = null; start = false;
            if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Edge>(road)
                || !EntityManager.HasComponent<Composition>(road) || EntityManager.HasComponent<Deleted>(road)
                || EntityManager.HasComponent<Game.Tools.Temp>(road)) return false;

            var edge = EntityManager.GetComponentData<Edge>(road);
            start = edge.m_Start == node;
            if (start == (edge.m_End == node)) return false;
            if (start ? !EntityManager.HasComponent<StartNodeGeometry>(road) : !EntityManager.HasComponent<EndNodeGeometry>(road)) return false;

            var composition = EntityManager.GetComponentData<Composition>(road);
            var definition = start ? composition.m_StartNode : composition.m_EndNode;
            if (!EntityManager.HasComponent<NetCompositionData>(definition) || !EntityManager.HasBuffer<NetCompositionLane>(definition)) return false;

            data = EntityManager.GetComponentData<NetCompositionData>(definition);
            geometry = start ? EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry
                : EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry;
            // Special nodes keep the existing split geometry path.
            if (geometry.m_MiddleRadius > 0 || !math.isfinite(data.m_Width) || data.m_Width < 1) return false;
            anchors = ReadCurbAnchors(definition, EntityManager.GetBuffer<NetCompositionLane>(definition, true));
            if (anchors.Count < 2) return false;
            anchors.Sort((a, b) => a.x.CompareTo(b.x));
            return true;
        }

        private static bool TryPaintCurb(EdgeNodeGeometry geometry, NetCompositionData data, float3 anchor,
            bool left, float offset, out Bezier4x3 curb)
        {
            curb = default;
            if (!CurbPlacement.TryMapComposed(data.m_Width, anchor, left, offset, out var position, out _)) return false;
            curb = SignalJunctionFootprint.Curb(geometry, data, position.x);
            AddPaintHeight(ref curb, position.y);
            return Valid(curb);
        }

        private static void AddPaintHeight(ref Bezier4x3 curve, float height)
        {
            curve.a.y += height; curve.b.y += height; curve.c.y += height; curve.d.y += height;
        }

        private bool TryReadPaintCurbs(Entity road, Entity node, float offset, out Bezier4x3 left, out Bezier4x3 right)
        {
            left = right = default;
            return TryReadPaintApproach(road, node, out var geometry, out var data, out var anchors, out _)
                && TryPaintCurb(geometry, data, anchors[0], true, offset, out left)
                && TryPaintCurb(geometry, data, anchors[anchors.Count - 1], false, offset, out right);
        }

        private Bezier4x3 ReadSectionCurb(Entity road, bool start, float3 anchor, Bezier4x3 junction)
        {
            if (!EntityManager.HasComponent<EdgeGeometry>(road)) return default;
            var definition = EntityManager.GetComponentData<Composition>(road).m_Edge;
            if (!EntityManager.HasComponent<NetCompositionData>(definition)) return default;
            float width = EntityManager.GetComponentData<NetCompositionData>(definition).m_Width;
            if (!CurbPlacement.TryMapComposed(width, anchor, true, 0, out var position, out var fraction)) return default;
            var edge = EntityManager.GetComponentData<EdgeGeometry>(road);
            var segment = start ? edge.m_Start : edge.m_End;
            var curve = MathUtils.Lerp(segment.m_Left, segment.m_Right, fraction);
            AddPaintHeight(ref curve, position.y);
            // Verify continuity at the actual road mouth before using a section
            // boundary. Some transitions use different curb slots at the node.
            return math.distancesq(start ? curve.a : curve.d, junction.a) <= .09f && Valid(curve) ? curve : default;
        }

        internal bool TryFitCrosswalkRails(Entity node, Entity road, float width, ref Bezier4x3 first, ref Bezier4x3 second)
        {
            // Require the native lane's resolved approach. The old conservative
            // fit remains available when the lane has no reliable owner.
            if (!TryReadPaintApproach(road, node, out var geometry, out var data, out var anchors, out bool start)) return false;
            float best = float.MaxValue;
            Bezier4x3 firstFit = default, secondFit = default;
            for (int i = 1; i < anchors.Count; i++)
            {
                // Adjacent boundaries keep divided crossings on their own side.
                if (!TryPaintCurb(geometry, data, anchors[i - 1], true, 0, out var left)
                    || !TryPaintCurb(geometry, data, anchors[i], false, 0, out var right)) continue;
                var leftSection = ReadSectionCurb(road, start, anchors[i - 1], left);
                var rightSection = ReadSectionCurb(road, start, anchors[i], right);
                if (!CrosswalkCurbGeometry.Fit(first, left, right, leftSection, rightSection, width, out var a)
                    || !CrosswalkCurbGeometry.Fit(second, left, right, leftSection, rightSection, width, out var b)) continue;
                float score = math.distancesq(a.a, first.a) + math.distancesq(a.d, first.d)
                    + math.distancesq(b.a, second.a) + math.distancesq(b.d, second.d);
                if (score >= best) continue;
                best = score; firstFit = a; secondFit = b;
            }
            if (best == float.MaxValue) return false;
            first = firstFit; second = secondFit;
            return true;
        }

        private bool AddNativePaintCorner(Bezier4x3 firstCurb, Bezier4x3 lastCurb,
            JoinCap first, JoinCap last, List<MarkCurve> output)
        {
            if (!NativePaintCornerGeometry.TryJoin(firstCurb, lastCurb, first.Point, first.Outward, last.Point,
                    last.Outward, out var a, out var b, out var startLead, out var endLead)) return false;
            if (startLead.HasValue) output.Add(new MarkCurve(startLead.Value, first.Prefab, .15f, false, true));
            output.Add(new MarkCurve(a, first.Prefab, .15f, false, true));
            output.Add(new MarkCurve(b, last.Prefab, .15f, false, true));
            if (endLead.HasValue) output.Add(new MarkCurve(endLead.Value, last.Prefab, .15f, false, true));
            return true;
        }
    }
}
