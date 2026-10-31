using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game.Common;

using Game.Net;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    // Saved intent belongs to a road end, not a transient generated lane or curve.

    public struct MGRCrosswalkRoadStyle : IComponentData, ISerializable

    {

        public byte Overrides, LineEnds;

        internal void Set(bool start, bool line) { byte bit = (byte)(start ? 1 : 2); Overrides |= bit; if (line) LineEnds |= bit; else LineEnds &= (byte)~bit; }

        internal bool TryGet(bool start, out bool line) { byte bit = (byte)(start ? 1 : 2); line = (LineEnds & bit) != 0; return (Overrides & bit) != 0; }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Overrides); writer.Write(LineEnds); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Overrides); reader.Read(out LineEnds); }

    }

    // Separate component: the existing two-byte road-style save layout is unchanged.

    public struct MGRInvisibleCrosswalkStyle : IComponentData, ISerializable

    {

        public byte Ends;

        internal bool Has(bool start) => (Ends & (start ? 1 : 2)) != 0;

        internal void Set(bool start, bool invisible) { byte bit = (byte)(start ? 1 : 2); if (invisible) Ends |= bit; else Ends &= (byte)~bit; }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Ends); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Ends); }

    }

    public struct MGRHiddenCrosswalkPrevious : IComponentData, ISerializable

    {

        public byte Have,Line;

        public void Serialize<T>(T writer) where T:IWriter {writer.Write(Have);writer.Write(Line);}

        public void Deserialize<T>(T reader) where T:IReader {reader.Read(out Have);reader.Read(out Line);}

    }

    public struct MGRCrosswalkApproachIntent : IBufferElementData, ISerializable

    {

        public float3 Direction; public byte Appearance;

        public void Serialize<T>(T writer) where T : IWriter { writer.Write(Direction); writer.Write(Appearance); }

        public void Deserialize<T>(T reader) where T : IReader { reader.Read(out Direction); reader.Read(out Appearance); }

    }

    internal static class CrosswalkIntentPolicy

    {

        internal static float Score(float3 a,float3 b)

        {

            if(!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || math.lengthsq(a.xz)<.001f || math.lengthsq(b.xz)<.001f) return -1;

            return math.dot(math.normalize(a.xz),math.normalize(b.xz));

        }

        internal static bool Matches(float3 a,float3 b) => Score(a,b) >= .95f;

    }

    internal static class CrosswalkApproachPlacement

    {

        internal static bool TryScore(EdgeGeometry geometry, bool start, Bezier4x3 crossing, out float score)

        {

            score = float.MaxValue; var segment = start ? geometry.m_Start : geometry.m_End;

            var left = start ? segment.m_Left.a : segment.m_Left.d; var right = start ? segment.m_Right.a : segment.m_Right.d;

            var axis = math.normalizesafe((right - left).xz); var tangent = math.normalizesafe(MathUtils.Tangent(crossing, .5f).xz);

            if (math.lengthsq(axis) < .5f || math.abs(math.dot(axis, tangent)) < .7f) return false;

            var point = MathUtils.Position(crossing, .5f);

            if (!math.all(math.isfinite(point)) || math.abs(point.y - (left.y + right.y) * .5f) > 3f) return false;

            float width = math.distance(left.xz, right.xz); if (!math.isfinite(width) || width < .1f) return false;

            float along = math.clamp(math.dot(point.xz - left.xz, axis), 0f, width);

            score = math.distance(point.xz, left.xz + axis * along);

            return math.isfinite(score) && score <= 12f;

        }

    }

    public partial class CrosswalkUpgradeSystem

    {

        internal void ResetRoadAppearance(Entity road)
        {
            if(!EntityManager.HasComponent<Edge>(road))return;
            var edge=EntityManager.GetComponentData<Edge>(road);
            foreach(var node in new[]{edge.m_Start,edge.m_End})
            {
                if(ApproachDirection(road,node,out var direction)&&EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node))
                {var intents=EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node);for(int i=intents.Length-1;i>=0;i--)if(CrosswalkIntentPolicy.Matches(direction,intents[i].Direction))intents.RemoveAt(i);}
            }
            if(HasChoices&&EntityManager.HasComponent<EdgeGeometry>(road))
            {
                var choices=EntityManager.GetBuffer<MGRCrosswalkChoice>(_choices);
                for(int i=choices.Length-1;i>=0;i--){var c=choices[i];var axis=math.normalizesafe(c.Axis);var path=new Bezier4x3(c.Center-axis,c.Center-axis/3,c.Center+axis/3,c.Center+axis);bool owned=false;foreach(var node in new[]{edge.m_Start,edge.m_End}){if(!EntityManager.HasBuffer<ConnectedEdge>(node))continue;Entity best=Entity.Null;float limit=float.MaxValue;foreach(var connected in EntityManager.GetBuffer<ConnectedEdge>(node,true)){var candidate=connected.m_Edge;if(!EntityManager.HasComponent<Edge>(candidate)||!EntityManager.HasComponent<EdgeGeometry>(candidate))continue;bool start=EntityManager.GetComponentData<Edge>(candidate).m_Start==node;if(CrosswalkApproachPlacement.TryScore(EntityManager.GetComponentData<EdgeGeometry>(candidate),start,path,out float score)&&score<limit){limit=score;best=candidate;}}owned|=best==road;}if(owned)choices.RemoveAt(i);}
            }
            if(EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road))EntityManager.RemoveComponent<MGRCrosswalkRoadStyle>(road);
            if(EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road))EntityManager.RemoveComponent<MGRInvisibleCrosswalkStyle>(road);
            if(EntityManager.HasComponent<MGRHiddenCrosswalkPrevious>(road))EntityManager.RemoveComponent<MGRHiddenCrosswalkPrevious>(road);
            SegmentUpgradePolicy.QueuePaint(EntityManager,road);
        }

        internal bool TryResolveApproach(Entity source, out Entity road, out Entity node)

        {

            road = Entity.Null; node = Entity.Null;

            if (!EntityManager.Exists(source) || !EntityManager.HasComponent<Owner>(source) || !EntityManager.HasComponent<Curve>(source)) return false;

            var owner = EntityManager.GetComponentData<Owner>(source).m_Owner; var curve = EntityManager.GetComponentData<Curve>(source).m_Bezier;

            if (EntityManager.HasComponent<Road>(owner) && EntityManager.HasComponent<Edge>(owner) && EntityManager.HasComponent<EdgeGeometry>(owner))

            {

                var edge = EntityManager.GetComponentData<Edge>(owner); road = owner;

                node = NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(owner), MathUtils.Position(curve, .5f)) ? edge.m_End : edge.m_Start; return true;

            }

            if (!EntityManager.HasBuffer<ConnectedEdge>(owner)) return false;

            float best = float.MaxValue;

            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(owner, true))

            {

                var candidate = entry.m_Edge;

                if (!EntityManager.HasComponent<Road>(candidate) || EntityManager.HasComponent<Deleted>(candidate) || EntityManager.HasComponent<Game.Tools.Temp>(candidate) || !EntityManager.HasComponent<Edge>(candidate) || !EntityManager.HasComponent<EdgeGeometry>(candidate)) continue;

                var edge = EntityManager.GetComponentData<Edge>(candidate); bool start = edge.m_Start == owner; if (!start && edge.m_End != owner) continue;

                if (CrosswalkApproachPlacement.TryScore(EntityManager.GetComponentData<EdgeGeometry>(candidate), start, curve, out float score) && score < best) { best = score; road = candidate; node = owner; }

            }

            return road != Entity.Null;

        }

        private bool ApproachDirection(Entity road,Entity node,out float3 direction)

        {

            direction=default;

            if(!EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<Curve>(road)) return false;

            var edge=EntityManager.GetComponentData<Edge>(road);var curve=EntityManager.GetComponentData<Curve>(road).m_Bezier;

            if(edge.m_Start==node) direction=MathUtils.Tangent(curve,0);

            else if(edge.m_End==node) direction=-MathUtils.Tangent(curve,1); else return false;

            return math.all(math.isfinite(direction)) && math.lengthsq(direction.xz)>.001f;

        }

        private void StoreNodeIntent(Entity road,Entity node,byte appearance)

        {

            if(!EntityManager.HasComponent<Node>(node) || !ApproachDirection(road,node,out var direction)) return;

            if(!EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node)) EntityManager.AddBuffer<MGRCrosswalkApproachIntent>(node);

            var buffer=EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node);

            int best=-1;float score=.95f;

            for(int i=0;i<buffer.Length;i++) { float s=CrosswalkIntentPolicy.Score(buffer[i].Direction,direction);if(s>=score) {score=s;best=i;} }

            var value=new MGRCrosswalkApproachIntent {Direction=direction,Appearance=appearance};

            if(best>=0) buffer[best]=value; else if(buffer.Length<32) buffer.Add(value);

        }

        private bool NodeIntent(Entity road,Entity node,out byte appearance)

        {

            appearance=0;

            if(!EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node) || !ApproachDirection(road,node,out var direction)) return false;

            int best=-1;float score=.95f;var buffer=EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node,true);

            for(int i=0;i<buffer.Length;i++) {float s=CrosswalkIntentPolicy.Score(buffer[i].Direction,direction);if(s>=score){score=s;best=i;}}

            if(best<0) return false; appearance=buffer[best].Appearance;return true;

        }

        internal void SeedNodeIntents()

        {

            var query=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<MGRCrosswalkRoadStyle>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());

            using(var roads=query.ToEntityArray(Unity.Collections.Allocator.Temp)) foreach(var road in roads)

            {

                var edge=EntityManager.GetComponentData<Edge>(road);var style=EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road);

                foreach(bool start in new[]{true,false})

                {

                    var node=start?edge.m_Start:edge.m_End;

                    if(NodeIntent(road,node,out var existing) || !style.TryGet(start,out bool line)) continue;

                    bool invisible=EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road) && EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start);

                    StoreNodeIntent(road,node,invisible?(byte)2:line?(byte)1:(byte)0);

                }

            }

        }

        internal void PruneNodeIntents(Entity node)

        {

            if(!EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node) || !EntityManager.HasBuffer<ConnectedEdge>(node)) return;

            var connections=EntityManager.GetBuffer<ConnectedEdge>(node,true);

            var buffer=EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node);

            for(int i=buffer.Length-1;i>=0;i--)

            {

                bool found=false;

                foreach(var entry in connections) if(!EntityManager.HasComponent<Deleted>(entry.m_Edge) && !EntityManager.HasComponent<Game.Tools.Temp>(entry.m_Edge) && ApproachDirection(entry.m_Edge,node,out var direction) && CrosswalkIntentPolicy.Matches(direction,buffer[i].Direction)) {found=true;break;}

                if(!found) buffer.RemoveAt(i);

            }

        }

        internal bool IsSourceSelected(Entity source)

        {

            if (TryResolveApproach(source, out var road, out var node) && NodeIntent(road,node,out byte appearance)) return appearance==1;

            if (TryResolveApproach(source, out road, out node) && EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road))

            {

                var edge = EntityManager.GetComponentData<Edge>(road);

                if (EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road).TryGet(edge.m_Start == node, out bool line)) return line;

            }

            return EntityManager.HasComponent<Curve>(source) && IsSelected(EntityManager.GetComponentData<Curve>(source).m_Bezier);

        }

        internal bool IsSourceInvisible(Entity source)

        {

            if(TryResolveApproach(source,out var road,out var node) && NodeIntent(road,node,out byte appearance)) return appearance==2;

            return TryResolveApproach(source, out road, out node) && EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road)

                && EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(EntityManager.GetComponentData<Edge>(road).m_Start == node);

        }

        private void SetInvisible(Entity road, Entity node, bool invisible)

        {

            if (!EntityManager.HasComponent<Edge>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Game.Tools.Temp>(road)) return;

            var edge = EntityManager.GetComponentData<Edge>(road); if (node != edge.m_Start && node != edge.m_End) return;

            bool has = EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road);

            if (!has && !invisible) return;

            var style = has ? EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road) : default;

            style.Set(edge.m_Start == node, invisible);

            if (!has) EntityManager.AddComponent<MGRInvisibleCrosswalkStyle>(road);

            EntityManager.SetComponentData(road, style);

        }

        private void RememberHiddenAppearance(Entity road,Entity node)

        {

            if(!EntityManager.HasComponent<Edge>(road)) return;var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;byte bit=(byte)(start?1:2);

            if(EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road) && EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start)) return;

            bool line=false;

            if(NodeIntent(road,node,out byte appearance)) line=appearance==1;

            else if(EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road)) EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road).TryGet(start,out line);

            var previous=EntityManager.HasComponent<MGRHiddenCrosswalkPrevious>(road)?EntityManager.GetComponentData<MGRHiddenCrosswalkPrevious>(road):default;

            previous.Have|=bit;if(line) previous.Line|=bit;else previous.Line&=(byte)~bit;

            if(!EntityManager.HasComponent<MGRHiddenCrosswalkPrevious>(road)) EntityManager.AddComponent<MGRHiddenCrosswalkPrevious>(road);

            EntityManager.SetComponentData(road,previous);

        }

        internal void RestoreHiddenAppearance(Entity road,Entity node)

        {

            if(!EntityManager.HasComponent<Edge>(road)) return;var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;byte bit=(byte)(start?1:2);

            bool hidden=EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road)&&EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start);

            if(!hidden && (!NodeIntent(road,node,out byte appearance)||appearance!=2)) return;

            var previous=EntityManager.HasComponent<MGRHiddenCrosswalkPrevious>(road)?EntityManager.GetComponentData<MGRHiddenCrosswalkPrevious>(road):default;

            bool line=(previous.Have&bit)!=0 && (previous.Line&bit)!=0;

            SetApproachAppearance(road,node,line,false);previous.Have&=(byte)~bit;

            if(EntityManager.HasComponent<MGRHiddenCrosswalkPrevious>(road))

            {

                if(previous.Have==0) EntityManager.RemoveComponent<MGRHiddenCrosswalkPrevious>(road);else EntityManager.SetComponentData(road,previous);

            }

        }

        internal void SetApproachAppearance(Entity road, Entity node, bool line, bool invisible)

        {

            if(invisible) RememberHiddenAppearance(road,node);

            SetApproach(road, node, line); // Explicit native/line choices clear invisible paint.

            if (invisible) { SetInvisible(road, node, true); StoreNodeIntent(road,node,2); }
            foreach(var owner in new[]{road,node})if(EntityManager.HasBuffer<Game.Net.SubLane>(owner))foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))IntersectionPaintSystem.RequestSource(entry.m_SubLane);

        }

        internal void SetApproach(Entity road, Entity node, bool line)

        {

            if (!EntityManager.Exists(road) || !EntityManager.HasComponent<Edge>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Game.Tools.Temp>(road)) return;

            var edge = EntityManager.GetComponentData<Edge>(road); if (node != edge.m_Start && node != edge.m_End) return;

            SetInvisible(road, node, false);

            StoreNodeIntent(road,node,line?(byte)1:(byte)0);

            bool has = EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road);

            var style = has ? EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road) : default;

            style.Set(edge.m_Start == node, line);

            if (!has) EntityManager.AddComponent<MGRCrosswalkRoadStyle>(road);

            EntityManager.SetComponentData(road, style);

            // Existing lanes need immediate reconciliation. New/rebuilt lanes are picked

            // up by IntersectionPaintSystem's changed-curve and changed-SubLane queries.

            foreach (var owner in new[] { road, node }) if (EntityManager.HasBuffer<Game.Net.SubLane>(owner))

                    foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true)) IntersectionPaintSystem.RequestSource(entry.m_SubLane);

            Mod.Debug("Crosswalk road-end paint choice: " + (line ? "two lines" : "native") + "; road=" + road + "; node=" + node);

        }

    }

}
