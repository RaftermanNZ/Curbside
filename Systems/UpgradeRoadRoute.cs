using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal readonly struct UpgradeRoadLink
    {
        internal readonly Entity Road, Start, End;
        internal readonly float Length;

        internal UpgradeRoadLink(Entity road, Entity start, Entity end, float length)
        { Road = road; Start = start; End = end; Length = length; }

        internal Entity Other(Entity node) => node == Start ? End : Start;
        internal UpgradeRoadStep From(Entity node) => new UpgradeRoadStep(Road, node, Other(node), node == Start);
    }

    internal readonly struct UpgradeRoadStep
    {
        internal readonly Entity Road, From, To;
        internal readonly bool Forward;

        internal UpgradeRoadStep(Entity road, Entity from, Entity to, bool forward)
        { Road = road; From = from; To = to; Forward = forward; }

        internal bool CarrySide(bool startLeft, UpgradeRoadStep first) => startLeft == (Forward == first.Forward);
    }

    internal readonly struct UpgradeRoadPoint
    {
        internal readonly Entity Owner;
        internal readonly UpgradeRoadLink Link;
        internal readonly float Progress;
        internal bool IsRoad => Link.Road != Entity.Null;

        internal UpgradeRoadPoint(Entity owner, UpgradeRoadLink link = default, float progress = 0f)
        { Owner = owner; Link = link; Progress = progress; }

        internal Entity FirstNode => IsRoad ? Link.Start : Owner;
        internal Entity SecondNode => IsRoad ? Link.End : Owner;
        internal float ToFirst => Progress * Link.Length;
        internal float ToSecond => (1f - Progress) * Link.Length;
    }

    internal static class UpgradeLineRoute
    {
        internal static byte RoleOnStep(byte startRole, UpgradeRoadStep first, UpgradeRoadStep step)
        {
            if (startRole != 1 && startRole != 2) return startRole;
            bool left = step.CarrySide(startRole == 1, first);
            return left ? (byte)1 : (byte)2;
        }

        internal static bool Matches(UpgradeDragTarget anchor, bool anchorIsNode, UpgradeDragTarget candidate,
            bool candidateIsNode, byte role, bool exact) => anchorIsNode == candidateIsNode && candidate.Role == role
            && candidate.Individual == anchor.Individual && (candidate.Part != Entity.Null) == (anchor.Part != Entity.Null)
            && (!exact || candidate.Part == anchor.Part);
    }

    internal interface IUpgradeRoadGraph
    {
        float3 Position(Entity node);
        void ReadLinks(Entity node, List<UpgradeRoadLink> output);
    }

    internal readonly struct UpgradeNetworkGraph : IUpgradeRoadGraph
    {
        private readonly EntityManager _manager;
        internal UpgradeNetworkGraph(EntityManager manager) => _manager = manager;

        public float3 Position(Entity node) => _manager.GetComponentData<Node>(node).m_Position;

        internal bool TryPoint(Entity owner, float3 hit, out UpgradeRoadPoint point)
        {
            point = default;
            if (!UpgradeDragTarget.Live(_manager, owner) || !math.all(math.isfinite(hit))) return false;
            if (_manager.HasComponent<Node>(owner))
            {
                point = new UpgradeRoadPoint(owner);
                return true;
            }
            if (!TryLink(owner, out var link)) return false;
            var curve = _manager.GetComponentData<Curve>(owner).m_Bezier;
            MathUtils.Distance(curve, hit, out float progress);
            if (!math.isfinite(progress)) return false;
            point = new UpgradeRoadPoint(owner, link, math.saturate(progress));
            return true;
        }

        internal Entity NearestNode(Entity road, float3 position)
        {
            if (!TryPoint(road, position, out var point) || !point.IsRoad) return Entity.Null;
            return point.Progress <= .5f ? point.Link.Start : point.Link.End;
        }

        public void ReadLinks(Entity node, List<UpgradeRoadLink> output)
        {
            if (!_manager.HasBuffer<ConnectedEdge>(node)) return;
            foreach (var edge in _manager.GetBuffer<ConnectedEdge>(node, true))
                if (TryLink(edge.m_Edge, out var link) && (node == link.Start || node == link.End))
                    output.Add(link);
        }

        internal bool IsCurrent(UpgradeRoadStep step)
            => TryLink(step.Road, out var link)
                && (step.Forward ? link.Start == step.From && link.End == step.To
                    : link.End == step.From && link.Start == step.To);

        private bool TryLink(Entity road, out UpgradeRoadLink link)
        {
            link = default;
            if (!UpgradeDragTarget.Live(_manager, road) || !_manager.HasComponent<Road>(road)
                || !_manager.HasComponent<Edge>(road) || !_manager.HasComponent<Curve>(road)) return false;
            var edge = _manager.GetComponentData<Edge>(road);
            if (edge.m_Start == edge.m_End || !UpgradeDragTarget.Live(_manager, edge.m_Start)
                || !UpgradeDragTarget.Live(_manager, edge.m_End) || !_manager.HasComponent<Node>(edge.m_Start)
                || !_manager.HasComponent<Node>(edge.m_End)) return false;
            var curve = _manager.GetComponentData<Curve>(road);
            float length = math.max(curve.m_Length, math.distance(Position(edge.m_Start).xz, Position(edge.m_End).xz));
            if (!math.isfinite(length) || length <= 0f) return false;
            link = new UpgradeRoadLink(road, edge.m_Start, edge.m_End, length);
            return true;
        }
    }

    internal sealed class UpgradeRoadRoute
    {
        // Searches only the connected network. Limit the work from a far or disconnected hover.
        private const int SearchLimit = 2048;
        private readonly Dictionary<Entity, Visit> _best = new Dictionary<Entity, Visit>();
        private readonly SortedSet<Visit> _open = new SortedSet<Visit>();
        private readonly List<UpgradeRoadLink> _links = new List<UpgradeRoadLink>();

        private readonly List<UpgradeRoadStep> _continuation = new List<UpgradeRoadStep>();
        private readonly HashSet<Entity> _traveledRoads = new HashSet<Entity>();
        private readonly HashSet<Entity> _traveledNodes = new HashSet<Entity>();

        private readonly struct Visit : IComparable<Visit>
        {
            internal readonly Entity Node;
            internal readonly float Distance, Estimate;
            internal readonly UpgradeRoadStep Incoming;

            internal Visit(Entity node, float distance, float estimate, UpgradeRoadStep incoming)
            { Node = node; Distance = distance; Estimate = estimate; Incoming = incoming; }

            public int CompareTo(Visit other)
            {
                int order = Estimate.CompareTo(other.Estimate);
                if (order == 0) order = Distance.CompareTo(other.Distance);
                if (order == 0) order = Node.Index.CompareTo(other.Node.Index);
                return order == 0 ? Node.Version.CompareTo(other.Node.Version) : order;
            }
        }

        // Keep a connected stretch that the user already traced, even when a
        // shorter route opens up farther ahead. Moving back onto it truncates it.
        internal bool Follow<T>(T graph, UpgradeRoadPoint start, UpgradeRoadPoint previous, UpgradeRoadPoint end,
            List<UpgradeRoadStep> steps)
            where T : IUpgradeRoadGraph
        {
            if (steps.Count <= 1 || end.Owner == start.Owner || end.Owner == steps[0].From)
                return Find(graph, start, end, steps);

            for (int i = 0; i < steps.Count; i++)
            {
                if (end.Owner != steps[i].Road && end.Owner != steps[i].To) continue;
                steps.RemoveRange(i + 1, steps.Count - i - 1);
                return true;
            }

            if (Find(graph, previous, end, _continuation))
            {
                int keep = steps.Count;
                int appendFrom = 0;
                var tail = steps[keep - 1];
                if (previous.IsRoad && tail.Road == previous.Owner && _continuation.Count != 0
                    && _continuation[0].Road == previous.Owner)
                {
                    if (_continuation[0].Forward != tail.Forward) keep--;
                    appendFrom++;
                }

                _traveledRoads.Clear();
                _traveledNodes.Clear();
                _traveledNodes.Add(steps[0].From);
                for (int i = 0; i < keep; i++)
                {
                    _traveledRoads.Add(steps[i].Road);
                    _traveledNodes.Add(steps[i].To);
                }

                bool returns = false;
                for (int i = appendFrom; i < _continuation.Count; i++)
                {
                    var step = _continuation[i];
                    // The far end of the last road is beyond the pointer.
                    bool reachesNode = !end.IsRoad || step.Road != end.Owner;
                    if (_traveledRoads.Contains(step.Road) || (reachesNode && _traveledNodes.Contains(step.To)))
                    {
                        returns = true;
                        break;
                    }
                }
                if (!returns)
                {
                    steps.RemoveRange(keep, steps.Count - keep);
                    for (int i = appendFrom; i < _continuation.Count; i++) steps.Add(_continuation[i]);
                    return true;
                }
            }

            return Find(graph, start, end, steps);
        }

        internal bool Find<T>(T graph, UpgradeRoadPoint start, UpgradeRoadPoint end, List<UpgradeRoadStep> steps)
            where T : IUpgradeRoadGraph
        {
            steps.Clear();
            if (start.Owner == end.Owner)
            {
                if (start.IsRoad) steps.Add(start.Link.From(start.Progress <= end.Progress ? start.Link.Start : start.Link.End));
                return true;
            }

            _best.Clear();
            _open.Clear();
            var goalFirst = graph.Position(end.FirstNode).xz;
            var goalSecond = graph.Position(end.SecondNode).xz;
            Offer(start.FirstNode, start.ToFirst, default);
            if (start.IsRoad) Offer(start.SecondNode, start.ToSecond, default);

            int visited = 0;
            Entity goalNode = Entity.Null;
            float bestGoal = float.PositiveInfinity;
            while (_open.Count != 0 && visited++ < SearchLimit)
            {
                var current = _open.Min;
                if (current.Estimate >= bestGoal) break;
                _open.Remove(current);
                float finish = current.Node == end.FirstNode ? end.ToFirst
                    : current.Node == end.SecondNode ? end.ToSecond : float.PositiveInfinity;
                if (current.Distance + finish < bestGoal)
                {
                    bestGoal = current.Distance + finish;
                    goalNode = current.Node;
                }

                _links.Clear();
                graph.ReadLinks(current.Node, _links);
                foreach (var link in _links)
                {
                    if (link.Road == start.Owner || link.Road == end.Owner) continue;
                    var step = link.From(current.Node);
                    Offer(step.To, current.Distance + link.Length, step);
                }
            }
            if (goalNode == Entity.Null || (_open.Count != 0 && _open.Min.Estimate < bestGoal)) return false;

            Entity node = goalNode;
            if (end.IsRoad) steps.Add(end.Link.From(node));
            while (_best[node].Incoming.Road != Entity.Null)
            {
                var incoming = _best[node].Incoming;
                steps.Add(incoming);
                node = incoming.From;
            }
            if (start.IsRoad) steps.Add(start.Link.From(start.Link.Other(node)));
            steps.Reverse();
            return true;

            void Offer(Entity node, float distance, UpgradeRoadStep incoming)
            {
                if (_best.TryGetValue(node, out var prior))
                {
                    if (distance >= prior.Distance) return;
                    _open.Remove(prior);
                }
                var location = graph.Position(node).xz;
                float goal = math.min(math.distance(location, goalFirst) + end.ToFirst,
                    math.distance(location, goalSecond) + end.ToSecond);
                var visit = new Visit(node, distance, distance + goal, incoming);
                _best[node] = visit;
                _open.Add(visit);
            }
        }
    }
}
