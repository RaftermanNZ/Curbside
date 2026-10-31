using System.Collections.Generic;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    internal static class RoadsideAnchorPolicy

    {

        internal static List<float3> Complete(IList<float3> authored,float width,float left,float right,float leftY,float rightY)

        {

            var anchors=new List<float3>(authored);bool hasLeft=false,hasRight=false;

            foreach(var curb in anchors) {if(curb.x<0) hasLeft=true;else if(curb.x>0) hasRight=true;}

            if(!hasLeft) anchors.Add(new float3(math.isfinite(left)&&left<float.MaxValue?left:-width*.5f,leftY,0));

            if(!hasRight) anchors.Add(new float3(math.isfinite(right)&&right>float.MinValue?right:width*.5f,rightY,0));

            return anchors;

        }

    }

}
