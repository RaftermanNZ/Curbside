using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    internal static class CurbPlacement

    {

        internal static bool TryPieceAnchor(float3 pieceOffset, float3 localAnchor, bool inverted, out float3 position)
        {
            if (inverted) localAnchor.x = -localAnchor.x;
            position = pieceOffset + localAnchor;
            return math.all(math.isfinite(position));
        }

        internal static bool TryOuterAnchor(System.Collections.Generic.IReadOnlyList<float3> anchors,
            float width, float middle, bool left, out float3 anchor)
        {
            anchor = default;
            if (!math.isfinite(width) || !math.isfinite(middle) || width <= 0f || math.abs(middle) >= width * .5f)
                return false;
            float best = 0f;
            foreach (var candidate in anchors)
            {
                if (!math.all(math.isfinite(candidate)) || math.abs(candidate.x) > width * .5f + .01f) continue;
                float distance = left ? middle - candidate.x : candidate.x - middle;
                if (distance <= best) continue;
                best = distance;
                anchor = candidate;
            }
            return best > 0f;
        }

        internal static bool TryRaisedPieceBoundary(float3 syncOffsets, float3 roadLane,
            float3 walkLane, out float offset)
        {
            offset = float.NaN;
            if (!math.all(math.isfinite(roadLane)) || !math.all(math.isfinite(walkLane))
                || walkLane.y - roadLane.y < .06f) return false;

            float low = math.min(roadLane.x, walkLane.x);
            float high = math.max(roadLane.x, walkLane.x);
            for (int i = 0; i < 3; i++)
            {
                float candidate = syncOffsets[i];
                if (!math.isfinite(candidate) || candidate <= low + .02f || candidate >= high - .02f) continue;
                if (math.isfinite(offset) && math.abs(candidate - offset) > .02f) return false;
                offset = candidate;
            }
            return math.isfinite(offset);
        }

        internal static bool TryPavementLaneBounds(float3 pieceOffset, float3 laneOffset, bool inverted,
            float laneWidth, float pieceWidth, out float3 left, out float3 right)
        {
            left = right = default;
            if (!math.isfinite(laneWidth) || !math.isfinite(pieceWidth) || laneWidth <= .1f || pieceWidth <= .1f
                || !TryPieceAnchor(pieceOffset, laneOffset, inverted, out var center)) return false;

            left = new float3(math.max(center.x - laneWidth * .5f, pieceOffset.x - pieceWidth * .5f), center.y, 0);
            right = new float3(math.min(center.x + laneWidth * .5f, pieceOffset.x + pieceWidth * .5f), center.y, 0);
            return right.x - left.x > .1f;
        }

        internal readonly struct MeshCurbFace
        {
            internal readonly float X, Bottom, Start, End, NormalX;

            internal MeshCurbFace(float x, float bottom, float start, float end, float normalX)
            {
                X = x;
                Bottom = bottom;
                Start = start;
                End = end;
                NormalX = normalX;
            }
        }

        internal static bool TryMeshCurbFace(float3 a, float3 b, float3 c, float3 normal, out MeshCurbFace face)
        {
            face = default;
            if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || !math.all(math.isfinite(c))
                || !math.all(math.isfinite(normal)) || math.abs(normal.x) < .8f || math.abs(normal.y) > .45f)
                return false;
            var low = math.min(a, math.min(b, c));
            var high = math.max(a, math.max(b, c));
            if (high.x - low.x > .045f || high.y - low.y < .055f || high.y - low.y > .35f
                || high.z - low.z < .2f) return false;
            float x = a.y <= b.y && a.y <= c.y ? a.x : b.y <= c.y ? b.x : c.x;
            face = new MeshCurbFace(x, low.y, low.z, high.z, normal.x);
            return true;
        }

        internal static bool TryMeshCurbAnchor(System.Collections.Generic.IReadOnlyList<MeshCurbFace> faces,
            float3 pieceOffset, float halfLength, bool inverted, bool left, float3 reference, out float3 anchor)
        {
            anchor = default;
            if (halfLength < .3f) return false;

            // GenerateCompositionMesh negates authored mesh X on non-inverted
            // pieces. Lane-local offsets already use the opposite convention.
            float sign = inverted ? 1f : -1f;
            float towardRoad = sign * (left ? 1f : -1f);
            float best = float.PositiveInfinity;
            foreach (var face in faces)
            {
                if (face.Start > .12f) break;
                if (face.Start < -.02f || face.NormalX * towardRoad < .8f) continue;

                float x = pieceOffset.x + sign * face.X;
                float difference = math.abs(x - reference.x);
                if (difference > .75f || difference >= best
                    || math.abs(pieceOffset.y + face.Bottom - reference.y) > .15f) continue;

                float covered = face.End;
                // A deformed curb often has several longitudinal cuts. Require
                // continuous coverage by faces at the same authored curb foot.
                foreach (var next in faces)
                {
                    if (next.Start > covered + .06f) break;
                    if (next.Start >= -.02f && next.NormalX * towardRoad >= .8f
                        && math.abs(next.X - face.X) <= .025f && math.abs(next.Bottom - face.Bottom) <= .04f)
                        covered = math.max(covered, next.End);
                }
                if (covered < halfLength * .8f) continue;
                best = difference;
                anchor = new float3(x, reference.y, reference.z);
            }
            return math.isfinite(best);
        }

        internal static bool TryMapBoundary(float width, float3 anchor, out float3 position, out float fraction)
        {
            position = default;
            fraction = 0;
            if (!math.isfinite(width) || width <= .1f || !math.all(math.isfinite(anchor))) return false;
            float halfWidth = width * .5f;
            if (math.abs(anchor.x) > halfWidth + .01f) return false;
            position = anchor;
            position.x = math.clamp(anchor.x, -halfWidth, halfWidth);
            position.y += .01f;
            fraction = position.x / width + .5f;
            return true;
        }

        internal static bool TryCrossingMedianBounds(float roadWidth, float middle, float syncLeft,
            float syncRight, float2 pieceBounds, out float2 median)
        {
            median = default;
            float halfWidth = roadWidth * .5f;
            if (!math.isfinite(halfWidth) || !math.all(math.isfinite(pieceBounds))
                || pieceBounds.y - pieceBounds.x < .15f
                || pieceBounds.x <= -halfWidth || pieceBounds.y >= halfWidth) return false;
            if (TryMedianBoundaries(roadWidth, middle, syncLeft, syncRight, -halfWidth, halfWidth, out var synced)
                && synced.x >= pieceBounds.x - .2f && synced.y <= pieceBounds.y + .2f)
                median = synced;
            else
                median = pieceBounds;
            return true;
        }

        internal static bool TryMedianBoundaries(float width, float middle, float leftSync, float rightSync,
            float outerLeft, float outerRight, out float2 bounds)
        {
            float halfWidth = width * .5f;
            bounds = new float2(math.lerp(-halfWidth, middle, leftSync), math.lerp(middle, halfWidth, rightSync));
            return math.all(math.isfinite(bounds)) && bounds.y - bounds.x >= .15f
                && bounds.x > outerLeft + .05f && bounds.y < outerRight - .05f;
        }

        internal static bool TryParkingCurb(float3 pieceOffset, float localCurb, float localRoadHeight,

            bool inverted, out float3 curb)

        {

            curb = default(float3);

            if (!math.all(math.isfinite(pieceOffset)) || !math.isfinite(localCurb) || !math.isfinite(localRoadHeight)) return false;

            curb = new float3(pieceOffset.x + (inverted ? -localCurb : localCurb), pieceOffset.y + localRoadHeight, 0f);

            return true;

        }



        internal static bool TryMapComposed(float roadWidth, float3 authoredCurb, bool left,

            out float3 position, out float fraction)

            => TryMapComposed(roadWidth, authoredCurb, left, 0.15f, out position, out fraction);



        internal static bool TryMapComposed(float roadWidth, float3 authoredCurb, bool left, float offset,

            out float3 position, out float fraction)

        {

            position = default(float3);

            fraction = 0f;

            if (!math.isfinite(offset) || offset < 0f || !math.isfinite(roadWidth) || roadWidth <= 0.1f || !math.all(math.isfinite(authoredCurb))) return false;

            position = authoredCurb;

            position.x += left ? offset : -offset;

            position.y += 0.01f;

            fraction = position.x / roadWidth + 0.5f;

            return math.isfinite(fraction) && fraction > 0f && fraction < 1f;

        }



        internal static bool TryMapDecal(float roadWidth, float3 curb, bool left, float offset, out float3 position, out float fraction)

        {

            position = default; fraction = 0;

            if (!math.isfinite(roadWidth) || roadWidth <= .1f || !math.all(math.isfinite(curb)) || !math.isfinite(offset)) return false;

            position = curb; position.x += left ? offset : -offset; position.y += .01f;

            fraction = position.x / roadWidth + .5f;

            return math.isfinite(fraction);

        }

        internal static bool TryMap(float roadWidth, float pieceWidth, float3 pieceOffset,

            float3 curbAnchor, bool inverted, out float3 position, out float fraction)

        {

            position = default(float3);

            fraction = 0f;

            if (!math.isfinite(roadWidth) || roadWidth <= 0.1f || !math.isfinite(pieceWidth) || pieceWidth <= 0f

                || !math.all(math.isfinite(pieceOffset)) || !math.all(math.isfinite(curbAnchor))

                || math.abs(curbAnchor.x + pieceWidth * 0.5f) > 0.05f) return false;

            float direction = inverted ? -1f : 1f;

            position = new float3(pieceOffset.x + direction * (curbAnchor.x - 0.15f),

                pieceOffset.y + curbAnchor.y + 0.01f, 0f);

            fraction = position.x / roadWidth + 0.5f;

            return math.isfinite(fraction) && fraction > 0f && fraction < 1f;

        }

    }

}
