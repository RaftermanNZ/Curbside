using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class MedianPaintPolicy
    {
        internal static bool Inside(float x,float2 span)=>math.isfinite(x)&&math.all(math.isfinite(span))&&span.y>span.x&&x>=span.x-.03f&&x<=span.y+.03f;
    }
}
