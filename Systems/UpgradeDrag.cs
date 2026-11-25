using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedianGrassReplacement.Systems
{
    internal readonly struct UpgradeDragTarget : IEquatable<UpgradeDragTarget>
    {
        internal readonly Entity Owner, Part, Other;
        internal readonly byte Role;
        internal readonly int Slot;
        internal readonly bool Individual;

        internal UpgradeDragTarget(Entity owner, Entity part = default, byte role = 0, Entity other = default, bool individual = false, int slot = 0)
        {
            Owner = owner;
            Part = part;
            Other = other;
            Role = role;
            Individual = individual;
            Slot = slot;
        }

        public bool Equals(UpgradeDragTarget other) => Owner == other.Owner && Part == other.Part && Other == other.Other && Role == other.Role && Individual == other.Individual && Slot == other.Slot;
        public override bool Equals(object obj) => obj is UpgradeDragTarget other && Equals(other);
        public override int GetHashCode() => unchecked(((((Owner.GetHashCode() * 397 ^ Part.GetHashCode()) * 397 ^ Other.GetHashCode()) * 397 ^ Role) * 397
            ^ Individual.GetHashCode()) * 397 ^ Slot);

        internal static bool Live(EntityManager manager, Entity entity) => entity != Entity.Null
            && manager.Exists(entity) && !manager.HasComponent<Temp>(entity) && !manager.HasComponent<Deleted>(entity);
    }

    internal sealed class UpgradeDrag
    {
        private readonly HashSet<UpgradeDragTarget> _seen = new HashSet<UpgradeDragTarget>();
        private readonly List<UpgradeDragTarget> _targets = new List<UpgradeDragTarget>();
        private readonly List<(float3 Position, float Size)> _markers = new List<(float3, float)>();
        private readonly UpgradeRoadRoute _router = new UpgradeRoadRoute();
        private readonly List<UpgradeRoadStep> _steps = new List<UpgradeRoadStep>();
        private readonly List<(Bezier4x3 Curve, float Width)> _highlights = new List<(Bezier4x3, float)>();
        private UpgradeRoadPoint _startPoint, _previousPoint;
        private UpgradeDragTarget _endTarget;
        private int _endProgress = -1;
        internal UpgradeDragTarget Anchor { get; private set; }
        internal float3 AnchorPosition { get; private set; }
        internal bool Anchored => Anchor.Owner != Entity.Null;
        internal bool StartsOnNode => Anchored && !_startPoint.IsRoad;
        internal IReadOnlyList<UpgradeRoadStep> Steps => _steps;
        private object _prefab;
        private bool _active, _remove, _sealed;
        internal bool Active => _active;
        internal bool Removing => _remove;

        internal enum Phase { Idle, Collect, Commit }

        internal Phase Update(object prefab, bool canCollect, bool applyPressed, bool removePressed,
            bool applyHeld, bool removeHeld)
        {
            if (!canCollect || prefab == null || (_active && !ReferenceEquals(_prefab, prefab)))
            {
                Reset();
                return Phase.Idle;
            }
            if (_active)
            {
                _sealed |= !(_remove ? removeHeld : applyHeld);
                return _sealed ? Phase.Commit : Phase.Collect;
            }

            if (!applyPressed && !removePressed) return Phase.Idle;
            _active = true;
            _remove = removePressed;
            _prefab = prefab;
            return Phase.Collect;
        }

        internal Phase Read(PrefabBase prefab, bool focused, bool applyPressed, bool secondaryPressed,
            bool applyHeld, bool secondaryHeld, bool rawPrimary = false)
        {
            var mouse = Mouse.current;
            return Update(prefab, focused && !UpgradeInputPolicy.PointerBlocked,
                applyPressed || (rawPrimary && mouse?.leftButton.wasPressedThisFrame == true),
                UpgradeInputPolicy.RemovePressed(secondaryPressed),
                applyHeld || (rawPrimary && mouse?.leftButton.isPressed == true),
                secondaryHeld || mouse?.rightButton.isPressed == true);
        }

        // The endpoint can be a routing junction even when every edit is a road segment.
        // The tool continues to use the starting target to determine the edit category.
        internal bool RouteTo(EntityManager manager, UpgradeDragTarget end, float3 position)
        {
            if (!_active || _sealed) return false;
            var graph = new UpgradeNetworkGraph(manager);
            if (!graph.TryPoint(end.Owner, position, out var endPoint))
            {
                _steps.Clear();
                ClearSelection();
                _endProgress = -1;
                return false;
            }
            if (!Anchored)
            {
                Anchor = end;
                AnchorPosition = position;
                _startPoint = endPoint;
            }
            int progress = (int)math.floor(endPoint.Progress * 8f);
            if (_endTarget.Equals(end) && _endProgress == progress) return false;
            _endTarget = end;
            _endProgress = progress;
            ClearSelection();
            if (!graph.TryPoint(Anchor.Owner, AnchorPosition, out _startPoint))
            {
                Reset();
                return false;
            }
            for (int i = 0; i < _steps.Count; i++)
            {
                if (graph.IsCurrent(_steps[i])) continue;
                _steps.Clear();
                break;
            }
            bool found = _router.Follow(graph, _startPoint, _previousPoint, endPoint, _steps);
            _previousPoint = endPoint;
            return found;
        }

        internal void AddHighlight(Bezier4x3 curve, float width)
        {
            if (_active && !_sealed) _highlights.Add((curve, width));
        }

        internal bool Add(UpgradeDragTarget target, float3 position, float markerSize = .45f)
        {
            if (!_active || _sealed || !_seen.Add(target)) return false;
            if (markerSize > 0f) _markers.Add((position, markerSize));
            _targets.Add(target);
            return true;
        }

        internal void AddNode(EntityManager manager, Entity node, float markerSize = 3f)
        {
            Add(new UpgradeDragTarget(node), manager.GetComponentData<Node>(node).m_Position, markerSize);
        }

        internal void AddRoad(Entity road, byte side, Bezier4x3 path)
        {
            if (Add(new UpgradeDragTarget(road, role: side), MathUtils.Position(path, .5f), 0f))
                AddHighlight(path, 2f);
        }

        internal void Draw(OverlayRenderSystem overlay)
        {
            if (_targets.Count == 0) return;
            var buffer = overlay.GetBuffer(out var dependency);
            dependency.Complete();
            var color = _remove ? new Color(1f, .55f, .3f, .7f) : new Color(.15f, 1f, .3f, .8f);
            foreach (var highlight in _highlights)
                buffer.DrawCurve(color, highlight.Curve, highlight.Width);
            foreach (var marker in _markers)
                buffer.DrawCircle(color, marker.Position, marker.Size);
        }

        internal int Count => _targets.Count;

        internal void Commit(Action<UpgradeDragTarget, bool> apply)
        {
            try
            {
                foreach (var target in _targets) apply(target, _remove);
            }
            finally { Reset(); }
        }

        internal void Reset()
        {
            _active = false;
            _sealed = false;
            _remove = false;
            _prefab = null;
            Anchor = default;
            AnchorPosition = default;
            _startPoint = default;
            _previousPoint = default;
            _endTarget = default;
            _endProgress = -1;
            _steps.Clear();
            ClearSelection();
        }

        private void ClearSelection()
        {
            _seen.Clear();
            _targets.Clear();
            _highlights.Clear();
            _markers.Clear();
        }
    }
}
