using System;

using System.Collections.Generic;

using Colossal.Serialization.Entities;

using Colossal.Mathematics;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Serialization;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

using ObjectTransform = Game.Objects.Transform;

using ObjectLight = Game.Objects.TrafficLight;

using ObjectChild = Game.Objects.SubObject;

namespace MedianGrassReplacement.Systems

{

    internal static class FarSideLightPolicy

    {

        internal struct Approach { internal float3 Left,Right; internal float3 JunctionCentre;internal bool HasJunctionCentre; internal float2 Outward; internal List<float3> Incoming;internal List<float3> WireBank;internal List<Bezier4x3> WireCurbs;internal WireHeadLayout WireHeads;internal List<ushort> LaneMasks;internal Entity Road;internal ushort VehicleMask;internal SignalJunctionFootprint Pavement; internal SignalJunctionFootprint Sidewalk; }
        internal static bool OwnsPose(Entity current,Entity native,Entity applied,bool samePose)
            =>(current==applied&&applied!=native)||(current==native&&samePose);
        internal static bool CanRestorePose(Entity owner, Entity expectedOwner,
            Entity current, Entity native, Entity applied, bool samePose)
            => owner == expectedOwner && samePose
                && (current == native || current == applied);
        internal static bool TrySignalMount(ObjectTransform source,IList<Approach> roads,out ObjectTransform placed)
            =>TrySignalMountFrom(source,roads,GardenCityLightPolicy.Nearest(source,roads),out placed);
        internal static bool TrySignalMountFrom(ObjectTransform source,IList<Approach> roads,int origin,out ObjectTransform placed)
        {
            if(!TryMoveForApproach(source,roads,origin,out placed))return false;
            if(origin<0)return false;var from=roads[origin];
            if(GardenCityLightPolicy.TryLayout(from.Incoming,from.Outward,placed.m_Position,out _,out _))return true;
            // A narrower opposite road can put the old bank inside the incoming
            // lane envelope. Use the other far curb, never aim an arm over grass.
            int target=-1;float score=.7f;
            for(int i=0;i<roads.Count;i++)if(i!=origin){float opposite=-math.dot(from.Outward,roads[i].Outward);if(opposite>score){score=opposite;target=i;}}
            if(target<0)return false;var to=roads[target];var centre=(to.Left.xz+to.Right.xz)*.5f;
            foreach(var curb in new[]{to.Left,to.Right})
            {
                var candidate=source;candidate.m_Position=curb;candidate.m_Position.xz+=math.normalizesafe(curb.xz-centre)*.6f;
                if(IsFarSide(candidate.m_Position,roads,origin)&&GardenCityLightPolicy.TryLayout(from.Incoming,from.Outward,candidate.m_Position,out _,out _)){placed=candidate;return true;}
            }
            return false;
        }

        internal static bool TryAppearanceSignalMountFrom(ObjectTransform source,IList<Approach> roads,int origin,byte style,out ObjectTransform placed)
            =>TrySignalMountFrom(source,roads,origin,out placed);
        internal static bool TryAppearanceArmMountFrom(ObjectTransform source,IList<Approach> roads,int origin,IList<float3> occupied,byte style,out ObjectTransform placed)
            =>TryArmMountFrom(source,roads,origin,occupied,out placed);
        internal static bool TryMove(ObjectTransform source, IList<Approach> approaches, out ObjectTransform result)
            =>TryMoveForApproach(source,approaches,-1,out result);

        // Arms are fitted to the controlled incoming lanes, not the total width
        // or a road's prefab name. Wire supports retain their separate corner plan.
        internal static bool TryArmMountFrom(ObjectTransform source,IList<Approach> roads,int origin,IList<float3> occupied,out ObjectTransform placed)
        {
            placed=source;
            if(roads==null||origin<0||origin>=roads.Count||!math.all(math.isfinite(source.m_Position)))return false;
            var from=roads[origin];int target=-1;float alignment=.7f;
            for(int i=0;i<roads.Count;i++)if(i!=origin){float score=-math.dot(from.Outward,roads[i].Outward);if(score>alignment){alignment=score;target=i;}}
            var candidates=new List<ObjectTransform>(roads.Count*2+3);
            if(TrySignalMountFrom(source,roads,origin,out var original))candidates.Add(original);
            if(target>=0)
            {
                var to=roads[target];var centre=(to.Left.xz+to.Right.xz)*.5f;
                foreach(var curb in new[]{to.Left,to.Right})
                {
                    var candidate=source;candidate.m_Position=curb;
                    candidate.m_Position.xz+=math.normalizesafe(curb.xz-centre)*.6f;
                    candidates.Add(candidate);
                }
            }
            else
            {
                var centre=(from.Left.xz+from.Right.xz)*.5f;
                foreach(var curb in new[]{from.Left,from.Right})
                {
                    var bank=source;bank.m_Position=curb;bank.m_Position.xz+=math.normalizesafe(curb.xz-centre)*.6f;
                    if(TryAcrossBoundary(bank,roads,origin,.6f,out var opposite))candidates.Add(opposite);
                }
            }
            // Skew and multi-leg junctions need every real curb bank considered,
            // not just a guessed opposite road. Never invent a rectangular corner.
            foreach(var road in roads)foreach(var curb in new[]{road.Left,road.Right})
            {
                var candidate=source;candidate.m_Position=curb;
                candidate.m_Position.xz+=math.normalizesafe(curb.xz-(road.Left.xz+road.Right.xz)*.5f)*.6f;
                var mouth=(from.Left.xz+from.Right.xz)*.5f;
                if(math.dot(candidate.m_Position.xz-mouth,from.Outward)>-2f)continue;
                candidates.Add(candidate);
            }
            float best=float.MaxValue,bestCost=float.MaxValue;bool found=false,bestRight=false;
            foreach(var candidate in candidates)
            {
                if(!RoadsideArmCandidate(candidate.m_Position,roads,origin,occupied,out float reach))continue;
                // Retain the first bank for effectively equal reaches, avoiding
                // arbitrary flips from tiny geometry differences after an edit.
                bool right=DriverRightBank(candidate.m_Position,from);
                float cost=ArmBankCost(reach,right);
                if(found&&(cost>bestCost+.05f||(math.abs(cost-bestCost)<=.05f&&(!right&&bestRight||right==bestRight&&reach>=best-.05f))))continue;
                best=reach;bestCost=cost;bestRight=right;placed=candidate;found=true;
            }
            return found;
        }
        // Primary supports must remain beyond the junction for every approach
        // they serve. Reach alone is lateral and cannot distinguish near/far side.
        internal static int OppositeRoad(IList<Approach> roads, int origin)
        {
            if (roads == null || origin < 0 || origin >= roads.Count) return -1;
            var direction = math.normalizesafe(-roads[origin].Outward);
            float alignment = .7f;
            for (int i = 0; i < roads.Count; i++) if (i != origin)
                alignment = math.max(alignment, math.dot(direction, math.normalizesafe(roads[i].Outward)));
            if (alignment <= .7f) return -1;
            float depth = float.MaxValue;
            for (int i = 0; i < roads.Count; i++) if (i != origin &&
                math.dot(direction, math.normalizesafe(roads[i].Outward)) >= alignment - .0001f)
                depth = math.min(depth, math.min(math.dot(roads[i].Left.xz, direction), math.dot(roads[i].Right.xz, direction)));
            int best = -1; float side = float.MinValue;
            for (int i = 0; i < roads.Count; i++) if (i != origin)
            {
                var outward = math.normalizesafe(roads[i].Outward);
                if (math.dot(direction, outward) < alignment - .0001f) continue;
                float near = math.min(math.dot(roads[i].Left.xz, direction), math.dot(roads[i].Right.xz, direction));
                if (near > depth + .001f) continue;
                float turn = direction.x * outward.y - direction.y * outward.x;
                bool stableRoad = best >= 0 && roads[i].Road != Entity.Null && roads[best].Road != Entity.Null;
                if (best < 0 || (stableRoad ? roads[i].Road.Index < roads[best].Road.Index ||
                    roads[i].Road.Index == roads[best].Road.Index && roads[i].Road.Version < roads[best].Road.Version : turn > side))
                { best = i; side = turn; }
            }
            return best;
        }

        internal static bool IsFarSide(float3 position,IList<Approach> roads,int origin)
        {
            if(roads==null||origin<0||origin>=roads.Count||!IsRoadside(position,roads))return false;
            var from=roads[origin];var direction=math.normalizesafe(-from.Outward);
            if(!math.all(math.isfinite(direction))||math.lengthsq(direction)<.5f)return false;
            float2 centre;
            if(from.HasJunctionCentre)
            {
                centre=from.JunctionCentre.xz;if(!math.all(math.isfinite(centre)))return false;
            }
            else
            {
                // Geometry-only fixtures and older callers: intersect the actual
                // mouth centre-lines. Never use the average of mixed-width mouths.
                float xx=0,xy=0,yy=0,bx=0,by=0;centre=float2.zero;
                foreach(var road in roads)
                {
                    var outward=math.normalizesafe(road.Outward);var mouth=(road.Left.xz+road.Right.xz)*.5f;
                    if(!math.all(math.isfinite(outward))||!math.all(math.isfinite(mouth))||math.lengthsq(outward)<.5f)return false;
                    var normal=new float2(-outward.y,outward.x);float projection=math.dot(normal,mouth);
                    xx+=normal.x*normal.x;xy+=normal.x*normal.y;yy+=normal.y*normal.y;bx+=normal.x*projection;by+=normal.y*projection;
                    centre+=mouth;
                }
                float determinant=xx*yy-xy*xy;
                centre=math.abs(determinant)>.0001f?new float2((yy*bx-xy*by)/determinant,(xx*by-xy*bx)/determinant):centre/math.max(1,roads.Count);
            }
            // Small geometric tolerance, not a real-world clearance specification.
            if(math.dot(position.xz-centre,direction)<.25f)return false;
            var sourceMouth=(from.Left.xz+from.Right.xz)*.5f;
            if(!math.all(math.isfinite(sourceMouth))||math.dot(position.xz-sourceMouth,direction)<2f)return false;
            int opposite=-1;float alignment=.7f;
            for(int i=0;i<roads.Count;i++)if(i!=origin)
            {
                float score=math.dot(direction,math.normalizesafe(roads[i].Outward));
                if(score>alignment){alignment=score;opposite=i;}
            }
            if(opposite>=0)
            {
                var far=roads[opposite];
                // Use the nearer end of the real far mouth on skew junctions.
                float depth=math.min(math.dot(far.Left.xz-centre,direction),math.dot(far.Right.xz-centre,direction));
                if(!math.isfinite(depth)||math.dot(position.xz-centre,direction)<depth-.75f)return false;
            }
            else
            {
                var probe=new ObjectTransform{m_Position=position};
                probe.m_Position.xz+=direction*math.dot(sourceMouth-position.xz,direction);
                if(TryAcrossBoundary(probe,roads,origin,0,out var boundary)&&math.dot(position.xz-boundary.m_Position.xz,direction)<-.75f)return false;
            }
            return true;
        }
        internal static bool RoadsideArmCandidate(float3 position,IList<Approach> roads,int origin,IList<float3> occupied,out float reach)
        {
            reach=0;return IsFarSide(position,roads,origin)&&ArmCandidate(position,roads[origin],occupied,out reach);
        }
        internal static bool CanSharePole(float3 position,IList<Approach> roads,int first,int second,out float reach)
        {
            reach=0;if(first==second)return false;
            return RoadsideArmCandidate(position,roads,first,null,out _)&&RoadsideArmCandidate(position,roads,second,null,out reach);
        }
        internal static bool IsRoadside(float3 position,IList<Approach> roads)
        {
            if(roads==null||roads.Count<2||!math.all(math.isfinite(position)))return false;
            foreach(var road in roads)
            {
                var outward=math.normalizesafe(road.Outward);var mouth=(road.Left.xz+road.Right.xz)*.5f;
                if(!math.all(math.isfinite(outward))||!math.all(math.isfinite(mouth))||math.lengthsq(outward)<.5f)return false;
                var lateral=new float2(-outward.y,outward.x);var delta=position.xz-mouth;
                float left=math.dot(road.Left.xz-mouth,lateral),right=math.dot(road.Right.xz-mouth,lateral),offset=math.dot(delta,lateral);
                // Beyond any mouth, reject its entire road envelope, including
                // medians. A fifth leg must not turn another leg's curb into a pole site.
                if(math.dot(delta,outward)>=-.75f&&offset>math.min(left,right)-.2f&&offset<math.max(left,right)+.2f)return false;
            }
            return true;
        }
        // Driver-relative, not screen-relative or tied to the opposite road's
        // nominal left/right curb labels. Mixed widths can put either curb
        // inside the projected incoming lane envelope.
        internal static bool DriverRightBank(float3 position,Approach approach)
        {
            if(!math.all(math.isfinite(position))||approach.Incoming==null||approach.Incoming.Count==0)return false;
            var right=math.normalizesafe(new float2(-approach.Outward.y,approach.Outward.x));
            if(!math.all(math.isfinite(right))||math.lengthsq(right)<.5f)return false;
            foreach(var lane in approach.Incoming)
                if(!math.all(math.isfinite(lane))||math.dot(position.xz-lane.xz,right)<.349f)return false;
            return true;
        }
        // Three metres is our layout preference (about one ordinary lane),
        // not a regulatory requirement. Compare actual available arm sizes so
        // a tiny curb adjustment cannot silently grow the mesh by a whole step.
        internal static float ArmBankCost(float reach,bool right)
        {
            int size=GardenCityLightPolicy.Size(reach);
            return size<0?float.PositiveInfinity:GardenCityLightPolicy.Reaches[size]+(right?0:3f);
        }
        internal static bool AcceptArmAdjustment(float3 current,float3 candidate,Approach approach,float currentReach,float candidateReach,float allowance)
        {
            if(!math.isfinite(currentReach)||!math.isfinite(candidateReach))return false;
            bool wasRight=DriverRightBank(current,approach),isRight=DriverRightBank(candidate,approach);
            float oldCost=ArmBankCost(currentReach,wasRight),newCost=ArmBankCost(candidateReach,isRight);
            if(!math.isfinite(oldCost)||!math.isfinite(newCost))return false;
            if(wasRight&&!isRight)return newCost<oldCost-.05f;
            if(wasRight!=isRight)return newCost<=oldCost+.05f;
            return newCost<=oldCost+allowance;
        }
        internal static bool ArmCandidate(float3 position,Approach approach,IList<float3> occupied,out float reach)
        {
            reach=0;if(!math.all(math.isfinite(position)))return false;
            if(occupied!=null)foreach(var pole in occupied)if(math.distancesq(pole.xz,position.xz)<1.5625f)return false;
            if(!GardenCityLightPolicy.TryLayout(approach.Incoming,approach.Outward,position,out _,out var heads))return false;
            reach=heads[heads.Length-1]+.35f;
            return GardenCityLightPolicy.Size(reach)>=0;
        }
        internal static bool TryMoveForApproach(ObjectTransform source,IList<Approach> approaches,int forcedOrigin,out ObjectTransform result)

        {

            result=source;if(approaches==null || approaches.Count<2 || !math.all(math.isfinite(source.m_Position))) return false;

            // Identify the source mouth by its closest curb, rather than node symmetry.

            int origin=-1;float closest=float.MaxValue;bool originLeft=false;

            for(int i=0;i<approaches.Count;i++)

            {

                var road=approaches[i];

                float left=math.distancesq(source.m_Position.xz,road.Left.xz),right=math.distancesq(source.m_Position.xz,road.Right.xz);

                float distance=math.min(left,right);

                if(distance<closest) {closest=distance;origin=i;originLeft=left<right;}

            }

            if(forcedOrigin>=0&&forcedOrigin<approaches.Count){origin=forcedOrigin;float left=math.distancesq(source.m_Position.xz,approaches[origin].Left.xz),right=math.distancesq(source.m_Position.xz,approaches[origin].Right.xz);closest=math.min(left,right);originLeft=left<right;}
            if(origin<0 || closest>144f) return false;

            var from=approaches[origin];int target=-1;float alignment=.7f;

            for(int i=0;i<approaches.Count;i++) if(i!=origin)

            {

                float opposite=-math.dot(from.Outward,approaches[i].Outward);

                if(opposite>alignment) {alignment=opposite;target=i;}

            }

            var sourceCurb=originLeft?from.Left:from.Right;

            float2 fromCentre=(from.Left.xz+from.Right.xz)*.5f;

            float2 side=new float2(-from.Outward.y,from.Outward.x);

            if(math.dot(side,sourceCurb.xz-fromCentre)<0) side=-side;

            float2 delta=source.m_Position.xz-sourceCurb.xz;

            float nativeClearance=math.dot(delta,side);

            // Median-mounted or otherwise unrelated signals must not be guessed onto a sidewalk.

            if(nativeClearance<-.5f || nativeClearance>4f) return false;

            float clearance=math.max(nativeClearance,.25f);

            float setback=math.clamp(math.dot(delta,from.Outward),0f,4f);

            if(target<0) return TryAcrossBoundary(source,approaches,origin,clearance,out result)&&IsFarSide(result.m_Position,approaches,origin);

            var to=approaches[target];

            // Retain the same physical side of the junction when choosing the opposite curb.

            float2 centre=(to.Left.xz+to.Right.xz)*.5f;

            bool targetLeft=math.dot(to.Left.xz-centre,side)>math.dot(to.Right.xz-centre,side);

            float3 curb=targetLeft?to.Left:to.Right;

            float2 outside=new float2(-to.Outward.y,to.Outward.x);

            if(math.dot(outside,curb.xz-centre)<0) outside=-outside;

            result.m_Position.xz=curb.xz+outside*clearance+to.Outward*setback;

            return math.all(math.isfinite(result.m_Position)) && math.distance(source.m_Position.xz,result.m_Position.xz)<120f && IsFarSide(result.m_Position,approaches,origin);

        }

        private static float Cross(float2 a,float2 b) => a.x*b.y-a.y*b.x;

        private static bool TryAcrossBoundary(ObjectTransform source,IList<Approach> roads,int origin,float clearance,out ObjectTransform result)

        {

            result=source;var from=roads[origin];float2 direction=-from.Outward;

            float best=120f;float2 chosen=default;bool found=false;

            // At a T-junction the opposite sidewalk joins the two through-road

            // mouths. Reconstruct that bank from their authored curb endpoints.

            for(int a=0;a<roads.Count;a++) if(a!=origin) for(int b=a+1;b<roads.Count;b++) if(b!=origin)

            {

                var first=roads[a];var last=roads[b];

                if(math.dot(first.Outward,last.Outward)>-.7f || math.abs(math.dot(first.Outward,from.Outward))>.75f || math.abs(math.dot(last.Outward,from.Outward))>.75f) continue;

                float3 left=math.dot(first.Left.xz,direction)>math.dot(first.Right.xz,direction)?first.Left:first.Right;

                float3 right=math.dot(last.Left.xz,direction)>math.dot(last.Right.xz,direction)?last.Left:last.Right;

                float handle=math.distance(left.xz,right.xz)/3f;

                if(handle<.5f || handle>40f) continue;

                var curve=new Bezier4x3(left,left-new float3(first.Outward.x,0,first.Outward.y)*handle,right-new float3(last.Outward.x,0,last.Outward.y)*handle,right);

                float2 previous=left.xz;

                for(int i=1;i<=32;i++)

                {

                    float2 next=MathUtils.Position(curve,i/32f).xz;float2 edge=next-previous;float denominator=Cross(direction,edge);

                    if(math.abs(denominator)>.0001f)

                    {

                        float2 delta=previous-source.m_Position.xz;float distance=Cross(delta,edge)/denominator;float u=Cross(delta,direction)/denominator;

                        if(distance>1f && distance<best && u>=0 && u<=1)

                        {

                            float2 normal=math.normalizesafe(new float2(-edge.y,edge.x));if(math.dot(normal,direction)<0) normal=-normal;

                            if(math.dot(normal,direction)>.5f) {chosen=source.m_Position.xz+direction*distance+normal*clearance;best=distance;found=true;}

                        }

                    }

                    previous=next;

                }

            }

            if(!found) return false;result.m_Position.xz=chosen;return math.all(math.isfinite(result.m_Position));

        }

        // Fit the connected road surfaces, retaining the original pole mounting offset.

        // Sampling the terrain underneath a bridge would put its signals on the ground.

        internal static bool TryGround(float3 from, float3 to, IList<float3> samples, out float height)

        {

            height = from.y;

            if (samples == null || samples.Count < 3 || !math.all(math.isfinite(from)) || !math.all(math.isfinite(to))) return false;

            double mx = 0, my = 0, mz = 0;

            foreach (var sample in samples) { if (!math.all(math.isfinite(sample))) return false; mx += sample.x; my += sample.y; mz += sample.z; }

            mx /= samples.Count; my /= samples.Count; mz /= samples.Count;

            double xx = 0, xz = 0, zz = 0, xy = 0, zy = 0;

            foreach (var sample in samples)

            {

                double x = sample.x - mx, z = sample.z - mz, y = sample.y - my;

                xx += x * x; xz += x * z; zz += z * z; xy += x * y; zy += z * y;

            }

            double determinant = xx * zz - xz * xz;

            if (determinant < .0001) return false;

            double slopeX = (xy * zz - zy * xz) / determinant, slopeZ = (zy * xx - xy * xz) / determinant;

            if (Math.Abs(slopeX) > 1 || Math.Abs(slopeZ) > 1) return false;

            height = from.y + (float)(slopeX * (to.x - from.x) + slopeZ * (to.z - from.z));

            return math.isfinite(height) && math.abs(height - from.y) <= 20;

        }

        internal static bool Vehicle(TrafficLightType type) => (type & (TrafficLightType.VehicleLeft | TrafficLightType.VehicleRight)) != 0;
        internal static bool TryCurbMount(ObjectTransform source,IList<Approach> roads,out ObjectTransform mounted)
        {
            mounted=source;int nearest=GardenCityLightPolicy.Nearest(source,roads);if(nearest<0)return false;
            var road=roads[nearest];bool left=math.distancesq(source.m_Position.xz,road.Left.xz)<math.distancesq(source.m_Position.xz,road.Right.xz);var curb=left?road.Left:road.Right;
            var centre=(road.Left.xz+road.Right.xz)*.5f;var outside=math.normalizesafe(curb.xz-centre);
            float clearance=math.dot(source.m_Position.xz-curb.xz,outside);if(clearance<-.5f||clearance>4f)return false;
            mounted.m_Position.xz+=outside*math.max(0,.6f-clearance);return math.all(math.isfinite(mounted.m_Position));
        }

    }

    public struct MGRSignalLuminaireRuntime:IComponentData{}
    public partial class FarSideLightSystem : GameSystemBase, IPreSerialize

    {

        private struct Moved { internal Entity Node, Owner, Prefab, AppliedPrefab; internal ObjectTransform Original, Applied; internal ushort OriginalPedMask,AppliedPedMask,AppliedVehicleMask; internal bool HadRelative; internal Game.Objects.Relative OriginalRelative; }
        private readonly Dictionary<Entity, Moved> _moved = new Dictionary<Entity, Moved>();
        // Managed scalar snapshots only, scoped to one maintenance pass. No ECS
        // views or lookups survive the prop restoration/retirement operations.
        private readonly Dictionary<Entity,byte> _maintenanceNodes = new Dictionary<Entity,byte>();
        private readonly Dictionary<Entity,Entity> _hiddenSignals=new Dictionary<Entity,Entity>();
        private struct Suppressed {internal Entity Original,Applied;internal ushort VehicleMask,PedestrianMask;}
        private readonly Dictionary<Entity,Suppressed> _suppressedPrefabs=new Dictionary<Entity,Suppressed>();
        private readonly Dictionary<Entity,Entity[]> _generatedLamps=new Dictionary<Entity,Entity[]>();
        private EntityQuery _lampTemplates;private Entity _lampDonor;
        private readonly Dictionary<Entity,Game.Objects.StreetLight> _originalLampStates=new Dictionary<Entity,Game.Objects.StreetLight>();
        private float _nextLampState;
        private readonly Dictionary<Entity,Moved> _lamps=new Dictionary<Entity,Moved>();
        private readonly Dictionary<Entity,ObjectTransform> _plannedSignals=new Dictionary<Entity,ObjectTransform>();
        private readonly Dictionary<Entity,int> _sourceApproaches=new Dictionary<Entity,int>();
        private readonly List<Entity> _primarySignals = new List<Entity>();
        private readonly Dictionary<Entity,Entity> _supportLeaders = new Dictionary<Entity,Entity>();
        private readonly HashSet<Entity> _sharedPhaseSignals = new HashSet<Entity>();
        private int _virtualSupport = -1;
        private int SourceApproach(Entity prop,IList<FarSideLightPolicy.Approach> roads)
        {
            ushort mask=EntityManager.HasComponent<ObjectLight>(prop)?EntityManager.GetComponentData<ObjectLight>(prop).m_GroupMask0:(ushort)0;
            return SignalSourceAssociationPolicy.Find(OriginalTransform(prop),roads,mask);
        }
        private readonly List<float3> _supportPositions=new CornerSupportList();
        private readonly List<int2> _requiredWireSpans=new List<int2>();
        private readonly HashSet<Entity> _visibleSignals=new HashSet<Entity>();

        private static readonly HashSet<Entity> Requested = new HashSet<Entity>();
        private readonly Queue<Entity> _reconcileNodes=new Queue<Entity>();private readonly HashSet<Entity> _reconcileSet=new HashSet<Entity>();private float _nextReconcile;

        internal static void Queue(Entity node) { Requested.Add(node); }

        private EntityQuery _upgrades;

        private bool _active, _initial = true;

        private readonly HashSet<Entity> _nodeWork = new HashSet<Entity>();

        private readonly List<Entity> _dead = new List<Entity>(), _props = new List<Entity>();
        private readonly List<Entity> _retiredLampNodes = new List<Entity>();

        protected override void OnCreate()

        {

            base.OnCreate();
            _lampTemplates=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<StreetLightData>(),ComponentType.ReadOnly<ObjectData>());

            _upgrades = GetEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<MGRFarSideLights>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            InitializeNativeSignalEvents();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _active = false; junctionFootprints.Clear(); sidewalkFootprints.Clear(); Restore(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode) { base.OnGameLoadingComplete(purpose, mode); ClearSignalLayouts(); nativeSignalChildren.BeginPass(); waitingSignalDefinitions.Clear();signalPresentationChecks.Clear(); _moved.Clear(); maintenance.Clear(); signalTickets.Clear(); ClearLampTracking(); Requested.Clear(); _active = (mode & GameMode.GameOrEditor) != GameMode.None; _initial = true; }

        protected override void OnDestroy() { Restore(); base.OnDestroy(); }

        private bool Live(Entity e) => EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e) && !EntityManager.HasComponent<Game.Tools.Temp>(e);

        private bool EnabledNode(Entity node) => Live(node) && EntityManager.HasComponent<MGRFarSideLights>(node) && EntityManager.HasComponent<TrafficLights>(node);

        internal void RecordSnapshot()
        {
            Dependency.Complete();
            Mod.Log.Info("Signal layout reuse snapshot: geometryReads="+networkGeometryReads+" geometryHits="+networkGeometryHits+
                " nativeNodeEvents="+nativeNodeEvents+" nativeRoadEvents="+nativeRoadEvents+" nativeActorEvents="+nativeActorEvents+" pendingOwnerChildren="+nativeSignalChildren.Count+" waitingDefinitions="+waitingSignalDefinitions.Count+" presentationReviews="+signalPresentationReviews+" lampRoadChecks="+lampRoadChecks+" lampStateChanges="+lampStateChanges+" maintenanceChecks="+maintenanceChecks+" recoveryChecks="+recoveryChecks+" unchangedNotifications="+unchangedSignalNotifications+" trackedActors="+maintenance.Count+" wirePlansBuilt="+wirePlansBuilt+" wirePlansReused="+wirePlansReused+" cachedNodes="+signalLayouts.Count+" lastResult="+lastLayoutResult);
            foreach(var cached in signalLayouts)Mod.Log.Info("Signal cached layout snapshot: node="+cached.Key+" supports="+(cached.Value.Wire?.Supports.Length??0)+
                " result="+(cached.Value.WireFailure??(cached.Value.Wire==null?"waiting":"1.5-metre corner layout")));
            // Explicit snapshot only: join native readers before inspecting
            // rendering buffers. No additional ordinary update work is added.
            NativeVisualInvalidation.CompleteReaders(World);
            int mastReports = 0;
            RecordMastCollisionSnapshot();
            RecordSignalCurbReaderSnapshot();
            var nativeEffects=World.GetExistingSystemManaged<Game.Effects.EffectControlSystem>();
            if(nativeEffects!=null)
            {
                var enabled=nativeEffects.GetEnabledData(true,out var jobs);jobs.Complete();int count=0;
                foreach(var effect in enabled)
                {
                    if(!_lamps.ContainsKey(effect.m_Owner)&&(!EntityManager.Exists(effect.m_Owner)||!EntityManager.HasComponent<MGRSignalLuminaireRuntime>(effect.m_Owner)))continue;
                    string color=EntityManager.HasComponent<EffectColorData>(effect.m_Prefab)?EntityManager.GetComponentData<EffectColorData>(effect.m_Prefab).m_Color.ToString():"missing";
                    Mod.Log.Info("Live lamp effect snapshot: owner="+effect.m_Owner+" effect="+effect.m_Prefab+" flags="+effect.m_Flags+" position="+effect.m_Position+" forward="+math.rotate(effect.m_Rotation,new float3(0,0,1))+" intensity="+effect.m_Intensity+" color="+color);
                    if(++count>=128)break;
                }
                Mod.Log.Info("Live custom-fixture effect count="+count+"; snapshot only, native effect data unchanged.");
            }
            World.GetOrCreateSystemManaged<SignalPreviewSystem>().RecordSnapshot();
            var objectQuery=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ObjectTransform>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            try
            {
                var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();int reported=0;
                using(var actors=objectQuery.ToEntityArray(Allocator.Temp))foreach(var actor in actors)
                {
                    var prefab=EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;if(!EntityManager.HasComponent<ObjectGeometryData>(prefab))continue;
                    var shape=EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Size;
                    if(!math.all(math.isfinite(shape))||math.cmin(shape)>12)
                    {
                        string name=prefabs.TryGetPrefab<PrefabBase>(prefab,out var model)?model.name:prefab.ToString();
                        Mod.Log.Info("Large-object diagnostic: actor="+actor+" prefab="+name+" size="+shape+" position="+EntityManager.GetComponentData<ObjectTransform>(actor).m_Position+" generatedLamp="+EntityManager.HasComponent<MGRSignalLuminaireRuntime>(actor));
                        if(++reported>=32)break;
                    }
                }
            }finally{objectQuery.Dispose();}
            foreach(var lamp in _lamps)
            {
                if(!Live(lamp.Key))continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(lamp.Key).m_Prefab;
                Mod.Log.Info("Signal lamp snapshot: prop="+lamp.Key+" node="+lamp.Value.Node+" position="+EntityManager.GetComponentData<ObjectTransform>(lamp.Key).m_Position+" relative="+EntityManager.HasComponent<Game.Objects.Relative>(lamp.Key)+" state="+(EntityManager.HasComponent<Game.Objects.StreetLight>(lamp.Key)?EntityManager.GetComponentData<Game.Objects.StreetLight>(lamp.Key).m_State.ToString():"missing")+" effects="+(EntityManager.HasBuffer<Game.Prefabs.Effect>(prefab)?EntityManager.GetBuffer<Game.Prefabs.Effect>(prefab,true).Length:0));
            }
            foreach(var group in _generatedLamps)foreach(var lamp in group.Value)if(Live(lamp))
            {
                var prefab=EntityManager.GetComponentData<PrefabRef>(lamp).m_Prefab;
                Mod.Log.Info("Generated signal lamp snapshot: node="+group.Key+" prop="+lamp+" position="+EntityManager.GetComponentData<ObjectTransform>(lamp).m_Position+" effects="+(EntityManager.HasBuffer<Game.Prefabs.Effect>(prefab)?EntityManager.GetBuffer<Game.Prefabs.Effect>(prefab,true).Length:0));
            }
            World.GetOrCreateSystemManaged<SignalWireSystem>().RecordSnapshot();
            using(var items=_upgrades.ToEntityArray(Allocator.Temp))foreach(var node in items)
            {
                var approaches=ReadApproaches(node);string lanes="";foreach(var a in approaches)lanes+=(a.Incoming?.Count??0)+",";
                ModDiagnostics.TraceBuffer.Record("snapshot.signal","node="+node+" enabled="+EnabledNode(node)+" custom="+EntityManager.HasComponent<MGRGardenCitySignals>(node)+" approaches="+approaches.Count+" incoming="+lanes+" children="+(EntityManager.HasBuffer<ObjectChild>(node)?EntityManager.GetBuffer<ObjectChild>(node,true).Length:0)+" moved="+_moved.Count);
                foreach(var road in approaches)Mod.Log.Info("Signal snapshot bank: node="+node+" left="+road.Left+" right="+road.Right+" outward="+road.Outward+" vehicleMask="+road.VehicleMask+" lanes="+string.Join(";",road.Incoming));
                foreach(var road in approaches)
                {
                    Mod.Log.Info("Signal native pavement snapshot: node="+node+" road="+road.Road+" vertices="+(road.Pavement?.Outline.Count??0));
                    if(road.Pavement?.NativeCurves!=null)for(int c=0;c<road.Pavement.NativeCurves.Length;c++)
                    {var curve=road.Pavement.NativeCurves[c];Mod.Log.Info("Signal native curb snapshot: node="+node+" road="+road.Road+" side="+c+" a="+curve.a+" b="+curve.b+" c="+curve.c+" d="+curve.d);}
                }
                // Explicit snapshot only: collect native movements before changing
                // face counts. No extra route scan during ordinary updates.
                int routesReported=0,pedestrianRoutesReported=0;
                if(EntityManager.HasBuffer<Game.Net.SubLane>(node))foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
                {
                    var lane=entry.m_SubLane;
                    if(pedestrianRoutesReported<64&&Live(lane)&&EntityManager.HasComponent<Game.Net.PedestrianLane>(lane)&&EntityManager.HasComponent<Curve>(lane))
                    {
                        var path=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                        Mod.Log.Info("Signal pedestrian curve snapshot: node="+node+" lane="+lane+" flags="+EntityManager.GetComponentData<Game.Net.PedestrianLane>(lane).m_Flags+
                            " a="+path.a+" b="+path.b+" c="+path.c+" d="+path.d);
                        pedestrianRoutesReported++;
                    }
                    if(routesReported>=64)continue;
                    if(!Live(lane)||!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasComponent<LaneSignal>(lane)||!EntityManager.HasComponent<Curve>(lane))continue;
                    var car=EntityManager.GetComponentData<Game.Net.CarLane>(lane);var route=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    Mod.Log.Info("Signal snapshot movement: node="+node+" lane="+lane+" mask="+EntityManager.GetComponentData<LaneSignal>(lane).m_GroupMask+" flags="+car.m_Flags+" speedRaw="+car.m_SpeedLimit+" start="+route.a+" control1="+route.b+" control2="+route.c+" end="+route.d);
                    routesReported++;
                }
                if(EntityManager.HasBuffer<ObjectChild>(node))foreach(var child in EntityManager.GetBuffer<ObjectChild>(node,true))
                {
                    var prop=child.m_SubObject;if(!Live(prop)||!EntityManager.HasComponent<ObjectLight>(prop)||!EntityManager.HasComponent<PrefabRef>(prop)||!EntityManager.HasComponent<ObjectTransform>(prop))continue;
                    var prefab=EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab;var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();string name=prefabs.TryGetPrefab<StaticObjectPrefab>(prefab,out var model)?model.name:prefab.ToString();
                    Mod.Log.Info("Signal snapshot prop: node="+node+" prop="+prop+" prefab="+name+" original="+OriginalTransform(prop).m_Position+" current="+EntityManager.GetComponentData<ObjectTransform>(prop).m_Position+" hidden="+EntityManager.HasComponent<Game.Tools.Hidden>(prop)+" ownedHidden="+_hiddenSignals.ContainsKey(prop));
                    var liveBinding = EntityManager.GetComponentData<ObjectLight>(prop);
                    Mod.Log.Info("Signal snapshot assignment: prop=" + prop + " approach=" + SourceApproach(prop, approaches)
                        + " vehicleMask=" + liveBinding.m_GroupMask0 + " pedestrianMask=" + liveBinding.m_GroupMask1);
                    bool trackedPresentation = _moved.TryGetValue(prop, out var expectedPresentation);
                    bool suppressedPresentation = _suppressedPrefabs.TryGetValue(prop, out var expectedSuppression);
                    Mod.Log.Info("Signal snapshot presentation: prop=" + prop
                        + " nativeOverridden=" + EntityManager.HasComponent<Game.Common.Overridden>(prop)
                        + " tracked=" + trackedPresentation + " suppressed=" + suppressedPresentation
                        + " expectedPrefab=" + (trackedPresentation ? expectedPresentation.AppliedPrefab : suppressedPresentation ? expectedSuppression.Applied : Entity.Null)
                        + " expectedVehicleMask=" + (trackedPresentation ? expectedPresentation.AppliedVehicleMask : suppressedPresentation ? expectedSuppression.VehicleMask : 0)
                        + " expectedPedestrianMask=" + (trackedPresentation ? expectedPresentation.AppliedPedMask : suppressedPresentation ? expectedSuppression.PedestrianMask : 0));
                    RecordMastSnapshot(node, prop, model, ref mastReports);
                    if(model?.m_Meshes!=null)foreach(var part in model.m_Meshes)
                        if(part.m_Mesh!=null&&part.m_Mesh.name.StartsWith("CSGModular",StringComparison.Ordinal))Mod.Log.Info("Signal snapshot hardware: prop="+prop+" mesh="+part.m_Mesh.name+" offset="+part.m_Position+" rotation="+part.m_Rotation);
                    if(EntityManager.HasComponent<ObjectGeometryData>(prefab)){var geometry=EntityManager.GetComponentData<ObjectGeometryData>(prefab);Mod.Log.Info("Signal render bounds: prefab="+name+" layers="+geometry.m_Layers+" size="+geometry.m_Size+" minLod="+geometry.m_MinLod);}
                    if(EntityManager.HasBuffer<SubMesh>(prefab))foreach(var mesh in EntityManager.GetBuffer<SubMesh>(prefab,true))
                    {
                        if(!EntityManager.Exists(mesh.m_SubMesh)||!EntityManager.HasComponent<MeshData>(mesh.m_SubMesh)){Mod.Log.Info("Signal render dependency missing: "+mesh.m_SubMesh);continue;}
                        var data=EntityManager.GetComponentData<MeshData>(mesh.m_SubMesh);Mod.Log.Info("Signal render dependency: entity="+mesh.m_SubMesh+" layers="+data.m_AvailableLayers+" types="+data.m_AvailableTypes+" meshes="+data.m_SubMeshCount+" indices="+data.m_IndexCount+" flags="+data.m_State);
                    }
                }
            }
        }

        protected override void OnUpdate()
        {long started=ModDiagnostics.RuntimeCosts.Begin();try{UpdateMeasuredCore();}finally{ModDiagnostics.RuntimeCosts.End("FarSideLightSystem",started);ModDiagnostics.RuntimeCosts.Workload("SignalReplacements",_moved.Count,Requested.Count);}}
        private void UpdateMeasuredCore()

        {

            if(!SignalNativePhasePolicy.CanApply(signalUpdateSystem.currentPhase,Mod.Saving,_active))return;

            if (!_active || (!_initial && Requested.Count == 0 && _moved.Count == 0 && _lamps.Count==0 && _generatedLamps.Count==0 && _hiddenSignals.Count==0 && maintenance.Count==0 && _upgrades.IsEmptyIgnoreFilter)) return;

            Dependency.Complete();
            SynchronizeChangedLampStates();

            var nodes = _nodeWork; nodes.Clear(); forcedSignalWork.Clear(); foreach (var node in Requested) { nodes.Add(node); forcedSignalWork.Add(node); } Requested.Clear();
            if (_initial) { using (var items = _upgrades.ToEntityArray(Allocator.Temp)) foreach (var node in items) nodes.Add(node); _initial = false; }

            GatherNativeSignalEvents(nodes);

            // Late native role initialization can follow a completed road edit.
            // Inspect two upgraded nodes per second, never all city signals or phase writes.
            bool recovery=UnityEngine.Time.realtimeSinceStartup>=_nextReconcile;
            if(recovery)
            {
                _nextReconcile=UnityEngine.Time.realtimeSinceStartup+1;
                for(int budget=0;budget<2&&_reconcileNodes.Count>0;budget++)
                {
                    var check=_reconcileNodes.Dequeue();_reconcileSet.Remove(check);
                    if(!EnabledNode(check)||!HasSignalChildren(check))continue;
                    _reconcileSet.Add(check);_reconcileNodes.Enqueue(check);if(waitingSignalDefinitions.Contains(check))nodes.Add(check);if(EdgeMedianSignals(check,false)){signalPresentationChecks.Add(check);nodes.Add(check);}
                    CopySignalChildren(check,signalRecoveryChildren);
                    foreach(var actor in signalRecoveryChildren)
                    {
                        if(!SignalProp(actor,check))continue;
                        var binding=EntityManager.GetComponentData<ObjectLight>(actor);
                        if(_suppressedPrefabs.TryGetValue(actor,out var suppressedBinding)
                            ?SignalBindingPolicy.Changed(binding.m_GroupMask0,binding.m_GroupMask1,suppressedBinding.VehicleMask,suppressedBinding.PedestrianMask)
                            :_moved.TryGetValue(actor,out var bound)&&SignalBindingPolicy.Changed(binding.m_GroupMask0,binding.m_GroupMask1,bound.AppliedVehicleMask,bound.AppliedPedMask))
                        {nodes.Add(check);break;}
                        if(!_moved.ContainsKey(actor)&&!_hiddenSignals.ContainsKey(actor)){signalPresentationChecks.Add(check);nodes.Add(check);break;}
                    }
                }
            }
            MaintainChangedSignals(nodes,recovery);

            foreach (var node in nodes)

            {

                if (!EnabledNode(node) || !HasSignalChildren(node))continue;
                // Native cable registration queues these nodes once it becomes ready.
                // Keep the existing native assembly intact until every custom span has a continuous cable.
                if(EntityManager.HasComponent<MGRGardenCitySignals>(node)&&EntityManager.HasComponent<MGRSignalStyle>(node)&&!GardenCityLightPolicy.WireAssemblyReady(EntityManager.GetComponentData<MGRSignalStyle>(node).Value,World.GetOrCreateSystemManaged<SignalWireSystem>().Ready))continue;

                if(UnchangedSignalNode(node)){if(signalPresentationChecks.Contains(node))ReviewSignalPresentation(node);continue;}
                waitingSignalDefinitions.Remove(node);
                var props=_props;CopySignalChildren(node,props);

                var approaches=ReadApproaches(node);
                var surface=ReadLayoutSurface(node);
                if(EntityManager.HasComponent<MGRGardenCitySignals>(node))if(GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"custom node processed: approaches="+approaches.Count+" props="+props.Count+" surfaceSamples="+surface.Count);

                if(_reconcileSet.Add(node))_reconcileNodes.Enqueue(node);
                PrepareSupports(node,approaches,surface);
                if(deferSignalApplication){NodeDuplicateSignals(node,approaches);EdgeMedianSignalsForApproaches(node,true,approaches);continue;}
                // Apply physical supports before their head-only phase actors.
                // A pending/removed support can then keep every follower native
                // in this same event, regardless of native SubObject ordering.
                foreach (var prop in props)
                    if(!_supportLeaders.TryGetValue(prop,out var leader)||leader==prop)
                        Move(node, prop, approaches, surface);
                foreach (var prop in _primarySignals)
                    if(_supportLeaders[prop]!=prop)Move(node, prop, approaches, surface);
                if(EntityManager.HasComponent<MGRGardenCitySignals>(node))ApplyCrossingPlan(node,approaches);
                else ApplyPlainCrossingPlan(node,surface);
                NodeDuplicateSignals(node,approaches);EdgeMedianSignalsForApproaches(node,true,approaches);
                ApplyWireLamps(node);
                var wires=World.GetOrCreateSystemManaged<SignalWireSystem>();
                if(EntityManager.HasComponent<MGRGardenCitySignals>(node)&&EntityManager.HasComponent<MGRSignalStyle>(node)&&GardenCityLightPolicy.IsWireStyle(EntityManager.GetComponentData<MGRSignalStyle>(node).Value))wires.Set(node,_supportPositions,_requiredWireSpans);else wires.Remove(node);
                UpdateSignLabels(node,approaches);
                RememberSignalNode(node);

            }

        }

        private void Move(Entity node, Entity prop, IList<FarSideLightPolicy.Approach> approaches, IList<float3> surface)

        {

            bool custom=EntityManager.HasComponent<MGRGardenCitySignals>(node);
            if (!Live(prop) || !EntityManager.HasComponent<ObjectLight>(prop) || !EntityManager.HasComponent<ObjectTransform>(prop) || !EntityManager.HasComponent<PrefabRef>(prop)) {if(custom&&GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"prop not signal-ready "+prop+" live="+Live(prop)+" light="+EntityManager.HasComponent<ObjectLight>(prop));return;}

            if(!EnabledNode(node)||!EntityManager.HasComponent<Owner>(prop)||EntityManager.GetComponentData<Owner>(prop).m_Owner!=node){if(custom&&GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"prop owner differs "+prop);return;}

            if(custom&&_plannedSignals.ContainsKey(prop))ReleaseHiddenSignal(prop);
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab;
            if(custom&&GardenCityLightPolicy.TraceEnabled){var p=World.GetOrCreateSystemManaged<PrefabSystem>();string name=p.TryGetPrefab<StaticObjectPrefab>(prefab,out var model)?model.name:prefab.ToString();var light=EntityManager.GetComponentData<ObjectLight>(prop);GardenCityLightPolicy.Trace(node,"signal identity "+prop+" prefab="+name+" vehicleMask="+light.m_GroupMask0+" pedestrianMask="+light.m_GroupMask1);}

            var rolePrefab=_moved.TryGetValue(prop,out var prior)?prior.Prefab:prefab;
            if (EntityManager.HasComponent<StreetLightData>(rolePrefab)) {if(custom&&GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"lamp/sign excluded "+prop);return;}

            if (!EntityManager.HasComponent<TrafficLightData>(rolePrefab) || !FarSideLightPolicy.Vehicle(EntityManager.GetComponentData<TrafficLightData>(rolePrefab).m_Type)) {if(custom&&GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"vehicle prefab not ready "+prop+" type="+(EntityManager.HasComponent<TrafficLightData>(prefab)?EntityManager.GetComponentData<TrafficLightData>(prefab).m_Type.ToString():"missing"));return;}

            var current = EntityManager.GetComponentData<ObjectTransform>(prop);

            bool applied=_moved.TryGetValue(prop,out var saved) && FarSideLightPolicy.OwnsPose(prefab,saved.Prefab,saved.AppliedPrefab,current.Equals(saved.Applied));

            var original=applied?saved.Original:current;
            if(custom&&!_plannedSignals.ContainsKey(prop)){if(applied&&EntityManager.HasComponent<TrafficLightData>(saved.AppliedPrefab)&&!FarSideLightPolicy.Vehicle(EntityManager.GetComponentData<TrafficLightData>(saved.AppliedPrefab).m_Type))return;if(GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"non-primary signal "+prop);if(applied){Undo(prop,saved);ForgetMoved(prop);}return;}

            // A head-only phase must not appear before its physical support has
            // passed the asynchronous prefab pipeline. Retry with the existing
            // node-ready callback; never add a preview-frame readiness scan.
            if(custom && _supportLeaders.TryGetValue(prop, out var leader) && leader != prop &&
                (!_moved.TryGetValue(leader, out var support) ||
                 support.Node != node ||
                 !EntityManager.Exists(leader) ||
                 !EntityManager.HasComponent<PrefabRef>(leader) ||
                 EntityManager.GetComponentData<PrefabRef>(leader).m_Prefab != support.AppliedPrefab ||
                 support.AppliedPrefab == support.Prefab ||
                 !support.Applied.m_Position.Equals(_plannedSignals[leader].m_Position)))
            {
                if(applied){Undo(prop,saved);ForgetMoved(prop);}
                return;
            }

            ObjectTransform placed;
            if(custom){placed=_plannedSignals[prop];}
            else
            {
                if(!FarSideLightPolicy.TryMove(original,approaches,out placed)||!FarSideLightPolicy.TryGround(original.m_Position,placed.m_Position,surface,out float height)){if(applied){Undo(prop,saved);ForgetMoved(prop);}return;}
                placed.m_Position.y=height;
            }

            Entity originalPrefab=GardenCityLightPolicy.NativeSource(applied?saved.Prefab:prefab);
            Entity selectedPrefab=originalPrefab;
            var lightState=EntityManager.GetComponentData<ObjectLight>(prop);
            ushort originalMask=applied&&lightState.m_GroupMask1==saved.AppliedPedMask?saved.OriginalPedMask:lightState.m_GroupMask1;
            ushort selectedMask=originalMask;
            if(custom)
                selectedPrefab=GardenCityLightPolicy.Select(World.GetOrCreateSystemManaged<PrefabSystem>(),EntityManager,node,original,ref placed,approaches,originalPrefab,originalMask,out selectedMask,_supportPositions,_virtualSupport,_requiredWireSpans,_sourceApproaches.TryGetValue(prop,out var bound)?bound:-1,OwnsSharedPole(prop),lightState.m_GroupMask0,!_supportLeaders.TryGetValue(prop,out var assemblyOwner)||assemblyOwner==prop,_sharedPhaseSignals.Contains(prop));
            if(selectedPrefab==originalPrefab){selectedMask=originalMask;if(EntityManager.HasComponent<MGRGardenCitySignals>(node)){if(applied&&saved.AppliedPrefab!=saved.Prefab&&lightState.m_GroupMask0==saved.AppliedVehicleMask&&lightState.m_GroupMask1==saved.AppliedPedMask)return;if(applied){Undo(prop,saved);ForgetMoved(prop);}return;}}
            if(lightState.m_GroupMask1!=selectedMask){lightState.m_GroupMask1=selectedMask;EntityManager.SetComponentData(prop,lightState);}
            if(applied&&placed.Equals(current)&&prefab==selectedPrefab&&saved.AppliedPedMask==selectedMask&&saved.AppliedVehicleMask==lightState.m_GroupMask0)return;
            if(prefab!=selectedPrefab){if(custom)Mod.Log.Info("Signal composite applied: node="+node+" prop="+prop+" prefab="+World.GetOrCreateSystemManaged<PrefabSystem>().GetPrefab<StaticObjectPrefab>(selectedPrefab).name+" vehicleMask="+lightState.m_GroupMask0+" pedestrianMask="+selectedMask);SetPreviewPrefab(prop,selectedPrefab);if(!EntityManager.HasComponent<BatchesUpdated>(prop))EntityManager.AddComponent<BatchesUpdated>(prop);}

            _moved[prop] = new Moved { Node = node, Owner = node, Prefab = originalPrefab, AppliedPrefab=selectedPrefab, Original = original, Applied = placed,OriginalPedMask=originalMask,AppliedPedMask=selectedMask,AppliedVehicleMask=lightState.m_GroupMask0 };

            maintenance.Track(prop,node,SignalMaintenanceIndex<Entity>.Moved);
            Write(prop, placed);

            // ObjectLight group masks, owner, node controller and lane signals remain native.

        }

        private ObjectTransform OriginalTransform(Entity prop)
        {
            var current=EntityManager.GetComponentData<ObjectTransform>(prop);
            bool known=_moved.TryGetValue(prop,out var moved)||_lamps.TryGetValue(prop,out moved);
            return known&&FarSideLightPolicy.OwnsPose(EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab,moved.Prefab,moved.AppliedPrefab,current.Equals(moved.Applied))?moved.Original:current;
        }
        private Entity OriginalPrefab(Entity prop)=>GardenCityLightPolicy.NativeSource(_moved.TryGetValue(prop,out var moved)||_lamps.TryGetValue(prop,out moved)?moved.Prefab:_suppressedPrefabs.TryGetValue(prop,out var suppressed)?suppressed.Original:EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab);
        internal bool TryPreviewVisual(Entity source,out Entity prefab,out ObjectTransform native,out ObjectTransform applied)
        {
            prefab=Entity.Null;native=applied=default;
            if(!Live(source)||!EntityManager.HasComponent<ObjectTransform>(source)||!EntityManager.HasComponent<PrefabRef>(source))return false;
            if((_moved.TryGetValue(source,out var moved)||_lamps.TryGetValue(source,out moved))&&EnabledNode(moved.Node)&&EntityManager.GetComponentData<PrefabRef>(source).m_Prefab==moved.AppliedPrefab)
            {prefab=moved.AppliedPrefab;native=moved.Original;applied=moved.Applied;return SignalPreviewIdentityPolicy.Customized(moved.Prefab,prefab,native,applied);}
            if(_suppressedPrefabs.TryGetValue(source,out var suppressed)&&EntityManager.GetComponentData<PrefabRef>(source).m_Prefab==suppressed.Applied)
            {prefab=suppressed.Applied;native=applied=EntityManager.GetComponentData<ObjectTransform>(source);return true;}
            return false;
        }

        internal bool TryPreviewMasks(Entity source, out ushort vehicle, out ushort pedestrian)
        {
            vehicle = pedestrian = 0;
            if (!Live(source) || !EntityManager.HasComponent<PrefabRef>(source)) return false;
            if (!EntityManager.HasComponent<ObjectLight>(source))
                return EntityManager.HasComponent<StreetLightData>(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab);
            var light = EntityManager.GetComponentData<ObjectLight>(source);
            vehicle = light.m_GroupMask0;
            pedestrian = light.m_GroupMask1;
            // Custom composite panels may combine crossing faces. Compare a
            // fallback against the source's native binding, not that visual
            // combination. A newer controller assignment takes precedence.
            if (_moved.TryGetValue(source, out var moved)
                && EntityManager.GetComponentData<PrefabRef>(source).m_Prefab == moved.AppliedPrefab)
                pedestrian = SignalPreviewIdentityPolicy.NativePedestrianMask(pedestrian,
                    moved.OriginalPedMask, moved.AppliedPedMask);
            return true;
        }
        private void PrepareSupports(Entity node,IList<FarSideLightPolicy.Approach> approaches,IList<float3> surface)
        {
            deferSignalApplication=false;((CornerSupportList)_supportPositions).UsesCornerRule=false;_plannedSignals.Clear();_sourceApproaches.Clear();_supportPositions.Clear();_requiredWireSpans.Clear();_primarySignals.Clear();_supportLeaders.Clear();_sharedPhaseSignals.Clear();_virtualSupport=-1;if(!EntityManager.HasComponent<MGRGardenCitySignals>(node))return;
            bool wire=EntityManager.HasComponent<MGRSignalStyle>(node)&&GardenCityLightPolicy.IsWireStyle(EntityManager.GetComponentData<MGRSignalStyle>(node).Value);
            foreach(var prop in _props)
            {
                if(!SignalProp(prop,node)||!PrimarySignal(prop,approaches))continue;
                _primarySignals.Add(prop);
                _supportLeaders[prop]=prop;
            }
            // Stable leadership is independent of the native SubObject order.
            // Share only when distinct native lane masks prove disjoint heads.
            for(int approach=0;approach<approaches.Count;approach++)
            {
                var members=new List<Entity>();var masks=new List<ushort>();
                foreach(var prop in _primarySignals)if(SourceApproach(prop,approaches)==approach)
                {members.Add(prop);masks.Add(EntityManager.GetComponentData<ObjectLight>(prop).m_GroupMask0);}
                if(!SignalSupportGroupPolicy.CanShare(approaches[approach],masks))continue;
                var leader=members[0];
                foreach(var member in members)if(member.Index<leader.Index || member.Index==leader.Index&&member.Version<leader.Version)leader=member;
                foreach(var member in members){_supportLeaders[member]=leader;_sharedPhaseSignals.Add(member);}
            }
            if(wire&&TryPrepareWireLayout(node,approaches))return;
            if(!wire&&TryPrepareArmCornerLayout(node,approaches))return;
            foreach(var prop in _primarySignals)
            {
                if(_supportLeaders[prop]!=prop)continue;
                var original=OriginalTransform(prop);
                int source=SourceApproach(prop,approaches);
                ObjectTransform placed;
                bool fitted=wire?FarSideLightPolicy.TrySignalMountFrom(original,approaches,source,out placed):FarSideLightPolicy.TryArmMountFrom(original,approaches,source,_supportPositions,out placed);
                if(!fitted&&wire)fitted=FarSideLightPolicy.TryArmMountFrom(original,approaches,source,_supportPositions,out placed);
                if(!fitted||!FarSideLightPolicy.IsFarSide(placed.m_Position,approaches,source)||!FarSideLightPolicy.TryGround(original.m_Position,placed.m_Position,surface,out var height))continue;
                placed.m_Position.y=height;_plannedSignals[prop]=placed;_sourceApproaches[prop]=source;_supportPositions.Add(placed.m_Position);
            }
            var keys=new List<Entity>(_plannedSignals.Keys);for(int i=0;i<keys.Count;i++)
            {
                var placed=_plannedSignals[keys[i]];
                int source=_sourceApproaches[keys[i]];
                var occupied=new List<float3>(_supportPositions);occupied.RemoveAt(i);
                float plannedReach=0;
                if(!wire&&source>=0)FarSideLightPolicy.ArmCandidate(placed.m_Position,approaches[source],null,out plannedReach);
                if(TryNativeMount(placed.m_Position,approaches,surface,out var native,occupied)&&source>=0&&FarSideLightPolicy.IsFarSide(native,approaches,source)&&GardenCityLightPolicy.TryLayout(approaches[source].Incoming,approaches[source].Outward,native,out _,out _)&&(wire||(FarSideLightPolicy.ArmCandidate(native,approaches[source],occupied,out var nativeReach)&&FarSideLightPolicy.AcceptArmAdjustment(placed.m_Position,native,approaches[source],plannedReach,nativeReach,.75f))))placed.m_Position=native;
                else if(FarSideLightPolicy.TryCurbMount(placed,approaches,out var safer)&&FarSideLightPolicy.IsFarSide(safer.m_Position,approaches,source)&&(wire||(source>=0&&FarSideLightPolicy.ArmCandidate(safer.m_Position,approaches[source],occupied,out var safeReach)&&FarSideLightPolicy.AcceptArmAdjustment(placed.m_Position,safer.m_Position,approaches[source],plannedReach,safeReach,.75f))))placed=safer;
                _plannedSignals[keys[i]]=placed;_supportPositions[i]=placed.m_Position;
            }
            if(!wire)for(int i=1;i<keys.Count;i++)
            {
                int incoming=_sourceApproaches[keys[i]];
                for(int j=0;j<i;j++)
                {
                    int other=_sourceApproaches[keys[j]];
                    if(incoming==other||math.abs(math.dot(approaches[incoming].Outward,approaches[other].Outward))>.92f)continue;
                    int count=0;for(int k=0;k<i;k++)if(math.distancesq(_supportPositions[k],_supportPositions[j])<.01f)count++;
                    if(count>=2||!FarSideLightPolicy.CanSharePole(_supportPositions[j],approaches,other,incoming,out var reach))continue;
                    if(!FarSideLightPolicy.ArmCandidate(_supportPositions[i],approaches[incoming],null,out var currentReach)||!FarSideLightPolicy.AcceptArmAdjustment(_supportPositions[i],_supportPositions[j],approaches[incoming],currentReach,reach,1.5f))continue;
                    var shared=_plannedSignals[keys[i]];shared.m_Position=_supportPositions[j];_plannedSignals[keys[i]]=shared;_supportPositions[i]=shared.m_Position;break;
                }
            }
            if(approaches.Count==3&&EntityManager.HasComponent<MGRSignalStyle>(node)&&GardenCityLightPolicy.IsWireStyle(EntityManager.GetComponentData<MGRSignalStyle>(node).Value)&&GardenCityLightPolicy.CompleteWireCorners(_supportPositions))
            {
                var estimate=_supportPositions[3];var occupied=_supportPositions.GetRange(0,3);bool found=TryNativeMount(estimate,approaches,surface,out var native,occupied);
                if(!found)
                {
                    float best=144;foreach(var road in approaches)foreach(var curb in new[]{road.Left,road.Right})
                    {
                        var outward=math.normalizesafe(curb.xz-(road.Left.xz+road.Right.xz)*.5f);var candidate=curb;candidate.xz+=outward*.6f;
                        bool duplicate=false;foreach(var used in occupied)if(math.distancesq(used.xz,candidate.xz)<6.25f)duplicate=true;
                        float distance=math.distancesq(estimate.xz,candidate.xz);if(duplicate||distance>=best||!FarSideLightPolicy.IsRoadside(candidate,approaches)||!FarSideLightPolicy.TryGround(estimate,candidate,surface,out var height))continue;
                        candidate.y=height;native=candidate;best=distance;found=true;
                    }
                }
                if(found)_supportPositions[3]=native;else _supportPositions.RemoveAt(3);
            }
            if(EntityManager.HasComponent<MGRSignalStyle>(node)&&GardenCityLightPolicy.IsWireStyle(EntityManager.GetComponentData<MGRSignalStyle>(node).Value)&&GardenCityLightPolicy.TryAlignedWireCorners(_supportPositions,approaches,out var aligned))
            {
                bool grounded=true;for(int i=0;i<aligned.Count;i++)
                {
                    if(!FarSideLightPolicy.IsRoadside(aligned[i],approaches)||(i<keys.Count&&!FarSideLightPolicy.IsFarSide(aligned[i],approaches,_sourceApproaches[keys[i]]))||!FarSideLightPolicy.TryGround(_supportPositions[i],aligned[i],surface,out var height)){grounded=false;break;}
                    var point=aligned[i];point.y=height;aligned[i]=point;
                }
                if(grounded)
                {
                    _supportPositions.Clear();_supportPositions.AddRange(aligned);
                    for(int i=0;i<keys.Count;i++){var placed=_plannedSignals[keys[i]];placed.m_Position=aligned[i];_plannedSignals[keys[i]]=placed;}
                }
            }
            for(int i=0;i<keys.Count;i++)
            {
                int selected=_sourceApproaches[keys[i]];if(selected<0)continue;var road=approaches[selected];
                if(!GardenCityLightPolicy.TryLayout(road.Incoming,road.Outward,_supportPositions[i],out var inward,out var heads))continue;
                int target=GardenCityLightPolicy.WireNeighborForLayout(_supportPositions,i,inward,((CornerSupportList)_supportPositions).UsesCornerRule);if(target>=0&&!GardenCityLightPolicy.HasRequiredSpan(_requiredWireSpans,i,target))_requiredWireSpans.Add(new int2(i,target));
            }
            if(_supportPositions.Count>keys.Count)_virtualSupport=((CornerSupportList)_supportPositions).UsesCornerRule?keys.Count:_supportPositions.Count-1;
            foreach(var prop in _primarySignals)
            {
                var leader=_supportLeaders[prop];
                if(leader==prop || !_plannedSignals.TryGetValue(leader,out var pose))continue;
                _plannedSignals[prop]=pose;_sourceApproaches[prop]=_sourceApproaches[leader];
            }
        }
        private bool OwnsSharedPole(Entity prop)
        {
            if(!_plannedSignals.TryGetValue(prop,out var pose))return true;
            foreach(var other in _plannedSignals)
            {
                if(other.Key==prop)return true;
                if(math.distancesq(other.Value.m_Position,pose.m_Position)<.01f)return false;
            }
            return true;
        }
        private bool TryNativeMount(float3 estimate,IList<FarSideLightPolicy.Approach> approaches,IList<float3> surface,out float3 position,IList<float3> occupied=null)
        {
            position=estimate;float best=64;bool found=false;
            foreach(var prop in _props)
            {
                if(!Live(prop)||!EntityManager.HasComponent<ObjectTransform>(prop)||!EntityManager.HasComponent<PrefabRef>(prop))continue;
                var prefab=OriginalPrefab(prop);if(!EntityManager.HasComponent<TrafficLightData>(prefab)&&!EntityManager.HasComponent<StreetLightData>(prefab))continue;
                var source=OriginalTransform(prop);if(!FarSideLightPolicy.TryCurbMount(source,approaches,out var mounted)||!FarSideLightPolicy.IsRoadside(mounted.m_Position,approaches))continue;
                bool duplicate=false;if(occupied!=null)foreach(var used in occupied)if(math.distancesq(used.xz,mounted.m_Position.xz)<6.25f)duplicate=true;if(duplicate)continue;
                float distance=math.distancesq(estimate.xz,mounted.m_Position.xz);if(distance>=best)continue;
                if(!FarSideLightPolicy.TryGround(source.m_Position,mounted.m_Position,surface,out var height))continue;
                best=distance;position=mounted.m_Position;position.y=height;found=true;
            }
            return found;
        }
        private void ApplyWireLamps(Entity node)
        {
            if(!EntityManager.HasComponent<MGRGardenCitySignals>(node)||_supportPositions.Count<2)return;
            bool customReady=false;foreach(var signal in _plannedSignals)if(_moved.TryGetValue(signal.Key,out var mounted)&&mounted.AppliedPrefab!=mounted.Prefab){customReady=true;break;}if(!customReady)return;
            WatchLampRoads(node);
            var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();var used=new HashSet<Entity>();float3 centre=float3.zero;foreach(var point in _supportPositions)centre+=point;centre/=_supportPositions.Count;
            var pair=GardenCityLightPolicy.LuminairePair(_supportPositions);int installed=0;
            foreach(int index in new[]{pair.x,pair.y})
            {
                Entity winner=Entity.Null;float best=float.MaxValue;
                foreach(var prop in _props)
                {
                    if(used.Contains(prop)||!Live(prop)||!EntityManager.HasComponent<Game.Objects.StreetLight>(prop)||!EntityManager.HasComponent<PrefabRef>(prop)||!EntityManager.HasComponent<ObjectTransform>(prop))continue;
                    var original=_lamps.TryGetValue(prop,out var saved)?saved.Original:EntityManager.GetComponentData<ObjectTransform>(prop);float distance=math.distancesq(original.m_Position.xz,_supportPositions[index].xz);if(distance<best){best=distance;winner=prop;}
                }
                if(winner==Entity.Null)
                {
                    int slot=index==pair.x?0:1;
                    if(EnsureGeneratedLamp(node,slot,_supportPositions[index],centre,prefabs))installed++;
                    continue;
                }
                if(_generatedLamps.TryGetValue(node,out var extras)){int slot=index==pair.x?0:1;if(extras[slot]!=Entity.Null){maintenance.Forget(extras[slot],SignalMaintenanceIndex<Entity>.GeneratedLamp);GeneratedVisualLifecycle.Retire(World,extras[slot]);extras[slot]=Entity.Null;}}
                used.Add(winner);
                bool prior=_lamps.TryGetValue(winner,out var previous);var originalPose=prior?previous.Original:EntityManager.GetComponentData<ObjectTransform>(winner);var originalPrefab=prior?previous.Prefab:OriginalPrefab(winner);
                var selected=GardenCityLightPolicy.SelectLuminaire(prefabs,EntityManager,node,originalPrefab);if(selected==originalPrefab){used.Remove(winner);continue;}installed++;ReleaseHiddenSignal(winner);
                var placed=new ObjectTransform{m_Position=_supportPositions[index],m_Rotation=GardenCityLightPolicy.InwardLampRotation(_supportPositions[index],centre)};
                if(prior&&previous.AppliedPrefab==selected&&!EntityManager.HasComponent<Game.Objects.Relative>(winner)&&EntityManager.GetComponentData<ObjectTransform>(winner).Equals(placed)&&EntityManager.GetComponentData<PrefabRef>(winner).m_Prefab==selected)continue;
                bool hadRelative=prior?previous.HadRelative:EntityManager.HasComponent<Game.Objects.Relative>(winner);
                var relative=prior?previous.OriginalRelative:hadRelative?EntityManager.GetComponentData<Game.Objects.Relative>(winner):default;
                // Native effect transforms prefer Relative over Transform. A relocated
                // fixture must use its actual pole position, not the old road-relative mount.
                if(EntityManager.HasComponent<Game.Objects.Relative>(winner))EntityManager.RemoveComponent<Game.Objects.Relative>(winner);
                SetPreviewPrefab(winner,selected);_lamps[winner]=new Moved{Node=node,Owner=prior?previous.Owner:EntityManager.HasComponent<Owner>(winner)?EntityManager.GetComponentData<Owner>(winner).m_Owner:Entity.Null,Prefab=originalPrefab,AppliedPrefab=selected,Original=originalPose,Applied=placed,HadRelative=hadRelative,OriginalRelative=relative};maintenance.Track(winner,node,SignalMaintenanceIndex<Entity>.Lamp);Write(winner,placed);SynchronizeLampState(winner,node,true);
                if(!EntityManager.HasComponent<EffectsUpdated>(winner))EntityManager.AddComponent<EffectsUpdated>(winner);Mod.Log.Info("Native junction luminaire attached: node="+node+" prop="+winner+" support="+index);
            }
            // Only node-owned junction controllers are considered. Never touch midblock lamps.
            // Keep native lighting until both derived fixtures are initialized.
            if(installed==2)foreach(var prop in _props)
            {
                if(used.Contains(prop)||!Live(prop)||!EntityManager.HasComponent<Game.Objects.StreetLight>(prop)||!EntityManager.HasComponent<PrefabRef>(prop))continue;
                HideJunctionLamp(prop,node,prefabs);
            }
            var removed=new List<Entity>();maintenance.CopyNode(node,maintenanceWork);foreach(var item in maintenanceWork)
                if(_lamps.TryGetValue(item.Actor,out var savedLamp)&&!used.Contains(item.Actor)){Undo(item.Actor,savedLamp);removed.Add(item.Actor);}foreach(var prop in removed)ForgetLamp(prop);
        }
        private bool EnsureGeneratedLamp(Entity node,int slot,float3 support,float3 centre,PrefabSystem prefabs)
        {
            // Roads without native junction lamps still receive two fixtures.
            // These are unsaved visual controllers with no network/Owner reference.
            if(_lampDonor==Entity.Null||!EntityManager.Exists(_lampDonor))
            {
                using(var templates=_lampTemplates.ToEntityArray(Allocator.Temp))foreach(var candidate in templates)
                {
                    if(!prefabs.TryGetPrefab<StaticObjectPrefab>(candidate,out var source)||!source.isBuiltin||!source.TryGet<EffectSource>(out var effects)||effects.m_Effects==null)continue;
                    foreach(var effect in effects.m_Effects)if(effect.m_Effect!=null&&prefabs.TryGetEntity(effect.m_Effect,out var light)&&EntityManager.HasComponent<LightEffectData>(light)){_lampDonor=candidate;break;}
                    if(_lampDonor!=Entity.Null)break;
                }
            }
            if(_lampDonor==Entity.Null)return false;
            var selected=GardenCityLightPolicy.SelectLuminaire(prefabs,EntityManager,node,_lampDonor);if(selected==_lampDonor||!EntityManager.HasComponent<ObjectData>(selected))return false;
            if(!_generatedLamps.TryGetValue(node,out var slots)){slots=new Entity[2];_generatedLamps[node]=slots;}
            var entity=slots[slot];var pose=new ObjectTransform{m_Position=support,m_Rotation=GardenCityLightPolicy.InwardLampRotation(support,centre)};
            if(!Live(entity))
            {
                maintenance.Forget(entity,SignalMaintenanceIndex<Entity>.GeneratedLamp);entity=EntityManager.CreateEntity(EntityManager.GetComponentData<ObjectData>(selected).m_Archetype);slots[slot]=entity;maintenance.Track(entity,node,SignalMaintenanceIndex<Entity>.GeneratedLamp);
                if(EntityManager.HasComponent<Owner>(entity))EntityManager.RemoveComponent<Owner>(entity);
                if(EntityManager.HasComponent<Game.Objects.Relative>(entity))EntityManager.RemoveComponent<Game.Objects.Relative>(entity);
                if(EntityManager.HasComponent<Game.Objects.NetObject>(entity))EntityManager.RemoveComponent<Game.Objects.NetObject>(entity);
                SetPreviewPrefab(entity,selected);EntityManager.SetComponentData(entity,pose);
                EntityManager.AddComponent<MGRSignalLuminaireRuntime>(entity);GeneratedVisualLifecycle.Track(World,entity);SynchronizeLampState(entity,node,false);
                if(!EntityManager.HasComponent<Created>(entity))EntityManager.AddComponent<Created>(entity);
            }
            else if(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab==selected&&EntityManager.GetComponentData<ObjectTransform>(entity).Equals(pose))return true;
            else SetPreviewPrefab(entity,selected);
            Write(entity,pose);if(!EntityManager.HasComponent<EffectsUpdated>(entity))EntityManager.AddComponent<EffectsUpdated>(entity);return true;
        }
        // Junction controllers are not road-owned SubObjects. The native road
        // lighting job therefore does not propagate road on/off transitions to them.
        internal static bool JunctionLampOn(bool anyLitRoad)=>anyLitRoad;
        private void SynchronizeLampState(Entity lamp,Entity node,bool preserve)
        {
            if(!Live(lamp)||!EntityManager.HasComponent<Game.Objects.StreetLight>(lamp)||!EntityManager.HasBuffer<ConnectedEdge>(node))return;
            bool found=false,on=false;
            foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(node,true))
                if(EntityManager.HasComponent<Game.Net.Road>(connection.m_Edge))
                {
                    found=true;on|=(EntityManager.GetComponentData<Game.Net.Road>(connection.m_Edge).m_Flags&Game.Net.RoadFlags.LightsOff)==0;
                }
            if(!found)return;
            var state=EntityManager.GetComponentData<Game.Objects.StreetLight>(lamp);
            var updated=state;
            if(JunctionLampOn(on))updated.m_State&=~Game.Objects.StreetLightState.TurnedOff;
            else updated.m_State|=Game.Objects.StreetLightState.TurnedOff;
            if(updated.m_State==state.m_State)return;
            if(preserve&&!_originalLampStates.ContainsKey(lamp))_originalLampStates.Add(lamp,state);
            EntityManager.SetComponentData(lamp,updated);
            if(!EntityManager.HasComponent<EffectsUpdated>(lamp))EntityManager.AddComponent<EffectsUpdated>(lamp);
            Mod.Log.Info("Junction lamp state synchronized: prop="+lamp+" node="+node+" state="+updated.m_State+" connectedRoadLit="+on);
        }
        private void RetireGeneratedLamps(Entity node,bool saving=false)
        {
            if(!_generatedLamps.TryGetValue(node,out var lamps))return;
            foreach(var lamp in lamps)if(lamp!=Entity.Null){maintenance.Forget(lamp,SignalMaintenanceIndex<Entity>.GeneratedLamp);if(saving)GeneratedVisualLifecycle.RetireForSerialization(World,lamp);else GeneratedVisualLifecycle.Retire(World,lamp);}
            _generatedLamps.Remove(node);
        }
        private void ClearGeneratedLamps(bool saving=false)
        {var nodes=new List<Entity>(_generatedLamps.Keys);foreach(var node in nodes)RetireGeneratedLamps(node,saving);_lampDonor=Entity.Null;}
        private void HideJunctionLamp(Entity prop,Entity node,PrefabSystem prefabs)
        {
            if(!EntityManager.HasComponent<Owner>(prop)||EntityManager.GetComponentData<Owner>(prop).m_Owner!=node||EntityManager.HasComponent<Game.Tools.Hidden>(prop))return;
            var original=OriginalPrefab(prop);var current=EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab;
            var selected=GardenCityLightPolicy.SelectSuppressedLamp(prefabs,EntityManager,node,original);if(selected==original)return;
            if(current==selected)return;
            _hiddenSignals[prop]=node;maintenance.Track(prop,node,SignalMaintenanceIndex<Entity>.Hidden);_suppressedPrefabs[prop]=new Suppressed{Original=original,Applied=selected};
            SetPreviewPrefab(prop,selected);Write(prop,EntityManager.GetComponentData<ObjectTransform>(prop));
            if(!EntityManager.HasComponent<EffectsUpdated>(prop))EntityManager.AddComponent<EffectsUpdated>(prop);
        }
        private void UpdateSignLabels(Entity node,IList<FarSideLightPolicy.Approach> roads)
        {
            // Do not plan labels that SetCore deliberately declines to create.
            // Keep plate geometry and all signal/lamp behavior unchanged.
            if(SignalStreetNameSystem.LetteringIsolationEnabled)return;
            if(!EntityManager.HasComponent<MGRGardenCitySignals>(node))return;
            foreach(var planned in _plannedSignals)
            {
                if(!_moved.TryGetValue(planned.Key,out var moved)||moved.AppliedPrefab==moved.Prefab)continue;
                int source=_sourceApproaches.TryGetValue(planned.Key,out var bound)?bound:SourceApproach(planned.Key,roads);if(source<0)continue;int cross=-1;float best=.7f;
                for(int i=0;i<roads.Count;i++)if(i!=source){float angle=math.abs(math.dot(roads[source].Outward,roads[i].Outward));if(angle<best){best=angle;cross=i;}}
                if(cross<0)continue;var facing=new float3(roads[source].Outward.x,0,roads[source].Outward.y);
                // Plate and its hangers follow the mast axis, independently of the lane-facing heads.
                facing=math.rotate(moved.Applied.m_Rotation,new float3(0,0,1));
                byte style=EntityManager.HasComponent<MGRSignalStyle>(node)?EntityManager.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;
                var position=moved.Applied.m_Position+math.rotate(moved.Applied.m_Rotation,GardenCityLightPolicy.StreetSignPosition(style))+facing*.026f;
                World.GetOrCreateSystemManaged<SignalStreetNameSystem>().Set(planned.Key,node,roads[cross].Road,position,facing);
            }
        }
        private byte MaintenanceNode(Entity node)
        {
            if(_maintenanceNodes.TryGetValue(node,out var state))return state;
            bool enabled=EnabledNode(node);
            state=SignalMaintenancePolicy.State(enabled,enabled&&EntityManager.HasComponent<MGRGardenCitySignals>(node));
            _maintenanceNodes.Add(node,state);return state;
        }
        private List<KeyValuePair<Entity,Moved>> ReadyCrossingSupports(Entity node)
        {
            // A plan is intent, not proof that the asynchronous prefab pipeline
            // has produced a replacement. Snapshot only applied supports.
            var ready=new List<KeyValuePair<Entity,Moved>>();
            foreach(var planned in _plannedSignals)
                if(!_supportLeaders.TryGetValue(planned.Key,out var leader)||leader==planned.Key)
                if(_moved.TryGetValue(planned.Key,out var moved)&&moved.Node==node&&moved.AppliedPrefab!=moved.Prefab)
                    ready.Add(new KeyValuePair<Entity,Moved>(planned.Key,moved));
            return ready;
        }
        private void ApplyCrossingPlan(Entity node,IList<FarSideLightPolicy.Approach> approaches)
        {
            _visibleSignals.Clear();var supports=ReadyCrossingSupports(node);
            // Every applied phase stays visible; only the hardware owner plans
            // pedestrian attachments, lamps and physical support geometry.
            foreach(var planned in _plannedSignals)
                if(_moved.TryGetValue(planned.Key,out var active)&&active.Node==node&&active.AppliedPrefab!=active.Prefab&&
                    EntityManager.Exists(planned.Key)&&EntityManager.HasComponent<PrefabRef>(planned.Key)&&
                    EntityManager.GetComponentData<PrefabRef>(planned.Key).m_Prefab==active.AppliedPrefab)
                    _visibleSignals.Add(planned.Key);
            if(supports.Count==0)
            {
                foreach(var prop in _props)if(SignalProp(prop,node))ReleaseHiddenSignal(prop);
                return;
            }
            var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
            var vehicleCoverage=ReadSignalCoverage(node,approaches);
            foreach(var planned in supports)
            {
                var pole=planned.Value;
                if(!Live(planned.Key)||!EntityManager.HasComponent<PrefabRef>(planned.Key)||EntityManager.GetComponentData<PrefabRef>(planned.Key).m_Prefab!=pole.AppliedPrefab)continue;
                var faces=GardenCityLightPolicy.CrossingFaces(EntityManager,node,pole.Applied.m_Position);
                for(int faceIndex=1;faceIndex<faces.Count;faceIndex++)
                {
                    Entity winner=Entity.Null;float best=float.MaxValue;
                    foreach(var candidate in _props)
                    {
                        if(_visibleSignals.Contains(candidate)||_plannedSignals.ContainsKey(candidate)||!SignalProp(candidate,node))continue;
                        var role=EntityManager.GetComponentData<TrafficLightData>(OriginalPrefab(candidate)).m_Type;
                        // A missing vehicle replacement cannot be converted into
                        // a pedestrian-only donor to complete another support.
                        if(FarSideLightPolicy.Vehicle(role)&&!SignalCoveragePolicy.Covered(SourceApproach(candidate,approaches),
                            EntityManager.GetComponentData<ObjectLight>(candidate).m_GroupMask0,0,true,false,vehicleCoverage))continue;
                        float score=math.distancesq(OriginalTransform(candidate).m_Position.xz,pole.Applied.m_Position.xz)+(FarSideLightPolicy.Vehicle(role)?10000:0);
                        if(score<best){best=score;winner=candidate;}
                    }
                    if(winner==Entity.Null){if(GardenCityLightPolicy.TraceEnabled)GardenCityLightPolicy.Trace(node,"crossing controller unavailable at support "+planned.Key);continue;}
                    var current=EntityManager.GetComponentData<ObjectTransform>(winner);var original=OriginalTransform(winner);var originalPrefab=OriginalPrefab(winner);
                    var light=EntityManager.GetComponentData<ObjectLight>(winner);bool wasMoved=_moved.TryGetValue(winner,out var prior);
                    ushort originalMask=wasMoved&&light.m_GroupMask1==prior.AppliedPedMask?prior.OriginalPedMask:light.m_GroupMask1;
                    var placed=pole.Applied;var face=faces[faceIndex];_visibleSignals.Add(winner);var selected=GardenCityLightPolicy.SelectPedestrian(prefabs,EntityManager,node,originalPrefab,ref placed,face);
                    if(selected==originalPrefab)continue;
                    _visibleSignals.Add(winner);ReleaseHiddenSignal(winner);
                    if(wasMoved&&prior.AppliedPrefab==selected&&current.Equals(placed)&&light.m_GroupMask1==face.Mask&&prior.AppliedVehicleMask==light.m_GroupMask0)continue;
                    light.m_GroupMask1=face.Mask;EntityManager.SetComponentData(winner,light);SetPreviewPrefab(winner,selected);
                    _moved[winner]=new Moved{Node=node,Owner=node,Prefab=originalPrefab,AppliedPrefab=selected,Original=original,Applied=placed,OriginalPedMask=originalMask,AppliedPedMask=face.Mask,AppliedVehicleMask=light.m_GroupMask0};maintenance.Track(winner,node,SignalMaintenanceIndex<Entity>.Moved);Write(winner,placed);
                    Mod.Log.Info("Crossing face attached: node="+node+" support="+planned.Key+" phase="+face.Mask);
                }
            }
            var coverage=ReadSignalCoverage(node,approaches);
            foreach(var prop in _props)if(SignalProp(prop,node))
            {
                if(_visibleSignals.Contains(prop)||!CoveredVehiclePhase(prop,node,approaches,coverage))ReleaseHiddenSignal(prop);
                else HideSignal(prop,node);
            }
        }
        private List<SignalCoverageEntry> ReadSignalCoverage(Entity node,IList<FarSideLightPolicy.Approach> approaches)
        {
            var result=new List<SignalCoverageEntry>();
            if(!HasSignalChildren(node))return result;
            CopySignalChildren(node,signalCoverageChildren);
            foreach(var key in signalCoverageChildren)
            {
                if(!_moved.TryGetValue(key,out var moved)||moved.Node!=node||(moved.AppliedPrefab==moved.Prefab&&moved.Applied.Equals(moved.Original))
                    ||!Live(key)||!EntityManager.HasComponent<Owner>(key)||!EntityManager.HasComponent<PrefabRef>(key)
                    ||!EntityManager.HasComponent<ObjectTransform>(key)
                    ||!EntityManager.HasComponent<ObjectLight>(key)||!EntityManager.HasComponent<TrafficLightData>(moved.AppliedPrefab))continue;
                if(!SignalCoveragePolicy.Ready(EntityManager.GetComponentData<Owner>(key).m_Owner==node,
                    EntityManager.GetComponentData<PrefabRef>(key).m_Prefab==moved.AppliedPrefab,
                    EntityManager.GetComponentData<ObjectTransform>(key).Equals(moved.Applied),_hiddenSignals.ContainsKey(key),
                    EntityManager.HasComponent<Game.Tools.Hidden>(key),EntityManager.HasComponent<Overridden>(key)))continue;
                var replacement=EntityManager.GetComponentData<ObjectLight>(key);
                var role=EntityManager.GetComponentData<TrafficLightData>(moved.AppliedPrefab).m_Type;
                result.Add(new SignalCoverageEntry {
                    Approach=SignalSourceAssociationPolicy.Find(moved.Original,approaches,replacement.m_GroupMask0),
                    Vehicle=FarSideLightPolicy.Vehicle(role)?replacement.m_GroupMask0:(ushort)0,
                    Pedestrian=(role&(TrafficLightType.CrossingLeft|TrafficLightType.CrossingRight))!=0?replacement.m_GroupMask1:(ushort)0 });
            }
            return result;
        }
        private bool CoveredVehiclePhase(Entity actor,Entity node,IList<FarSideLightPolicy.Approach> approaches,IList<SignalCoverageEntry> coverage)
        {
            if(!EntityManager.HasComponent<ObjectLight>(actor))return false;
            var role=EntityManager.GetComponentData<TrafficLightData>(OriginalPrefab(actor)).m_Type;
            var light=EntityManager.GetComponentData<ObjectLight>(actor);
            if(!FarSideLightPolicy.Vehicle(role)&&(role&(TrafficLightType.CrossingLeft|TrafficLightType.CrossingRight))==0)return false;
            int approach=SourceApproach(actor,approaches);
            return SignalCoveragePolicy.Covered(approach,light.m_GroupMask0,light.m_GroupMask1,
                FarSideLightPolicy.Vehicle(role),(role&(TrafficLightType.CrossingLeft|TrafficLightType.CrossingRight))!=0,coverage);
        }
        private bool EdgeMedianSignals(Entity node,bool apply)
            =>EdgeMedianSignalsForApproaches(node,apply,null);
        private bool EdgeMedianSignalsForApproaches(Entity node,bool apply,IList<FarSideLightPolicy.Approach> approaches)
        {
            if(!EntityManager.HasComponent<Node>(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))return false;
            // Reconciliation already visits two nodes per second. Defer fresh
            // lane/coverage reads until an eligible edge-owned signal exists.
            List<SignalCoverageEntry> coverage=null;
            var centre=EntityManager.GetComponentData<Node>(node).m_Position;bool pending=false;
            foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(node,true))
            {
                var edge=connection.m_Edge;if(!EntityManager.HasComponent<Edge>(edge)||!HasSignalChildren(edge))continue;
                var ends=EntityManager.GetComponentData<Edge>(edge);var other=ends.m_Start==node?ends.m_End:ends.m_Start;if(!EntityManager.HasComponent<Node>(other))continue;
                var opposite=EntityManager.GetComponentData<Node>(other).m_Position;
                CopySignalChildren(edge,signalEdgeChildren);
                foreach(var actor in signalEdgeChildren)
                {
                    if(!Live(actor)||!EntityManager.HasComponent<ObjectLight>(actor)||!EntityManager.HasComponent<PrefabRef>(actor)||!EntityManager.HasComponent<ObjectTransform>(actor)||!EntityManager.HasComponent<Owner>(actor)||EntityManager.GetComponentData<Owner>(actor).m_Owner!=edge)continue;
                    var original=OriginalPrefab(actor);if(!EntityManager.HasComponent<TrafficLightData>(original)||EntityManager.HasComponent<StreetLightData>(original))continue;
                    var point=OriginalTransform(actor).m_Position;float distance=math.distancesq(point.xz,centre.xz);
                    if(distance>2500||distance>=math.distancesq(point.xz,opposite.xz)||math.abs(point.y-centre.y)>2)continue;
                    if(coverage==null)
                    {
                        if(approaches==null)approaches=ReadApproaches(node);
                        coverage=ReadSignalCoverage(node,approaches);
                        if(coverage.Count==0&&_hiddenSignals.Count==0)return false;
                    }
                    bool hiddenHere=_hiddenSignals.TryGetValue(actor,out var hiddenNode)&&hiddenNode==node;
                    bool covered=CoveredVehiclePhase(actor,node,approaches,coverage);
                    if(covered&&!_hiddenSignals.ContainsKey(actor)){pending=true;if(apply)HideSignal(actor,node);}
                    else if(!covered&&hiddenHere){pending=true;if(apply)ReleaseHiddenSignal(actor);}
                }
            }
            return pending;
        }
        private bool SignalProp(Entity prop,Entity node)
        {
            if(!Live(prop)||!EntityManager.HasComponent<ObjectLight>(prop)||!EntityManager.HasComponent<ObjectTransform>(prop)||!EntityManager.HasComponent<PrefabRef>(prop)||!EntityManager.HasComponent<Owner>(prop)||EntityManager.GetComponentData<Owner>(prop).m_Owner!=node)return false;
            var prefab=OriginalPrefab(prop);return EntityManager.HasComponent<TrafficLightData>(prefab)&&!EntityManager.HasComponent<StreetLightData>(prefab);
        }
        private void HideSignal(Entity prop,Entity node)
        {
            if(EntityManager.HasComponent<Game.Tools.Hidden>(prop))return;
            var original=OriginalPrefab(prop);var current=EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab;
            var selected=GardenCityLightPolicy.SelectSuppressed(World.GetOrCreateSystemManaged<PrefabSystem>(),EntityManager,node,original);
            if(selected==original)return;
            var binding=EntityManager.GetComponentData<ObjectLight>(prop);
            _hiddenSignals[prop]=node;maintenance.Track(prop,node,SignalMaintenanceIndex<Entity>.Hidden);_suppressedPrefabs[prop]=new Suppressed{Original=original,Applied=selected,VehicleMask=binding.m_GroupMask0,PedestrianMask=binding.m_GroupMask1};
            if(current==selected)return;
            SetPreviewPrefab(prop,selected);Write(prop,EntityManager.GetComponentData<ObjectTransform>(prop));
        }
        private void ReleaseHiddenSignal(Entity prop)
        {
            if(!_hiddenSignals.Remove(prop))return;maintenance.Forget(prop,SignalMaintenanceIndex<Entity>.Hidden);
            if(_suppressedPrefabs.TryGetValue(prop,out var saved))
            {
                _suppressedPrefabs.Remove(prop);
                if(Live(prop)&&EntityManager.HasComponent<PrefabRef>(prop)&&EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab==saved.Applied)
                {
                    SetPreviewPrefab(prop,saved.Original);
                    if(!Mod.Saving)
                    {
                        Write(prop,EntityManager.GetComponentData<ObjectTransform>(prop));
                        if(EntityManager.HasComponent<Game.Objects.StreetLight>(prop)&&!EntityManager.HasComponent<EffectsUpdated>(prop))EntityManager.AddComponent<EffectsUpdated>(prop);
                    }
                }
            }
        }

        private readonly List<FarSideLightPolicy.Approach> _approaches=new List<FarSideLightPolicy.Approach>();

        private readonly SignalJunctionFootprintCache junctionFootprints = new SignalJunctionFootprintCache();
        private readonly SignalJunctionFootprintCache sidewalkFootprints = new SignalJunctionFootprintCache();

        private SignalJunctionFootprint ReadJunctionFootprint(Entity road, bool start, Composition composition,
            CurbLineEmissionSystem marking, out SignalJunctionFootprint sidewalk)
        {
            sidewalk = null;
            var nodeComposition = start ? composition.m_StartNode : composition.m_EndNode;
            if (!EntityManager.HasComponent<NetCompositionData>(nodeComposition) ||
                (start ? !EntityManager.HasComponent<StartNodeGeometry>(road) : !EntityManager.HasComponent<EndNodeGeometry>(road))) return null;
            var data = EntityManager.GetComponentData<NetCompositionData>(nodeComposition);
            var anchors = ReadSignalCurbAnchors(nodeComposition, marking);
            if (anchors.Count < 2) return null;
            float low = float.MaxValue, high = float.MinValue;
            foreach (var anchor in anchors) { low = math.min(low, anchor.x); high = math.max(high, anchor.x); }
            if (high - low < 1) return null;
            var geometry = start ? EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry : EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry;
            // Width describes the composition's outside edges, not the road
            // mouth's longitudinal trim. Read anchors once for both boundaries.
            if (low + data.m_Width * .5f >= .9f && data.m_Width * .5f - high >= .9f)
                sidewalk = sidewalkFootprints.Get(road, start, nodeComposition, geometry, data, -data.m_Width * .5f, data.m_Width * .5f);
            return junctionFootprints.Get(road, start, nodeComposition, geometry, data, low, high);
        }

        private List<float3> ReadSignalCurbAnchors(Entity composition, CurbLineEmissionSystem marking)
        {
            if (EntityManager.HasBuffer<NetCompositionLane>(composition))
            {
                var composed = marking.ReadCurbAnchors(composition, EntityManager.GetBuffer<NetCompositionLane>(composition, true));
                if (composed.Count >= 2) return composed;
            }
            // Native node compositions can carry pieces without a lane buffer.
            // Use the same piece inversion and offset as AddCompositionLanes.
            var anchors = new List<float3>();
            if (!EntityManager.HasBuffer<NetCompositionPiece>(composition)) return anchors;
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            foreach (var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition, true))
            {
                if ((piece.m_SectionFlags & (NetSectionFlags.Median | NetSectionFlags.Underground | NetSectionFlags.Overhead)) != 0 ||
                    !EntityManager.HasBuffer<NetPieceLane>(piece.m_Piece)) continue;
                var lanes = EntityManager.GetBuffer<NetPieceLane>(piece.m_Piece, true);
                bool inverted = (piece.m_SectionFlags & NetSectionFlags.Invert) != 0;
                foreach (var lane in lanes)
                {
                    if (!prefabs.TryGetPrefab<NetLanePrefab>(lane.m_Lane, out var prefab) || prefab == null || prefab.name != "Boarding Lane 0") continue;
                    var position = lane.m_Position; if (inverted) position.x = -position.x; position += piece.m_Offset;
                    if (math.all(math.isfinite(position)) && !anchors.Exists(p => math.abs(p.x - position.x) < .05f)) anchors.Add(position);
                }
                if (!prefabs.TryGetPrefab<NetPiecePrefab>(piece.m_Piece, out var piecePrefab) || piecePrefab == null ||
                    !piecePrefab.name.StartsWith("Sidewalk With Parking Piece ", StringComparison.Ordinal) ||
                    piecePrefab.name.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    !EntityManager.HasComponent<NetVertexMatchData>(piece.m_Piece)) continue;
                float roadHeight = float.NaN;
                foreach (var lane in lanes)
                {
                    if (!EntityManager.HasComponent<NetLaneData>(lane.m_Lane)) continue;
                    if ((EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Flags & (LaneFlags.Parking | LaneFlags.Road)) != 0)
                    { roadHeight = lane.m_Position.y; break; }
                }
                if (CurbPlacement.TryParkingCurb(piece.m_Offset, EntityManager.GetComponentData<NetVertexMatchData>(piece.m_Piece).m_Offsets.y,
                    roadHeight, inverted, out var curb) && !anchors.Exists(p => math.abs(p.x - curb.x) < .05f)) anchors.Add(curb);
            }
            return anchors;
        }

        private SignalJunctionFootprint ReadLegacyJunctionFootprint(Entity road, bool start, Composition composition, CurbLineEmissionSystem marking)
        {
            var nodeComposition = start ? composition.m_StartNode : composition.m_EndNode;
            if (!EntityManager.HasComponent<NetCompositionData>(nodeComposition) || !EntityManager.HasBuffer<NetCompositionLane>(nodeComposition) ||
                (start ? !EntityManager.HasComponent<StartNodeGeometry>(road) : !EntityManager.HasComponent<EndNodeGeometry>(road))) return null;
            var anchors = marking.ReadCurbAnchors(nodeComposition, EntityManager.GetBuffer<NetCompositionLane>(nodeComposition, true));
            if (anchors.Count < 2) return null;
            float low = float.MaxValue, high = float.MinValue;
            foreach (var anchor in anchors) { low = math.min(low, anchor.x); high = math.max(high, anchor.x); }
            if (high - low < 1) return null;
            var geometry = start ? EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry : EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry;
            return junctionFootprints.Get(road,start,nodeComposition,geometry,EntityManager.GetComponentData<NetCompositionData>(nodeComposition),low,high);
        }

        private List<FarSideLightPolicy.Approach> ReadApproachesUncached(Entity node)

        {

            _approaches.Clear();if(!EntityManager.HasBuffer<ConnectedEdge>(node)) return _approaches;

            var marking=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();marking.RefreshDefinitionsIfNeeded();

            foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(node,true))

            {

                Entity road=connection.m_Edge;

                if(!Live(road)||!EntityManager.HasComponent<Road>(road)||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road)) continue;

                var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;

                if(!start && edge.m_End!=node) continue;

                var nativeComposition=EntityManager.GetComponentData<Composition>(road);
                var composition=nativeComposition.m_Edge;

                if(!EntityManager.HasComponent<NetCompositionData>(composition)||!EntityManager.HasBuffer<NetCompositionLane>(composition)) continue;

                var data=EntityManager.GetComponentData<NetCompositionData>(composition);

                var anchors=marking.ReadCurbAnchors(composition,EntityManager.GetBuffer<NetCompositionLane>(composition,true));

                if(anchors.Count<2)
                {
                    // Highway/shoulder and Road Builder layouts may have no
                    // boarding/curb lanes. Derive support limits from actual lanes.
                    var footprint=new List<float2>();
                    foreach(var lane in EntityManager.GetBuffer<NetCompositionLane>(composition,true))
                    {
                        if(!EntityManager.HasComponent<CarLaneData>(lane.m_Lane)||!EntityManager.HasComponent<NetLaneData>(lane.m_Lane))continue;
                        var laneData=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane);
                        if(((lane.m_Flags|laneData.m_Flags)&(LaneFlags.Master|LaneFlags.Virtual|LaneFlags.Parking|LaneFlags.BicyclesOnly))!=0)continue;
                        footprint.Add(new float2(lane.m_Position.x,laneData.m_Width));
                    }
                    if(GardenCityLightPolicy.TryShoulderBounds(footprint,data.m_Width,out var lower,out var upper)){anchors.Clear();anchors.Add(new float3(lower,0,0));anchors.Add(new float3(upper,0,0));}
                }
                if(anchors.Count<2 || data.m_Width<=.1f) continue;

                anchors.Sort((a,b)=>a.x.CompareTo(b.x));

                var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);var segment=start?geometry.m_Start:geometry.m_End;

                // Boarding lanes include median islands and are not guaranteed ordered.
                // Signal supports must use the two outer curb banks.
                anchors.Sort((a,b)=>a.x.CompareTo(b.x));
                float t=start?0f:1f;

                var left=MathUtils.Position(MathUtils.Lerp(segment.m_Left,segment.m_Right,anchors[0].x/data.m_Width+.5f),t);

                var right=MathUtils.Position(MathUtils.Lerp(segment.m_Left,segment.m_Right,anchors[anchors.Count-1].x/data.m_Width+.5f),t);

                float2 outward=math.normalizesafe(MathUtils.Tangent(MathUtils.Lerp(segment.m_Left,segment.m_Right,.5f),t).xz)*(start?1f:-1f);

                if(!math.all(math.isfinite(left))||!math.all(math.isfinite(right))||math.lengthsq(outward)<.5f||math.distance(left.xz,right.xz)<1) continue;

                var incoming=new List<float3>();
                foreach(var lane in EntityManager.GetBuffer<NetCompositionLane>(composition,true))
                {
                    var flags=lane.m_Flags;if(EntityManager.HasComponent<NetLaneData>(lane.m_Lane))flags|=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Flags;
                    if((flags&(LaneFlags.Parking|LaneFlags.Master|LaneFlags.Virtual|LaneFlags.BicyclesOnly))!=0||!EntityManager.HasComponent<CarLaneData>(lane.m_Lane))continue;
                    if((flags&(start?LaneFlags.DisconnectedStart:LaneFlags.DisconnectedEnd))!=0)continue;
                    bool inverted=(lane.m_Flags&LaneFlags.Invert)!=0;if((flags&LaneFlags.Twoway)==0&&inverted!=start)continue;
                    var point=MathUtils.Position(MathUtils.Lerp(segment.m_Left,segment.m_Right,lane.m_Position.x/data.m_Width+.5f),t);
                    if(math.all(math.isfinite(point))&&!incoming.Exists(x=>math.distancesq(x.xz,point.xz)<1.44f))incoming.Add(point);
                }
                SignalJunctionFootprint sidewalk = null;
                bool wireStyle = EntityManager.HasComponent<MGRSignalStyle>(node) && GardenCityLightPolicy.IsWireStyle(EntityManager.GetComponentData<MGRSignalStyle>(node).Value);
                SignalJunctionFootprint pavement = null;
                _approaches.Add(new FarSideLightPolicy.Approach {Left=left,Right=right,JunctionCentre=EntityManager.GetComponentData<Node>(node).m_Position,HasJunctionCentre=true,Outward=outward,Incoming=incoming,LaneMasks=new List<ushort>(new ushort[incoming.Count]),Road=road,Pavement=pavement,Sidewalk=sidewalk});

            }

            BindApproachMasks(node);
            return _approaches;
        }
        private void BindApproachMasks(Entity node)
        {
            // Bind vehicle phases to their incoming road using native lane curves.
            // Corner distance alone can choose the perpendicular road at mixed widths.
            if(EntityManager.HasBuffer<Game.Net.SubLane>(node))foreach(var item in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                var lane=item.m_SubLane;if(!Live(lane)||!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasComponent<LaneSignal>(lane)||!EntityManager.HasComponent<Curve>(lane))continue;
                // Aggregate masters and secondary aliases are not physical head
                // inputs. Their masks must not be assigned to a nearby real lane.
                if(!SignalLaneBindingPolicy.Physical(EntityManager.HasComponent<Game.Net.MasterLane>(lane),
                    EntityManager.HasComponent<Game.Net.SecondaryLane>(lane)))continue;
                var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;var direction=math.normalizesafe(curve.b.xz-curve.a.xz);
                int source=-1;float best=144;
                for(int i=0;i<_approaches.Count;i++)
                {
                    var road=_approaches[i];if(math.dot(direction,road.Outward)>-.3f)continue;
                    foreach(var point in road.Incoming){float distance=math.distancesq(point.xz,curve.a.xz);if(distance<best){best=distance;source=i;}}
                }
                if(source>=0){var road=_approaches[source];ushort mask=EntityManager.GetComponentData<LaneSignal>(lane).m_GroupMask;road.VehicleMask|=mask;
                    int nearest=-1;float distance=float.MaxValue;
                    for(int index=0;index<road.Incoming.Count;index++){float d=math.distancesq(road.Incoming[index].xz,curve.a.xz);if(d<distance){nearest=index;distance=d;}}
                    if(nearest>=0&&distance<144)road.LaneMasks[nearest]|=mask;
                    _approaches[source]=road;}
            }
        }

        private bool PrimarySignal(Entity prop,IList<FarSideLightPolicy.Approach> approaches)
        {
            Entity winner=Entity.Null;float best=float.MaxValue;
            if(!EntityManager.HasComponent<Owner>(prop))return false;
            var node=EntityManager.GetComponentData<Owner>(prop).m_Owner;
            var current=OriginalTransform(prop);
            int selected=SourceApproach(prop,approaches);if(selected<0||approaches[selected].Incoming==null||approaches[selected].Incoming.Count==0)return false;
            float3 centre=float3.zero;foreach(var lane in approaches[selected].Incoming)centre+=lane;centre/=approaches[selected].Incoming.Count;
            foreach(var candidate in _props)
            {
                if(!SignalProp(candidate,node))continue;
                var prefab=OriginalPrefab(candidate);if(!EntityManager.HasComponent<TrafficLightData>(prefab)||!FarSideLightPolicy.Vehicle(EntityManager.GetComponentData<TrafficLightData>(prefab).m_Type))continue;
                var source=OriginalTransform(candidate);
                if(SourceApproach(candidate,approaches)!=selected)continue;
                // Distinct protected-turn or vehicle-phase masks need their own
                // controller even when they belong to the same road mouth.
                if(EntityManager.GetComponentData<ObjectLight>(candidate).m_GroupMask0!=EntityManager.GetComponentData<ObjectLight>(prop).m_GroupMask0)continue;
                float distance=math.distancesq(source.m_Position.xz,centre.xz);if(distance<best||(distance==best&&candidate.Index<winner.Index)){best=distance;winner=candidate;}
            }
            return winner==prop;
        }

        private readonly List<float3> _surfacePoints = new List<float3>();

        private List<float3> SurfacePoints(Entity node)

        {

            _surfacePoints.Clear();

            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return _surfacePoints;

            float nodeHeight = EntityManager.GetComponentData<Node>(node).m_Position.y;

            foreach (var connection in EntityManager.GetBuffer<ConnectedEdge>(node, true))

            {

                Entity road = connection.m_Edge;

                if (!Live(road) || !EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<EdgeGeometry>(road)) continue;

                var edge = EntityManager.GetComponentData<Edge>(road); var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);

                bool start = edge.m_Start == node; if (!start && edge.m_End != node) continue;

                var segment = start ? geometry.m_Start : geometry.m_End;

                foreach (var curve in new[] { segment.m_Left, segment.m_Right })

                {

                    var end = start ? curve.a : curve.d;

                    var inner = MathUtils.Position(curve, start ? .1f : .9f);

                    if (math.all(math.isfinite(end)) && math.abs(end.y - nodeHeight) < 3) _surfacePoints.Add(end);

                    if (math.all(math.isfinite(inner)) && math.abs(inner.y - nodeHeight) < 3) _surfacePoints.Add(inner);

                }

            }

            return _surfacePoints;

        }

        internal int PreviewRevision { get; private set; }
        private void SetPreviewPrefab(Entity actor,Entity prefab)
        {
            bool changed=EntityManager.HasComponent<PrefabRef>(actor)&&EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab!=prefab;
            if(changed)PreviewRevision++;
            NativeVisualInvalidation.SetPrefab(World,actor,prefab);
            if(changed)SignalPreviewSystem.SourceChanged(World,actor);
        }

        private void Write(Entity prop, ObjectTransform transform)
        {
            bool changed=EntityManager.HasComponent<ObjectTransform>(prop)&&!EntityManager.GetComponentData<ObjectTransform>(prop).Equals(transform);
            if(changed)PreviewRevision++;
            NativeVisualInvalidation.SetTransform(World,prop,transform);
            if(changed)SignalPreviewSystem.SourceChanged(World,prop);
        }

        private void Undo(Entity prop, Moved moved,bool serializing=false)

        {

            if (!Live(prop) || !EntityManager.HasComponent<ObjectTransform>(prop) || !EntityManager.HasComponent<PrefabRef>(prop) || (EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab != moved.Prefab && EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab != moved.AppliedPrefab)) return;
            if(_originalLampStates.TryGetValue(prop,out var originalLampState))
            {
                if(EntityManager.HasComponent<Game.Objects.StreetLight>(prop))EntityManager.SetComponentData(prop,originalLampState);
                _originalLampStates.Remove(prop);
            }
            var owner=EntityManager.HasComponent<Owner>(prop)?EntityManager.GetComponentData<Owner>(prop).m_Owner:Entity.Null;
            bool ownsPose=FarSideLightPolicy.CanRestorePose(owner,moved.Owner,EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab,moved.Prefab,moved.AppliedPrefab,EntityManager.GetComponentData<ObjectTransform>(prop).Equals(moved.Applied));

            if(EntityManager.HasComponent<ObjectLight>(prop)){var light=EntityManager.GetComponentData<ObjectLight>(prop);if(light.m_GroupMask1==moved.AppliedPedMask){light.m_GroupMask1=moved.OriginalPedMask;EntityManager.SetComponentData(prop,light);}}
            if(EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab==moved.AppliedPrefab)SetPreviewPrefab(prop,moved.Prefab);
            if(ownsPose&&moved.HadRelative&&!EntityManager.HasComponent<Game.Objects.Relative>(prop))NativeVisualInvalidation.RestoreRelative(World,prop,moved.OriginalRelative);
            if (ownsPose) NativeVisualInvalidation.SetTransform(World,prop,moved.Original);
            // A save-time prefab swap is still a live-world visual change.
            // Notify native batching/effects even if another owner kept its pose.
            NativeVisualInvalidation.Mark(World,prop);

        }

        internal void Restore() { ClearSignalLayouts(); nativeSignalChildren.BeginPass(); waitingSignalDefinitions.Clear();signalPresentationChecks.Clear(); Dependency.Complete();_reconcileNodes.Clear();_reconcileSet.Clear();_nextReconcile=0;ClearGeneratedLamps(); var hidden=new List<Entity>(_hiddenSignals.Keys);foreach(var prop in hidden)ReleaseHiddenSignal(prop); foreach (var item in _moved) Undo(item.Key, item.Value);foreach(var item in _lamps)Undo(item.Key,item.Value);_lamps.Clear(); _moved.Clear(); maintenance.Clear(); signalTickets.Clear(); ClearLampTracking(); Requested.Clear(); }

        public void PreSerialize(Context context)
        {
            Dependency.Complete();ClearGeneratedLamps(true);var hidden=new List<Entity>(_hiddenSignals.Keys);foreach(var prop in hidden)ReleaseHiddenSignal(prop);Mod.Log.Info("Signal save preparation: restoring "+_moved.Count+" controlled props.");
            foreach(var item in _moved)Undo(item.Key,item.Value,true);
            foreach(var item in _lamps)Undo(item.Key,item.Value,true);_lamps.Clear();
            int remaining=0;
            foreach(var item in _moved)if(Live(item.Key)&&EntityManager.HasComponent<PrefabRef>(item.Key)&&EntityManager.GetComponentData<PrefabRef>(item.Key).m_Prefab==item.Value.AppliedPrefab&&item.Value.AppliedPrefab!=item.Value.Prefab)remaining++;
            _moved.Clear();maintenance.Clear();signalTickets.Clear();nativeSignalChildren.BeginPass();waitingSignalDefinitions.Clear();signalPresentationChecks.Clear();ClearLampTracking();Requested.Clear();_initial=true;
            Mod.Log.Info("Signal save preparation complete: remaining temporary references="+remaining);
        }

    }

}
