using System.Collections.Generic;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class RoadsideAnchorPolicy

    {

        internal static List<float3> Complete(IList<float3> authored,float width,float left,float right,float leftY,float rightY,
            float middle=0, bool allowSectionBounds=true)

        {

            var anchors=new List<float3>(authored);bool hasLeft=false,hasRight=false;

            foreach(var curb in anchors) {if(curb.x<middle) hasLeft=true;else if(curb.x>middle) hasRight=true;}

            bool validLeft = math.isfinite(left) && left < middle;
            bool validRight = math.isfinite(right) && right > middle;
            if(!hasLeft && (validLeft || allowSectionBounds)) anchors.Add(new float3(validLeft?left:-width*.5f,leftY,0));
            if(!hasRight && (validRight || allowSectionBounds)) anchors.Add(new float3(validRight?right:width*.5f,rightY,0));

            return anchors;

        }

    }

}
