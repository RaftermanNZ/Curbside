using Colossal.Mathematics;

using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    internal static class ParkingMarkPlacement

    {

        // Match the game's 16-sample parking-space parameterization. Using

        // a different arc-length solver can move paint away from its slots.

        internal static float SlotParameter(Bezier4x3 curve, float distance)

        {

            if (!math.isfinite(distance) || distance < 0f) return float.NaN;

            float travelled = 0f;

            float3 previous = curve.a;

            for (int i = 1; i <= 16; i++)

            {

                float t = i / 16f;

                float3 next = MathUtils.Position(curve, t);

                float length = math.distance(previous, next);

                if (distance <= travelled + length)

                    return math.lerp((i - 1) / 16f, t, length > 0f ? (distance - travelled) / length : 0f);

                travelled += length; previous = next;

            }

            return 1f;

        }



        internal struct BoundaryMarker
        {
            internal float T;
            internal bool Before,After,AccessEnd;
            internal BoundaryMarker(float t,bool before,bool after){T=t;Before=before;After=after;AccessEnd=false;}
        }

        // A terminal is determined by adjacent available bays, never by the first
        // or last marker that happens to survive visual clipping.
        internal static System.Collections.Generic.List<BoundaryMarker> BayMarkers(float[] parameters,bool[] available,float lower,float upper)
        {
            var result=new System.Collections.Generic.List<BoundaryMarker>();
            if(parameters==null||available==null||available.Length==0||available.Length>512||parameters.Length!=available.Length+1||!math.isfinite(lower)||!math.isfinite(upper)||lower<0||upper>1||upper<=lower)return result;
            for(int i=0;i<parameters.Length;i++)if(!math.isfinite(parameters[i])||parameters[i]<0||parameters[i]>1||i>0&&parameters[i]<=parameters[i-1])return result;
            var usable=new bool[available.Length];
            for(int i=0;i<usable.Length;i++)usable[i]=available[i]&&parameters[i]>=lower-.00001f&&parameters[i+1]<=upper+.00001f;
            for(int i=0;i<parameters.Length;i++)
            {
                bool before=i>0&&usable[i-1],after=i<usable.Length&&usable[i];
                if(before||after)result.Add(new BoundaryMarker(parameters[i],before,after){AccessEnd=i>0&&i<available.Length&&(!available[i-1]||!available[i])});
            }
            return result;
        }

        internal static bool TryBoundaryMarker(Bezier4x3 boundary,float t,bool parkingOnLeft,bool before,bool after,out Bezier4x3 arm,out Bezier4x3 stem)
        {
            arm=stem=default;
            if(!before&&!after||!math.isfinite(t)||t<0||t>1)return false;
            var point=MathUtils.Position(boundary,t);var direction=math.normalizesafe(MathUtils.Tangent(boundary,t).xz);
            if(!math.all(math.isfinite(point))||math.lengthsq(direction)<.9f)return false;
            var normal=MathUtils.Left(direction)*(parkingOnLeft?1f:-1f);
            var along=new float3(direction.x,0,direction.y)*.30f;var across=new float3(normal.x,0,normal.y)*.40f;
            arm=Straight(before?point-along:point,after?point+along:point);stem=Straight(point,point+across);return true;
        }

        internal static System.Collections.Generic.List<Bezier4x3> BuildBayStyle(Bezier4x3 boundary,float lower,float upper,
            System.Collections.Generic.List<BoundaryMarker> markers,bool parkingOnLeft,ParkingBayStyle style,float bayWidth=2f,bool closeStart=true,bool closeEnd=true,bool closeAccess=false)
        {
            var output=new System.Collections.Generic.List<Bezier4x3>();
            float length=MathUtils.Length(boundary);
            if(markers==null||!math.isfinite(length)||length<.1f||!math.isfinite(lower)||!math.isfinite(upper)||lower<0||upper>1||upper<=lower)return output;
            float runStart=float.NaN;
            foreach(var marker in markers)
            {
                if(!math.isfinite(marker.T)||marker.T<lower-.00001f||marker.T>upper+.00001f)continue;
                if(marker.After&&!marker.Before)runStart=marker.T;
                if(marker.Before&&!marker.After&&math.isfinite(runStart))
                {
                    if((style==ParkingBayStyle.BoundaryAndTs||style==ParkingBayStyle.CurbDividers)&&marker.T>runStart)output.Add(MathUtils.Cut(boundary,new float2(runStart,marker.T)));
                    runStart=float.NaN;
                }
                // Segment boundaries and building entrances do not close a parking run.
                bool terminal=!marker.Before||!marker.After;
                if(terminal&&!(closeAccess&&marker.AccessEnd)&&(!marker.Before&&!closeStart||!marker.After&&!closeEnd))continue;
                if(style==ParkingBayStyle.CurbDividers||style==ParkingBayStyle.FullDepthTs||style==ParkingBayStyle.DividersOnly)
                {
                    if(!math.isfinite(bayWidth)||bayWidth<=.3f)continue;
                    var point=MathUtils.Position(boundary,marker.T);
                    var normal=MathUtils.Left(math.normalizesafe(MathUtils.Tangent(boundary,marker.T).xz))*(parkingOnLeft?1f:-1f)*(bayWidth-.15f);
                    output.Add(Straight(point,point+new float3(normal.x,0,normal.y)));
                    if(style==ParkingBayStyle.FullDepthTs&&TryBoundaryMarker(boundary,marker.T,parkingOnLeft,marker.Before,marker.After,out var fullArm,out _))output.Add(fullArm);
                }
                else if(TryBoundaryMarker(boundary,marker.T,parkingOnLeft,marker.Before,marker.After,out var arm,out var stem))
                {
                    if(style==ParkingBayStyle.TsOnly)output.Add(arm);
                    output.Add(stem);
                }
            }
            return output;
        }

        private static Bezier4x3 Straight(float3 a, float3 d)

            => new Bezier4x3(a, math.lerp(a, d, 1f / 3f), math.lerp(a, d, 2f / 3f), d);

    }

}

