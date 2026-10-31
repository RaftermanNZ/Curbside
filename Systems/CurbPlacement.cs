using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    internal static class CurbPlacement

    {

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
