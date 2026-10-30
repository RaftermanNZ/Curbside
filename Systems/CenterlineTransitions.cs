using System;using System.Collections.Generic;using Colossal.Mathematics;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    internal static class CenterTransitionPolicy
    {
        // Widths describe stripe counts per semantic boundary, not per carriageway.
        // Sorted boundaries and stripes preserve left/right order through every fan.
        internal static List<int2> Links(int[] first,int[] last)
        {
            var result=new List<int2>();
            if(first==null||last==null||first.Length<1||first.Length>2||last.Length<1||last.Length>2)return result;
            foreach(int n in first)if(n<1||n>2)return result;foreach(int n in last)if(n<1||n>2)return result;
            if(first.Length>last.Length){foreach(var link in Links(last,first))result.Add(new int2(link.y,link.x));return result;}
            if(first.Length==last.Length)
            {
                int a=0,b=0;
                for(int group=0;group<first.Length;group++)
                {
                    int count=math.max(first[group],last[group]);
                    for(int stripe=0;stripe<count;stripe++)result.Add(new int2(a+(first[group]==1?0:stripe),b+(last[group]==1?0:stripe)));
                    a+=first[group];b+=last[group];
                }
            }
            else
            {
                int b=0;
                for(int group=0;group<last.Length;group++)
                {
                    int a=first[0]==1?0:group;
                    for(int stripe=0;stripe<last[group];stripe++)result.Add(new int2(a,b+stripe));
                    b+=last[group];
                }
            }
            return result;
        }
    }
    public partial class CurbLineEmissionSystem
    {
        private void CanonicalCenterPairs(List<MarkCurve> output)
        {
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();var result=new List<MarkCurve>();
            foreach(var mark in output){bool paired=false,yellow=false,known=false;
                if(mark.Median&&!mark.Protected&&_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(mark.Prefab,out var prefab)&&prefab?.m_Meshes!=null)foreach(var mesh in prefab.m_Meshes)if(mesh?.m_Mesh!=null){paired|=mesh.m_Mesh.name.IndexOf("Double",StringComparison.OrdinalIgnoreCase)>=0;yellow|=mesh.m_Mesh.name.IndexOf("Yellow",StringComparison.OrdinalIgnoreCase)>=0;known|=yellow||mesh.m_Mesh.name.IndexOf("White",StringComparison.OrdinalIgnoreCase)>=0;}
                var single=palette.Explicit(1,yellow);
                if(!paired||!known||single==Entity.Null||!EntityManager.HasComponent<NetLaneGeometryData>(mark.Prefab)||!EntityManager.HasComponent<NetLaneGeometryData>(single)){result.Add(mark);continue;}
                float spacing=CenterlineUpgradePolicy.HalfSpacing(EntityManager.GetComponentData<NetLaneGeometryData>(mark.Prefab).m_Size.x,EntityManager.GetComponentData<NetLaneGeometryData>(single).m_Size.x);
                for(int stripe=-1;stripe<=1;stripe+=2){var copy=mark;copy.Curve=Game.Net.NetUtils.OffsetCurveLeftSmooth(mark.Curve,stripe*spacing);copy.Prefab=single;result.Add(copy);}
            }output.Clear();output.AddRange(result);
        }
        private List<List<JoinCap>> CenterBoundaries(Entity node,List<JoinCap> caps,float3 right)
        {
            var groups=new Dictionary<int,List<JoinCap>>();
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            foreach(var cap in caps)
            {
                if(cap.Node!=node||(cap.Kind!=2&&cap.Kind!=3))continue;
                if(!groups.TryGetValue(cap.Boundary,out var group))groups[cap.Boundary]=group=new List<JoinCap>();
                bool paired=false,yellow=false,recognised=false;
                if(_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(cap.Prefab,out var prefab)&&prefab?.m_Meshes!=null)
                    foreach(var mesh in prefab.m_Meshes)if(mesh?.m_Mesh!=null){paired|=mesh.m_Mesh.name.IndexOf("Double",StringComparison.OrdinalIgnoreCase)>=0;yellow|=mesh.m_Mesh.name.IndexOf("Yellow",StringComparison.OrdinalIgnoreCase)>=0;recognised|=yellow||mesh.m_Mesh.name.IndexOf("White",StringComparison.OrdinalIgnoreCase)>=0;}
                var single=palette.Explicit(1,yellow);
                if(paired&&recognised&&single!=Entity.Null&&EntityManager.HasComponent<NetLaneGeometryData>(cap.Prefab)&&EntityManager.HasComponent<NetLaneGeometryData>(single))
                {
                    float spacing=CenterlineUpgradePolicy.HalfSpacing(EntityManager.GetComponentData<NetLaneGeometryData>(cap.Prefab).m_Size.x,EntityManager.GetComponentData<NetLaneGeometryData>(single).m_Size.x);
                    var normal=math.normalizesafe(new float3(cap.Outward.z,0,-cap.Outward.x));
                    var left=cap;left.Point-=normal*spacing;left.Prefab=single;var other=left;other.Point+=normal*spacing*2;
                    group.Add(left);group.Add(other);
                }
                else group.Add(cap);
            }
            var result=new List<List<JoinCap>>(groups.Values);
            foreach(var group in result)group.Sort((a,b)=>math.dot(a.Point,right).CompareTo(math.dot(b.Point,right)));
            result.Sort((a,b)=>math.dot((a[0].Point+a[a.Count-1].Point)*.5f,right).CompareTo(math.dot((b[0].Point+b[b.Count-1].Point)*.5f,right)));
            return result;
        }
        private bool TryCenterTransitions(Entity node,List<JoinCap> aCaps,List<JoinCap> bCaps,List<MarkCurve> output)
        {
            float3 direction=default;
            foreach(var cap in aCaps)if(cap.Node==node&&(cap.Kind==2||cap.Kind==3))direction+=cap.Outward;
            foreach(var cap in bCaps)if(cap.Node==node&&(cap.Kind==2||cap.Kind==3))direction-=cap.Outward;
            direction=math.normalizesafe(direction);if(math.lengthsq(direction.xz)<.5f)return false;
            var right=math.normalizesafe(new float3(direction.z,0,-direction.x));
            var first=CenterBoundaries(node,aCaps,right);var last=CenterBoundaries(node,bCaps,right);
            var widthsA=new int[first.Count];var widthsB=new int[last.Count];var a=new List<JoinCap>();var b=new List<JoinCap>();
            for(int i=0;i<first.Count;i++){widthsA[i]=first[i].Count;a.AddRange(first[i]);}
            for(int i=0;i<last.Count;i++){widthsB[i]=last[i].Count;b.AddRange(last[i]);}
            var links=CenterTransitionPolicy.Links(widthsA,widthsB);if(links.Count==0)return false;
            var planned=new List<MarkCurve>();
            foreach(var link in links)
            {
                var start=a[link.x];var end=b[link.y];
                if(math.distance(start.Point,end.Point)<.05f)continue;
                if(!NodeLinePlacement.CanJoin(start.Point,start.Outward,end.Point,end.Outward,3,start.Side,end.Side))return false;
                var curve=NodeLinePlacement.Connect(start.Point,start.Outward,end.Point,end.Outward);if(!Valid(curve))return false;
                if(start.Prefab==end.Prefab)planned.Add(new MarkCurve(curve,start.Prefab,0,true));
                else {planned.Add(new MarkCurve(MathUtils.Cut(curve,new float2(0,.5f)),start.Prefab,0,true));planned.Add(new MarkCurve(MathUtils.Cut(curve,new float2(.5f,1)),end.Prefab,0,true));}
            }
            output.AddRange(planned);return true;
        }
    }
}
