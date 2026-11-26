using System;
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
        private void QueueChangedJunctionGeometry()
        {
            var crossings = World.GetExistingSystemManaged<IntersectionPaintSystem>();
            // Geometry mods can change the two node patches without changing
            // ConnectedEdge or the rendering/traffic sublane lists.
            using var roads = _changedJunctionGeometry.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var road in roads)
            {
                var edge = EntityManager.GetComponentData<Edge>(road);
                Queue(edge.m_Start);
                Queue(edge.m_End);
            }

            void Queue(Entity node)
            {
                if (node == Entity.Null || _dirtyNodes.Contains(node)) return;
                if (EntityManager.HasComponent<MGRJunctionEdgeLines>(node) || crossings?.HasCrosswalkEdges(node) == true)
                    _dirtyNodes.Add(node);
            }
        }

        private bool TryReadPaintApproach(Entity road, Entity node, out EdgeNodeGeometry geometry,
            out NetCompositionData data, out List<float3> anchors, out bool start, bool completeExterior = true, bool includeAlleyPavement = false, bool allowEmpty = false)
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
            if (!EntityManager.HasComponent<NetCompositionData>(definition)) return false;

            data = EntityManager.GetComponentData<NetCompositionData>(definition);
            geometry = start ? EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry
                : EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry;
            // Special nodes keep the existing split geometry path.
            if (geometry.m_MiddleRadius > 0 || !math.isfinite(data.m_Width) || data.m_Width < 1) return false;
            anchors = ReadAuthoredCurbAnchors(definition);
            if (completeExterior)
            {
                // Parking and sidewalk pieces can both contribute anchors on
                // the same side. They do not create separate crossing spans.
                if ((!CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, true, out _)
                    || !CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, false, out _))
                    && EntityManager.HasBuffer<NetCompositionLane>(definition))
                {
                    var estimated = ReadDecalAnchors(definition, data.m_Width,
                        EntityManager.GetBuffer<NetCompositionLane>(definition, true), data.m_MiddleOffset, requirePavement: true);
                    for (int side = 0; side < 2; side++)
                    {
                        bool left = side == 0;
                        if (!CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, left, out _)
                            && CurbPlacement.TryOuterAnchor(estimated, data.m_Width, data.m_MiddleOffset, left, out var fallback))
                            AddDistinctCurb(anchors, fallback);
                    }
                }
                if (!CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, true, out var leftCurb)
                    || !CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, false, out var rightCurb))
                    return false;
                anchors.Clear();
                anchors.Add(leftCurb);
                anchors.Add(rightCurb);
            }
            if (anchors.Count == 0 && includeAlleyPavement && RoadSurfacePolicy.IsAlley(EntityManager, road))
                AddAlleyPavementAnchors(definition, data, anchors);
            if (anchors.Count == 0 && !allowEmpty) return false;
            anchors.Sort((a, b) => a.x.CompareTo(b.x));
            return true;
        }

        private void AddAlleyPavementAnchors(Entity composition, NetCompositionData data, List<float3> anchors)
        {
            if (!RoadSurfacePolicy.PavedFlags(data.m_Flags.m_General)
                || ((data.m_Flags.m_Left | data.m_Flags.m_Right) & CompositionFlags.Side.Raised) != 0
                || !EntityManager.HasBuffer<NetCompositionPiece>(composition)) return;

            var left = new float3(float.PositiveInfinity, 0, 0);
            var right = new float3(float.NegativeInfinity, 0, 0);
            foreach (var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition, true))
            {
                if ((piece.m_PieceFlags & NetPieceFlags.Surface) == 0
                    || (piece.m_SectionFlags & (NetSectionFlags.Median | NetSectionFlags.Hidden | NetSectionFlags.HiddenSurface
                    | NetSectionFlags.Overhead | NetSectionFlags.Underground)) != 0
                    || !EntityManager.HasBuffer<NetPieceLane>(piece.m_Piece)) continue;

                bool inverted = (piece.m_SectionFlags & NetSectionFlags.Invert) != 0;
                foreach (var lane in EntityManager.GetBuffer<NetPieceLane>(piece.m_Piece, true))
                {
                    if (!EntityManager.HasComponent<NetLaneData>(lane.m_Lane)) continue;
                    var laneData = EntityManager.GetComponentData<NetLaneData>(lane.m_Lane);
                    var flags = laneData.m_Flags | lane.m_ExtraFlags;
                    if ((flags & (LaneFlags.Road | LaneFlags.Master | LaneFlags.Underground | LaneFlags.Virtual)) != LaneFlags.Road
                        || !CurbPlacement.TryPavementLaneBounds(piece.m_Offset, lane.m_Position, inverted,
                            laneData.m_Width, piece.m_Size.x, out var low, out var high)) continue;
                    if (low.x < left.x) left = low;
                    if (high.x > right.x) right = high;
                }
            }

            if (!math.all(math.isfinite(new float2(left.x, right.x))) || left.x >= data.m_MiddleOffset
                || right.x <= data.m_MiddleOffset || math.abs(left.x) > data.m_Width * .5f + .01f
                || math.abs(right.x) > data.m_Width * .5f + .01f) return;
            anchors.Add(left);
            anchors.Add(right);
        }

        private List<float3> ReadAuthoredCurbAnchors(Entity definition)
        {
            var anchors = EntityManager.HasBuffer<NetCompositionLane>(definition)
                ? ReadCurbAnchors(definition, EntityManager.GetBuffer<NetCompositionLane>(definition, true))
                : new List<float3>();
            foreach (var anchor in ReadPieceCurbAnchors(definition)) AddDistinctCurb(anchors, anchor);
            return anchors;
        }

        internal List<float3> ReadPieceCurbAnchors(Entity composition)
        {
            // Native node compositions can carry pieces without a lane buffer.
            // Use the same piece inversion and offset as AddCompositionLanes.
            var anchors = new List<float3>();
            if (!EntityManager.HasBuffer<NetCompositionPiece>(composition)) return anchors;
            foreach (var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition, true))
            {
                if ((piece.m_SectionFlags & (NetSectionFlags.Median | NetSectionFlags.Underground | NetSectionFlags.Overhead
                    | NetSectionFlags.Hidden | NetSectionFlags.HiddenSurface)) != 0 ||
                    !EntityManager.HasBuffer<NetPieceLane>(piece.m_Piece)) continue;
                var lanes = EntityManager.GetBuffer<NetPieceLane>(piece.m_Piece, true);
                bool inverted = (piece.m_SectionFlags & NetSectionFlags.Invert) != 0;
                bool hasBoardingAnchor = false;
                foreach (var lane in lanes)
                {
                    if (!_prefabs.TryGetPrefab<NetLanePrefab>(lane.m_Lane, out var prefab) || prefab == null || prefab.name != "Boarding Lane 0") continue;
                    if (!CurbPlacement.TryPieceAnchor(piece.m_Offset, lane.m_Position, inverted, out var position)) continue;
                    AddDistinctCurb(anchors, position);
                    hasBoardingAnchor = true;
                }

                if (hasBoardingAnchor || !EntityManager.HasComponent<NetVertexMatchData>(piece.m_Piece)) continue;
                var offsets = EntityManager.GetComponentData<NetVertexMatchData>(piece.m_Piece).m_Offsets;
                bool stockParkingCurb = _prefabs.TryGetPrefab<NetPiecePrefab>(piece.m_Piece, out var piecePrefab)
                    && piecePrefab != null
                    && piecePrefab.name.StartsWith("Sidewalk With Parking Piece ", StringComparison.Ordinal)
                    && piecePrefab.name.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) < 0;
                if (stockParkingCurb)
                {
                    foreach (var lane in lanes)
                    {
                        if (!EntityManager.HasComponent<NetLaneData>(lane.m_Lane)
                            || (EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Flags & (LaneFlags.Parking | LaneFlags.Road)) == 0)
                            continue;
                        if (CurbPlacement.TryParkingCurb(piece.m_Offset, offsets.y, lane.m_Position.y, inverted, out var curb))
                            AddDistinctCurb(anchors, curb);
                        break;
                    }
                }
                else if ((piece.m_PieceFlags & NetPieceFlags.Surface) != 0
                    && TryReadRaisedPieceBoundary(piece, lanes, offsets, inverted, out var boundary))
                    AddDistinctCurb(anchors, boundary);
            }
            return anchors;
        }

        private bool TryReadRaisedPieceBoundary(NetCompositionPiece piece, DynamicBuffer<NetPieceLane> lanes,
            float3 offsets, bool inverted, out float3 boundary)
        {
            boundary = default;
            float bestGap = float.PositiveInfinity;
            foreach (var walk in lanes)
            {
                if (!EntityManager.HasComponent<NetLaneData>(walk.m_Lane)) continue;
                var walkData = EntityManager.GetComponentData<NetLaneData>(walk.m_Lane);
                if (walkData.m_Width <= 0 || (walkData.m_Flags & LaneFlags.Pedestrian) == 0) continue;
                foreach (var road in lanes)
                {
                    if (!EntityManager.HasComponent<NetLaneData>(road.m_Lane)) continue;
                    var roadData = EntityManager.GetComponentData<NetLaneData>(road.m_Lane);
                    if (roadData.m_Width <= 0 || (roadData.m_Flags & (LaneFlags.Road | LaneFlags.Parking)) == 0
                        || (roadData.m_Flags & (LaneFlags.Master | LaneFlags.Underground)) != 0) continue;
                    float gap = math.abs(walk.m_Position.x - road.m_Position.x);
                    if (gap >= bestGap || !CurbPlacement.TryRaisedPieceBoundary(offsets, road.m_Position, walk.m_Position, out float offset))
                        continue;
                    if (CurbPlacement.TryParkingCurb(piece.m_Offset, offset, road.m_Position.y, inverted, out var candidate))
                    {
                        bestGap = gap;
                        boundary = candidate;
                    }
                }
            }
            return math.isfinite(bestGap);
        }

        private static void AddDistinctCurb(List<float3> anchors, float3 position)
        {
            foreach (var anchor in anchors)
                if (math.abs(anchor.x - position.x) < .05f) return;
            anchors.Add(position);
        }

        private struct SurfaceMedian
        {
            internal float2 Bounds;
            internal float Height;
            internal bool BlocksCrosswalk;
        }

        internal struct MedianOpening
        {
            internal Bezier4x3 Left, Right;
            internal float Width;
            internal bool Split;
        }

        private static void ReadMedianCurbs(List<SurfaceMedian> medians, List<float3> anchors)
        {
            float left = anchors[0].x, right = anchors[1].x;
            foreach (var median in medians)
            {
                if (median.Bounds.x <= left + .05f || median.Bounds.y >= right - .05f) continue;
                AddDistinctCurb(anchors, new float3(median.Bounds.x, median.Height, 0));
                AddDistinctCurb(anchors, new float3(median.Bounds.y, median.Height, 0));
            }
        }

        internal List<MedianOpening> ReadMedianOpenings(Entity road, Entity node)
        {
            if (!TryReadPaintApproach(road, node, out var geometry, out var data, out _, out bool start,
                completeExterior: false, allowEmpty: true)) return null;
            var composition = EntityManager.GetComponentData<Composition>(road);
            var definition = start ? composition.m_StartNode : composition.m_EndNode;
            if (!EntityManager.HasBuffer<NetCompositionCrosswalk>(definition)) return null;
            var medians = ReadSurfaceMedians(definition, data, false);
            // An edit can remove a single island from the node patch while
            // keeping its neighbors. Use section bounds for that island only.
            int authoredCount = medians.Count;
            foreach (var section in ReadSurfaceMedians(composition.m_Edge, data, start))
            {
                bool represented = false;
                for (int i = 0; i < authoredCount; i++)
                    if (CrosswalkCurbGeometry.TryMergeMedianSpans(medians[i].Bounds,section.Bounds,out _))
                    { represented = true; break; }
                if (!represented) medians.Add(section);
            }
            if (medians.Count == 0) return null;

            var openings = new List<MedianOpening>(medians.Count);
            var crossings = EntityManager.GetBuffer<NetCompositionCrosswalk>(definition, true);
            foreach (var median in medians)
            {
                if (median.BlocksCrosswalk || !CanCrossMedian(median.Bounds, crossings, out bool split)) continue;
                if (!TryPaintCurb(geometry,data,new float3(median.Bounds.x,median.Height,0),out var left) ||
                    !TryPaintCurb(geometry,data,new float3(median.Bounds.y,median.Height,0),out var right)) continue;
                openings.Add(new MedianOpening { Left = left, Right = right, Width = median.Bounds.y - median.Bounds.x, Split = split });
            }
            return openings;
        }

        private static bool CanCrossMedian(float2 median, DynamicBuffer<NetCompositionCrosswalk> crossings, out bool split)
        {
            split = false;
            foreach (var crossing in crossings)
                if ((crossing.m_Flags & LaneFlags.Underground) == 0 &&
                    CrosswalkCurbGeometry.CoversMedianOpening(median, crossing.m_Start, crossing.m_End)) return true;
            for (int i = 0; i < crossings.Length; i++)
            {
                var left = crossings[i];
                if ((left.m_Flags & LaneFlags.Underground) != 0) continue;
                var outerLeft = left.m_Start.x < left.m_End.x ? left.m_Start : left.m_End;
                var innerLeft = left.m_Start.x < left.m_End.x ? left.m_End : left.m_Start;
                if (innerLeft.x >= median.y) continue;
                for (int j = 0; j < crossings.Length; j++)
                {
                    var right = crossings[j];
                    if (i == j || (right.m_Flags & LaneFlags.Underground) != 0) continue;
                    var innerRight = right.m_Start.x < right.m_End.x ? right.m_Start : right.m_End;
                    var outerRight = right.m_Start.x < right.m_End.x ? right.m_End : right.m_Start;
                    if (CrosswalkCurbGeometry.BracketsMedianOpening(median, outerLeft, innerLeft, innerRight, outerRight, .45f))
                    {
                        split = true;
                        return true;
                    }
                }
            }
            return false;
        }

        private List<SurfaceMedian> ReadSurfaceMedians(Entity definition, NetCompositionData data, bool invert)
        {
            var medians = new List<SurfaceMedian>();
            if (!EntityManager.HasBuffer<NetCompositionPiece>(definition)) return medians;
            foreach (var piece in EntityManager.GetBuffer<NetCompositionPiece>(definition, true))
            {
                if ((piece.m_SectionFlags & NetSectionFlags.Median) == 0 ||
                    (piece.m_SectionFlags & (NetSectionFlags.Hidden | NetSectionFlags.HiddenSurface |
                        NetSectionFlags.Underground | NetSectionFlags.Overhead)) != 0 ||
                    (piece.m_PieceFlags & NetPieceFlags.Surface) == 0) continue;
                float x = invert ? -piece.m_Offset.x : piece.m_Offset.x;
                var raw = new float2(x - piece.m_Size.x * .5f, x + piece.m_Size.x * .5f);
                if (!CurbPlacement.TryCrossingMedianBounds(data.m_Width, data.m_MiddleOffset,
                    data.m_SyncVertexOffsetsLeft.w, data.m_SyncVertexOffsetsRight.x, raw, out var bounds)) continue;
                var median = new SurfaceMedian { Bounds = bounds, Height = piece.m_Offset.y,
                    BlocksCrosswalk = (piece.m_PieceFlags & NetPieceFlags.BlockCrosswalk) != 0 };
                // Authored pieces for the same island can overlap. A real
                // carriageway gap between islands must stay a separate span.
                for (int i = medians.Count - 1; i >= 0; i--)
                {
                    var existing = medians[i];
                    if (math.abs(existing.Height - median.Height) > .08f ||
                        !CrosswalkCurbGeometry.TryMergeMedianSpans(median.Bounds, existing.Bounds, out var merged)) continue;
                    median.Bounds = merged;
                    median.BlocksCrosswalk |= existing.BlocksCrosswalk;
                    medians.RemoveAt(i);
                    i = medians.Count;
                }
                medians.Add(median);
            }
            medians.Sort((a,b) => a.Bounds.x.CompareTo(b.Bounds.x));
            return medians;
        }

        private static bool TryPaintCurb(EdgeNodeGeometry geometry, NetCompositionData data, float3 anchor,
            out Bezier4x3 curb)
        {
            curb = default;
            if (!CurbPlacement.TryMapBoundary(data.m_Width, anchor, out var position, out _)) return false;
            curb = SignalJunctionFootprint.Curb(geometry, data, position.x);
            AddPaintHeight(ref curb, position.y);
            return Valid(curb);
        }

        private static void AddPaintHeight(ref Bezier4x3 curve, float height)
        {
            curve.a.y += height; curve.b.y += height; curve.c.y += height; curve.d.y += height;
        }

        private readonly Dictionary<Entity, List<Bezier4x3>> _paintedCornerCurbs
            = new Dictionary<Entity, List<Bezier4x3>>();

        private readonly Dictionary<Entity, List<CurbPlacement.MeshCurbFace>> _curbMeshFaces
            = new Dictionary<Entity, List<CurbPlacement.MeshCurbFace>>();

        private float3 ReadMeshCurbAnchor(Entity composition, float3 reference, bool left)
        {
            if (!EntityManager.HasBuffer<NetCompositionPiece>(composition)) return reference;
            float best = .75f;
            var result = reference;
            foreach (var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition, true))
            {
                const NetSectionFlags excluded = NetSectionFlags.Median | NetSectionFlags.Hidden | NetSectionFlags.HiddenSurface
                    | NetSectionFlags.Overhead | NetSectionFlags.Underground | NetSectionFlags.FlipMesh | NetSectionFlags.HalfLength;
                if ((piece.m_SectionFlags & excluded) != 0 || (piece.m_PieceFlags & NetPieceFlags.Surface) == 0
                    || (piece.m_PieceFlags & NetPieceFlags.PreserveShape) != 0
                    || ((piece.m_SectionFlags & NetSectionFlags.Right) == 0) != left
                    || math.abs(piece.m_Offset.x - reference.x) > piece.m_Size.x * .5f + .75f) continue;
                var faces = ReadCurbMeshFaces(piece.m_Piece);
                if (faces == null) continue;
                bool inverted = (piece.m_SectionFlags & NetSectionFlags.Invert) != 0;
                if (!CurbPlacement.TryMeshCurbAnchor(faces, piece.m_Offset, piece.m_Size.z * .5f,
                    inverted, left, reference, out var candidate)) continue;
                float distance = math.abs(candidate.x - reference.x);
                if (distance >= best) continue;
                best = distance;
                result = candidate;
            }
            return result;
        }

        private List<CurbPlacement.MeshCurbFace> ReadCurbMeshFaces(Entity piece)
        {
            if (_curbMeshFaces.TryGetValue(piece, out var cached)) return cached;
            if (!EntityManager.HasBuffer<MeshVertex>(piece) || !EntityManager.HasBuffer<MeshNormal>(piece)
                || !EntityManager.HasBuffer<MeshIndex>(piece)) return null;

            var indices = EntityManager.GetBuffer<MeshIndex>(piece, true);
            var vertices = EntityManager.GetBuffer<MeshVertex>(piece, true);
            var normals = EntityManager.GetBuffer<MeshNormal>(piece, true);
            if (indices.Length == 0 || indices.Length > 12288 || vertices.Length != normals.Length) return null;

            var faces = new List<CurbPlacement.MeshCurbFace>();
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i].m_Index, b = indices[i + 1].m_Index, c = indices[i + 2].m_Index;
                if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length) continue;
                var normal = (normals[a].m_Normal + normals[b].m_Normal + normals[c].m_Normal) / 3f;
                if (CurbPlacement.TryMeshCurbFace(vertices[a].m_Vertex, vertices[b].m_Vertex, vertices[c].m_Vertex,
                    normal, out var face)) faces.Add(face);
            }
            faces.Sort((a, b) => a.Start.CompareTo(b.Start));
            if (_curbMeshFaces.Count < 512) _curbMeshFaces.Add(piece, faces);
            return faces;
        }

        private void ReadRenderedPaintCurbs(Entity road, Entity node, out Bezier4x3 left, out Bezier4x3 right, bool includeAlleyPavement = true)
        {
            left = right = default;
            if (!TryReadPaintApproach(road, node, out var geometry, out var data, out var anchors, out bool atStart, completeExterior: false, includeAlleyPavement: includeAlleyPavement))
                return;
            // Middle connections, roundabouts and other patched meshes use
            // different vertex encodings from ordinary left/right node halves.
            if (geometry.m_MiddleRadius > 0 || math.any(geometry.m_Middle.d.xy != 0)
                || (data.m_Flags.m_General & CompositionFlags.General.StraightNodeEnd) != 0) return;
            var composition = EntityManager.GetComponentData<Composition>(road);
            Entity definition = atStart ? composition.m_StartNode : composition.m_EndNode;
            float halfWidth = data.m_Width * .5f;
            if (CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, true, out var a))
            {
                a = ReadMeshCurbAnchor(definition, a, left: true);
                if (NativePaintCornerGeometry.TryRenderedCurb(geometry.m_Left.m_Left, geometry.m_Left.m_Right,
                    (a.x + halfWidth) / (halfWidth + data.m_MiddleOffset),
                    data.m_SyncVertexOffsetsLeft, geometry.m_SyncVertexTargetsLeft, out left))
                    AddPaintHeight(ref left, a.y + .01f);
            }
            if (CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, false, out var b))
            {
                b = ReadMeshCurbAnchor(definition, b, left: false);
                if (NativePaintCornerGeometry.TryRenderedCurb(geometry.m_Right.m_Left, geometry.m_Right.m_Right,
                    (b.x - data.m_MiddleOffset) / (halfWidth - data.m_MiddleOffset),
                    data.m_SyncVertexOffsetsRight, geometry.m_SyncVertexTargetsRight, out right))
                    AddPaintHeight(ref right, b.y + .01f);
            }
        }

        private bool TryReadPaintCurbs(Entity road, Entity node, out Bezier4x3 left, out Bezier4x3 right)
        {
            ReadPaintCurbs(road, node, out left, out right);
            return Valid(left) && Valid(right);
        }

        private void ReadPaintCurbs(Entity road, Entity node, out Bezier4x3 left, out Bezier4x3 right, bool includeAlleyPavement = false)
        {
            left = right = default;
            if (!TryReadPaintApproach(road, node, out var geometry, out var data, out var anchors, out _, completeExterior: false, includeAlleyPavement: includeAlleyPavement)) return;
            // Read a side only when its own surface piece supplies a boundary.
            // The center or median cannot stand in for a missing exterior curb.
            if (CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, true, out var a))
                TryPaintCurb(geometry, data, a, out left);
            if (CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, false, out var b))
                TryPaintCurb(geometry, data, b, out right);
        }

        private Bezier4x3 ReadSectionCurb(Entity road, bool start, float3 anchor, Bezier4x3 junction)
        {
            if (!EntityManager.HasComponent<EdgeGeometry>(road)) return default;
            var definition = EntityManager.GetComponentData<Composition>(road).m_Edge;
            if (!EntityManager.HasComponent<NetCompositionData>(definition)) return default;
            float width = EntityManager.GetComponentData<NetCompositionData>(definition).m_Width;
            // GeometrySystem builds the start-node patch from Invert(edge.m_Start).
            // Its end-node patch uses edge.m_End directly.
            if (start) anchor.x = -anchor.x;
            if (!CurbPlacement.TryMapBoundary(width, anchor, out var position, out var fraction)) return default;
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
            var composition = EntityManager.GetComponentData<Composition>(road);
            var definition = start ? composition.m_StartNode : composition.m_EndNode;
            var medians = ReadSurfaceMedians(definition, data, false);
            ReadMedianCurbs(medians, anchors);
            if (medians.Count > 0) anchors.Sort((a, b) => a.x.CompareTo(b.x));
            float best = float.MaxValue;
            Bezier4x3 firstFit = default, secondFit = default;
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                if (InsideMedian(anchors[i].x) || !TryPaintCurb(geometry, data, anchors[i], out var left)) continue;
                var leftSection = ReadSectionCurb(road, start, anchors[i], left);
                for (int j = i + 1; j < anchors.Count; j++)
                {
                    if (!TryPaintCurb(geometry, data, anchors[j], out var right)) break;
                    if (j > i + 1 && !CanContinue(first, second, anchors[j - 2], anchors[j - 1])) break;
                    if (InsideMedian((anchors[j].x + anchors[j - 1].x) * .5f)) continue;
                    var rightSection = ReadSectionCurb(road, start, anchors[j], right);
                    if (!CrosswalkCurbGeometry.Fit(first, left, right, leftSection, rightSection, width, out var a)
                        || !CrosswalkCurbGeometry.Fit(second, left, right, leftSection, rightSection, width, out var b)) continue;
                    float score = math.distancesq(a.a, first.a) + math.distancesq(a.d, first.d)
                        + math.distancesq(b.a, second.a) + math.distancesq(b.d, second.d);
                    if (score >= best) continue;
                    best = score; firstFit = a; secondFit = b;
                }
            }
            if (best == float.MaxValue) return false;
            first = firstFit; second = secondFit;
            return true;

            bool InsideMedian(float x)
            {
                foreach (var median in medians)
                    if (x >= median.Bounds.x - .025f && x < median.Bounds.y - .025f) return true;
                return false;
            }

            bool CanContinue(Bezier4x3 a, Bezier4x3 b, float3 low, float3 high)
            {
                if (!InsideMedian((low.x + high.x) * .5f)) return true;
                foreach (var median in medians)
                    if (median.BlocksCrosswalk && low.x < median.Bounds.y && high.x > median.Bounds.x) return false;
                return TryPaintCurb(geometry, data, low, out var left) &&
                    TryPaintCurb(geometry, data, high, out var right) &&
                    CrosswalkCurbGeometry.CrossesBothBoundaries(a,left,right) &&
                    CrosswalkCurbGeometry.CrossesBothBoundaries(b,left,right);
            }
        }

        internal void JoinVisibleCrosswalkEdges(Entity node, Entity road, bool fittedToCurbs, float railWidth, ref Bezier4x3 first, ref Bezier4x3 second)
        {
            var edges = NativeNodePaintPaths(node, 2, visibleOnly:true);
            if (road != Entity.Null) edges.AddRange(NativeNodePaintPaths(road, 2, visibleOnly:true));
            List<Bezier4x3> emittedCurbEdges = null;
            if (_joins.TryGetValue(node, out var nodeMarks)) AddVisibleEdges(nodeMarks);
            if (_marks.TryGetValue(road, out var roadMarks)) AddVisibleEdges(roadMarks);
            List<Bezier4x3> boundaries = null;
            if (fittedToCurbs && edges.Count > 0)
            {
                // Join to the same corner outline that produced the visible
                // stripe. A different guide can put a fictitious curb across
                // the inboard end of a correctly paved crossing.
                if (!_paintedCornerCurbs.TryGetValue(node, out boundaries))
                {
                    ReadRenderedPaintCurbs(road, node, out var left, out var right, includeAlleyPavement: false);
                    boundaries = new List<Bezier4x3>(2);
                    if (Valid(left)) boundaries.Add(left);
                    if (Valid(right)) boundaries.Add(right);
                }
            }
            else if (!fittedToCurbs && edges.Count > 0 &&
                TryReadPaintApproach(road, node, out var geometry, out var data, out var anchors, out bool start, completeExterior: false))
            {
                boundaries = new List<Bezier4x3>();
                for (int side = 0; side < 2; side++)
                {
                    if (!CurbPlacement.TryOuterAnchor(anchors, data.m_Width, data.m_MiddleOffset, side == 0, out var anchor)
                        || !TryPaintCurb(geometry, data, anchor, out var curb)) continue;
                    boundaries.Add(curb);
                    boundaries.Add(ReadSectionCurb(road, start, anchor, curb));
                }
            }
            float curbLineOffset = Mod.Settings.CurbLineOffset;
            first = CrosswalkCurbGeometry.JoinEdgeEnds(first, edges, fittedToCurbs, boundaries, .25f, railWidth, emittedCurbEdges, curbLineOffset, requireCurbEvidence: true);
            second = CrosswalkCurbGeometry.JoinEdgeEnds(second, edges, fittedToCurbs, boundaries, .25f, railWidth, emittedCurbEdges, curbLineOffset, requireCurbEvidence: true);

            void AddVisibleEdges(RoadMarks marks)
            {
                for (int i = 0; i < marks.Curves.Count && i < marks.Entities.Count; i++)
                {
                    var entity = marks.Entities[i];
                    if (marks.Curves[i].Curb && EntityManager.Exists(entity)
                        && !EntityManager.HasComponent<Deleted>(entity) && !EntityManager.HasComponent<Game.Tools.Hidden>(entity))
                    {
                        var mark = marks.Curves[i];
                        edges.Add(mark.Curve);
                        if (fittedToCurbs && !mark.Median)
                        {
                            emittedCurbEdges ??= new List<Bezier4x3>();
                            emittedCurbEdges.Add(mark.Curve);
                        }
                    }
                }
            }
        }

        private bool AddNativePaintCorner(Bezier4x3 firstCurb, Bezier4x3 lastCurb,
            float3 firstAcross, float3 lastAcross, Bezier4x3 firstSection, Bezier4x3 lastSection,
            JoinCap first, JoinCap last, float stripeRadius, List<MarkCurve> output, List<Bezier4x3> curbReferences)
        {
            float setback = Mod.Settings.CurbLineOffset;
            bool hasFirst = Valid(firstCurb) && first.Prefab != Entity.Null;
            bool hasLast = Valid(lastCurb) && last.Prefab != Entity.Null;
            if (!hasFirst && !hasLast) return false;
            if (hasFirst && hasLast && NativePaintCornerGeometry.NativeSeamMatches(firstCurb, lastCurb)
                && NativePaintCornerGeometry.TryJoinAlongCurb(firstCurb, lastCurb, firstAcross, lastAcross,
                    setback, stripeRadius, firstSection, lastSection, first.Point, first.Outward,
                    last.Point, last.Outward, out var a, out var b, out var startLead, out var endLead))
            {
                BalanceLeads(firstCurb, lastCurb, ref a, ref b, ref startLead, ref endLead);
                RecordCurbs(firstCurb, lastCurb);
                AddLeg(a, startLead, first.Prefab, end: false);
                AddLeg(b, endLead, last.Prefab, end: true);
                return true;
            }

            if (hasFirst && hasLast
                && NativePaintCornerGeometry.TryContinueMixedCurb(firstCurb, lastCurb, out var curbA, out var curbB)
                && NativePaintCornerGeometry.TryJoinAlongCurb(curbA, curbB, firstAcross, lastAcross, setback, stripeRadius,
                    firstSection, lastSection, first.Point, first.Outward, last.Point, last.Outward,
                    out var continuedA, out var continuedB, out var leadA, out var leadB))
            {
                BalanceLeads(curbA, curbB, ref continuedA, ref continuedB, ref leadA, ref leadB);
                RecordCurbs(curbA, curbB);
                AddLeg(continuedA, leadA, first.Prefab, end: false);
                AddLeg(continuedB, leadB, last.Prefab, end: true);
                return true;
            }

            bool fitFirst = NativePaintCornerGeometry.TryAttachCurbLeg(hasFirst ? firstCurb : default, firstAcross, setback,
                stripeRadius, firstSection, first.Point, first.Outward, out var firstPaint, out var firstLead);
            bool fitLast = NativePaintCornerGeometry.TryAttachCurbLeg(hasLast ? lastCurb : default, lastAcross, setback,
                stripeRadius, lastSection, last.Point, last.Outward, out var lastPaint, out var lastLead);
            if (!fitFirst || !fitLast || !NativePaintCornerGeometry.TryMixedCurbCorner(firstCurb, lastCurb,
                firstAcross, lastAcross, setback, firstPaint, lastPaint, out _, out var continuous))
                return false;

            MathUtils.Divide(continuous, out firstPaint, out lastPaint, .5f);
            AddLeg(firstPaint, firstLead, first.Prefab, end: false);
            AddLeg(lastPaint, lastLead.HasValue ? MathUtils.Invert(lastLead.Value) : (Bezier4x3?)null, last.Prefab, end: true);
            return true;

            void BalanceLeads(Bezier4x3 before, Bezier4x3 after, ref Bezier4x3 a, ref Bezier4x3 b,
                ref Bezier4x3? leadA, ref Bezier4x3? leadB)
            {
                float measuredA = NativePaintCornerGeometry.Curvature(firstCurb, 1f);
                float measuredB = -NativePaintCornerGeometry.Curvature(lastCurb, 1f);
                if (measuredA * measuredB <= 0) return;
                float target = (measuredA + measuredB) * .5f;
                float minimum = stripeRadius + .01f;
                if (leadA.HasValue && NativePaintCornerGeometry.TryBalanceLeadIn(leadA.Value, a, b, before, after,
                    firstAcross, lastAcross, setback, minimum, target, out var entry, out var exit))
                {
                    a = entry;
                    b = exit;
                    leadA = null;
                }
                else if (leadB.HasValue && NativePaintCornerGeometry.TryBalanceLeadIn(MathUtils.Invert(leadB.Value),
                    MathUtils.Invert(b), MathUtils.Invert(a), after, before, lastAcross, firstAcross,
                    setback, minimum, -target, out entry, out exit))
                {
                    a = MathUtils.Invert(exit);
                    b = MathUtils.Invert(entry);
                    leadB = null;
                }
            }

            void RecordCurbs(Bezier4x3 a, Bezier4x3 b)
            {
                if (curbReferences == null) return;
                curbReferences.Add(a);
                curbReferences.Add(b);
            }

            void AddLeg(Bezier4x3 paint, Bezier4x3? lead, Entity prefab, bool end)
            {
                var oriented = end ? MathUtils.Invert(paint) : paint;
                var guide = end ? lastCurb : firstCurb;
                var across = end ? lastAcross : firstAcross;
                if (!lead.HasValue && NativePaintCornerGeometry.TryEaseRoadMouth(oriented, guide, across,
                    stripeRadius + .01f, out var mouth, out var remainder))
                {
                    if (end)
                    {
                        output.Add(new MarkCurve(MathUtils.Invert(remainder), prefab, .15f, false, true, JunctionPaintOrigin.NativeCurb));
                        output.Add(new MarkCurve(MathUtils.Invert(mouth), prefab, .15f, false, true, JunctionPaintOrigin.NativeCurb));
                    }
                    else
                    {
                        output.Add(new MarkCurve(mouth, prefab, .15f, false, true, JunctionPaintOrigin.NativeCurb));
                        output.Add(new MarkCurve(remainder, prefab, .15f, false, true, JunctionPaintOrigin.NativeCurb));
                    }
                    return;
                }
                var leg = new MarkCurve(paint, prefab, .15f, false, true, JunctionPaintOrigin.NativeCurb);
                if (end) output.Add(leg);
                if (lead.HasValue) output.Add(new MarkCurve(lead.Value, prefab, .15f, false, true, JunctionPaintOrigin.RoadSectionLead));
                if (!end) output.Add(leg);
            }
        }
    }
}
