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

    public partial class CrosswalkUpgradeSystem

    {

        internal void ResetRoadAppearance(Entity road)
        {
            if(!EntityManager.HasComponent<Edge>(road))return;
            var edge=EntityManager.GetComponentData<Edge>(road);
            foreach(var node in new[]{edge.m_Start,edge.m_End})
            {
                if(EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node))
                {var intents=EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node);for(int i=intents.Length-1;i>=0;i--)if(LegacyIntentRoad(node,intents[i].Direction)==road)intents.RemoveAt(i);}
            }
            if(HasChoices&&EntityManager.HasComponent<EdgeGeometry>(road))
            {
                var choices=EntityManager.GetBuffer<MGRCrosswalkChoice>(_choices);
                for (int i = choices.Length - 1; i >= 0; i--)
                {
                    var choice = choices[i];
                    var axis = math.normalizesafe(choice.Axis);
                    var path = new Bezier4x3(choice.Center-axis, choice.Center-axis/3f,
                        choice.Center+axis/3f, choice.Center+axis);
                    if ((TryResolveNodeCrossing(edge.m_Start, path, out var startRoad) && startRoad == road) ||
                        (TryResolveNodeCrossing(edge.m_End, path, out var endRoad) && endRoad == road))
                        choices.RemoveAt(i);
                }
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

            if (!TryResolveNodeCrossing(owner, curve, out road)) return false;
            node = owner;
            return true;
        }

        private bool TryResolveNodeCrossing(Entity node, Bezier4x3 curve, out Entity road)
        {
            road = Entity.Null;
            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return false;
            float best = float.MaxValue;
            bool tied = false;
            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                var candidate = entry.m_Edge;
                if (!EntityManager.HasComponent<Road>(candidate) || EntityManager.HasComponent<Deleted>(candidate) ||
                    EntityManager.HasComponent<Game.Tools.Temp>(candidate) || !EntityManager.HasComponent<Edge>(candidate) ||
                    !EntityManager.HasComponent<EdgeGeometry>(candidate)) continue;
                var edge = EntityManager.GetComponentData<Edge>(candidate);
                bool start = edge.m_Start == node;
                if (start == (edge.m_End == node)) continue;
                if (!CrosswalkApproachPlacement.TryScore(EntityManager.GetComponentData<EdgeGeometry>(candidate),
                    start, curve, out float score)) continue;
                if (CrosswalkPreviewChoicePolicy.Better(score, best))
                {
                    best = score;
                    road = candidate;
                    tied = false;
                }
                else if (candidate != road && CrosswalkPreviewChoicePolicy.Tied(score, best)) tied = true;
            }
            if (tied) road = Entity.Null;
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

        private Entity LegacyIntentRoad(Entity node, float3 direction)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return Entity.Null;
            Entity bestRoad = Entity.Null;
            var match = new CrosswalkIntentPolicy.LegacyMatch();
            foreach (var connection in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                var candidate = connection.m_Edge;
                if (!EntityManager.HasComponent<Road>(candidate) || EntityManager.HasComponent<Deleted>(candidate) ||
                    EntityManager.HasComponent<Game.Tools.Temp>(candidate)) continue;
                if (!ApproachDirection(candidate, node, out var outward)) return Entity.Null;
                float score = CrosswalkIntentPolicy.Score(direction, outward);
                if (match.Consider(score)) bestRoad = candidate;
            }
            return match.Unique ? bestRoad : Entity.Null;
        }

        private int FindLegacyIntent(Entity road, Entity node, DynamicBuffer<MGRCrosswalkApproachIntent> buffer)
        {
            if (!ApproachDirection(road,node,out var outward)) return -1;
            int best = -1;
            float bestScore = -1f;
            for (int i = 0; i < buffer.Length; i++)
            {
                var entry = buffer[i];
                float score = CrosswalkIntentPolicy.Score(entry.Direction,outward);
                if (score <= bestScore || LegacyIntentRoad(node,entry.Direction) != road) continue;
                best = i;
                bestScore = score;
            }
            return best;
        }

        private void StoreNodeIntent(Entity road, Entity node, byte appearance)
        {
            // Keep directional entries for legacy replacement recovery only.
            // The persisted road end retains its choice even at a parallel fork.
            if (!EntityManager.HasComponent<Node>(node) || !ApproachDirection(road,node,out var direction) ||
                LegacyIntentRoad(node,direction) != road) return;
            if (!EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node))
                EntityManager.AddBuffer<MGRCrosswalkApproachIntent>(node);
            var buffer = EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node);
            int index = FindLegacyIntent(road,node,buffer);
            var value = new MGRCrosswalkApproachIntent {Direction=direction,Appearance=appearance};
            if (index >= 0) buffer[index] = value;
            else if (buffer.Length < 32) buffer.Add(value);
        }

        private bool NodeIntent(Entity road, Entity node, out byte appearance)
        {
            appearance = 0;
            if (!EntityManager.HasComponent<Edge>(road)) return false;
            var edge = EntityManager.GetComponentData<Edge>(road);
            bool start = edge.m_Start == node;
            if (!start && edge.m_End != node) return false;
            bool invisible = EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road) &&
                EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start);
            bool line = false;
            bool style = EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road) &&
                EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road).TryGet(start,out line);
            if (invisible || style)
            {
                appearance = invisible ? (byte)2 : line ? (byte)1 : (byte)0;
                return true;
            }

            if (!EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node)) return false;
            var buffer = EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node,true);
            int index = FindLegacyIntent(road,node,buffer);
            if (index < 0) return false;
            appearance = buffer[index].Appearance;
            return true;
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

                    if(!style.TryGet(start,out bool line) || (EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node) &&
                        FindLegacyIntent(road,node,EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node,true)) >= 0)) continue;

                    bool invisible=EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road) && EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start);

                    StoreNodeIntent(road,node,invisible?(byte)2:line?(byte)1:(byte)0);

                }

            }

        }

        // Exact read-only early-return predicate of PruneNodeIntents.
        internal bool HasNodeIntentsForPruning(Entity node)
            => EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node)
                && EntityManager.HasBuffer<ConnectedEdge>(node)
                && EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node, true).Length != 0;

        internal void PruneNodeIntents(Entity node) => PruneNodeIntents(node, null);

        internal void PruneNodeIntents(Entity node, System.Action beforeWrite)
        {
            if (!EntityManager.HasBuffer<MGRCrosswalkApproachIntent>(node)
                || !EntityManager.HasBuffer<ConnectedEdge>(node)) return;
            if (EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node, true).Length == 0) return;

            // Missing geometry is not evidence that a saved approach was removed.
            // The next native owner change can reconsider it; no retry sweep is added.
            var directions = new System.Collections.Generic.List<float3>();
            {
                var connections = EntityManager.GetBuffer<ConnectedEdge>(node, true);
                if (connections.Length == 0) return;
                foreach (var entry in connections)
                {
                    var road = entry.m_Edge;
                    // A stale/deleted entry still in ConnectedEdge means the
                    // native connection list has not finished settling.
                    if (!EntityManager.Exists(road)
                        || EntityManager.HasComponent<Deleted>(road)
                        || EntityManager.HasComponent<Game.Tools.Temp>(road)
                        || !ApproachDirection(road, node, out var direction)) return;
                    directions.Add(direction);
                }
            }

            // Read first: an intact choice must not dirty the owner chunk.
            bool remove = false;
            {
                var intents = EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node, true);
                foreach (var intent in intents)
                {
                    bool found = false;
                    foreach (var direction in directions)
                        if (CrosswalkIntentPolicy.Matches(direction, intent.Direction)) { found = true; break; }
                    if (!found) { remove = true; break; }
                }
            }
            if (!remove) return;

            // Read-only no-ops must not discard the whole marking cohort's
            // observed inputs. Close borrowed scopes only before the real write.
            beforeWrite?.Invoke();
            var buffer = EntityManager.GetBuffer<MGRCrosswalkApproachIntent>(node);
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                bool found = false;
                foreach (var direction in directions)
                    if (CrosswalkIntentPolicy.Matches(direction, buffer[i].Direction)) { found = true; break; }
                if (!found) buffer.RemoveAt(i);
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
