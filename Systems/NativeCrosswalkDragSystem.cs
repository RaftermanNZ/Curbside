using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Serialization;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace MedianGrassReplacement.Systems
{
    internal struct NativeCrosswalkStroke { internal Bezier4x3 Curve; internal float Width; }

    [BurstCompile]
    internal struct DrawNativeCrosswalkPreviewJob : IJob
    {
        [ReadOnly] internal NativeList<NativeCrosswalkStroke> Strokes;
        internal OverlayRenderSystem.Buffer Buffer;
        public void Execute()
        {
            for (int i = 0; i < Strokes.Length; i++)
                Buffer.DrawCurve(Color.white, Strokes[i].Curve, Strokes[i].Width);
        }
    }

    [DisableAutoCreation]
    public partial class NativeCrosswalkDragSystem : GameSystemBase, IPreSerialize
    {
        private ToolSystem tools;
        private CrosswalkUpgradeSystem choices;
        private CrosswalkVisualSystem visuals;
        private IntersectionPaintSystem paint;
        private ToolOutputBarrier output;
        private OverlayRenderSystem overlay;
        private ModificationBarrier4B nativeBoundary;
        private EntityQuery temporaryRoads;
        private NetToolSystem producer;
        private NativeList<NativeCrosswalkSelection> snapshot;
        private NativeList<NativeCrosswalkStroke> strokes;
        private JobHandle captureFence, drawFence;
        private ulong scheduled, consumed;
        private NativeCrosswalkAppearance snapshotMode;
        private bool armed, sealedApply, renderDirty, destroyed;
        internal bool Ready { get; set; }
        private readonly Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance> targets = new Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance>();
        private readonly Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance> next = new Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance>();
        private readonly Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance> pending = new Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance>();
        private readonly Dictionary<NativeCrosswalkPair, List<Entity>> pairs = new Dictionary<NativeCrosswalkPair, List<Entity>>();
        private readonly HashSet<Entity> indexedNodes = new HashSet<Entity>(), indexedRoads = new HashSet<Entity>();
        private readonly Dictionary<NativeCrosswalkPair, NativeCrosswalkSelection> merged = new Dictionary<NativeCrosswalkPair, NativeCrosswalkSelection>();
        private readonly Stack<List<Entity>> pairPool = new Stack<List<Entity>>();
        private readonly Dictionary<Entity, Entity> receipts = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, Fit> previewActors = new Dictionary<Entity, Fit>();
        private readonly Dictionary<Entity, Entity> proposalActors = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, int> proposalSources = new Dictionary<Entity, int>();
        private Dictionary<FitKey, Fit> fits = new Dictionary<FitKey, Fit>(), previousFits = new Dictionary<FitKey, Fit>();
        private readonly Dictionary<Entity, ClipCertificate> clipCertificates = new Dictionary<Entity, ClipCertificate>();
        private readonly HashSet<Entity> selectedNodes = new HashSet<Entity>();
        private readonly List<Entity> staleNodes = new List<Entity>();
        private ulong clipRevision;
        private long captureEvents, fitCalls, fitHits, commitEvents, committedChoices, rejectedReceipts;

        private readonly struct FitKey : IEquatable<FitKey>
        {
            internal readonly NativeCrosswalkApproach Approach;
            internal readonly Bezier4x3 Curve;
            internal readonly Entity Prefab;
            internal readonly float Width;
            internal readonly ulong ClipRevision;
            internal readonly int Catalog;
            internal FitKey(NativeCrosswalkApproach approach, Bezier4x3 curve, Entity prefab, float width, ulong clipRevision)
            { Approach = approach; Curve = curve; Prefab = prefab; Width = width; ClipRevision = clipRevision; Catalog = CurbLineEmissionSystem.CatalogRevision; }
            public bool Equals(FitKey other) => Approach.Equals(other.Approach) && MarkingPreviewPolicy.SameCurve(Curve, other.Curve)
                && Prefab == other.Prefab && Width.Equals(other.Width) && ClipRevision == other.ClipRevision && Catalog == other.Catalog;
            public override bool Equals(object value) => value is FitKey key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Approach.GetHashCode(); h = h * 397 ^ Curve.a.GetHashCode(); h = h * 397 ^ Curve.b.GetHashCode();
                    h = h * 397 ^ Curve.c.GetHashCode(); h = h * 397 ^ Curve.d.GetHashCode();
                    return (((h * 397 ^ Prefab.GetHashCode()) * 397 ^ Width.GetHashCode()) * 397 ^ ClipRevision.GetHashCode()) * 397 ^ Catalog;
                }
            }
        }
        private struct Fit { internal Bezier4x3 First, Second; internal float Width; }
        private struct ClipEdge { internal Entity Road; internal EdgeGeometry Geometry; }
        private sealed class ClipCertificate
        { internal uint Pass; internal bool Ready; internal ulong Revision; internal readonly List<ClipEdge> Edges = new List<ClipEdge>(); }

        protected override void OnCreate()
        {
            base.OnCreate(); Enabled = false;
            tools = World.GetOrCreateSystemManaged<ToolSystem>();
            choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            visuals = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>();
            paint = World.GetOrCreateSystemManaged<IntersectionPaintSystem>();
            paint.AttachNativeCrosswalkPreview(this);
            output = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            nativeBoundary = World.GetOrCreateSystemManaged<ModificationBarrier4B>();
            snapshot = new NativeList<NativeCrosswalkSelection>(16, Allocator.Persistent);
            strokes = new NativeList<NativeCrosswalkStroke>(16, Allocator.Persistent);
            temporaryRoads = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Road>(), ComponentType.Exclude<Deleted>());
            tools.EventToolChanged += ToolChanged;
            tools.EventPrefabChanged += PrefabChanged;
        }
        internal bool Owns(NetToolSystem tool) => Ready && !Mod.Saving && tool != null && tools.activeTool == tool
            && choices.NativeCrosswalkPrefab != null && ReferenceEquals(tool.GetPrefab(), choices.NativeCrosswalkPrefab);
        private NativeCrosswalkAppearance Mode(bool removing) => removing
            ? choices.NativeInvisibleMode ? NativeCrosswalkAppearance.Restore : NativeCrosswalkAppearance.Zebra
            : choices.NativeInvisibleMode ? NativeCrosswalkAppearance.Invisible : choices.NativeLineMode ? NativeCrosswalkAppearance.Lines : NativeCrosswalkAppearance.Zebra;

        internal JobHandle PrepareCourse(NetToolSystem tool, NativeList<NetToolSystem.UpgradeState> states, bool removing, JobHandle input)
        {
            if (!Owns(tool) || !removing || !choices.NativeInvisibleMode) return input;
            captureFence = new RestoreNativeCrosswalkRoutingJob { States = states }.Schedule(JobHandle.CombineDependencies(input, captureFence));
            // Keep ownership even if another patch skips native UpdateCourse.
            output.AddJobHandleForProducer(captureFence);
            return captureFence;
        }
        internal JobHandle CaptureCourse(NetToolSystem tool, NativeList<ControlPoint> controls,
            NativeList<NetToolSystem.UpgradeState> states, bool removing, JobHandle input)
        {
            if (!Owns(tool) || Mod.Settings?.CurbsidePreviews != true) return input;
            producer = tool; snapshotMode = Mode(removing); scheduled++; captureEvents++;
            captureFence = new CaptureNativeCrosswalkSelectionJob { Controls = controls, States = states, Output = snapshot }
                .Schedule(JobHandle.CombineDependencies(input, captureFence));
            output.AddJobHandleForProducer(captureFence);
            return captureFence;
        }

        // Called only at the native accepted-apply method, before it clears its
        // native lists. No persistence here; failed/skipped apply cannot save.
        internal bool CaptureCommit(NetToolSystem tool, NativeList<ControlPoint> controls,
            NativeList<NetToolSystem.UpgradeState> states, bool removing)
        {
            if (!Owns(tool)) return false;
            captureFence.Complete();
            new CaptureNativeCrosswalkSelectionJob { Controls = controls, States = states, Output = snapshot }.Execute();
            producer = tool; BuildTargets(snapshot, Mode(removing), pending);
            armed = false; sealedApply = false; receipts.Clear();
            return pending.Count != 0;
        }
        internal void AcceptCommit(bool captured, bool ranOriginal)
        {
            armed = captured && ranOriginal;
            if (!armed) pending.Clear();
            Enabled = armed;
            if (armed) commitEvents++;
        }

        private bool Live(Entity e) => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);
        private bool Committed(Entity e) => Live(e) && !EntityManager.HasComponent<Temp>(e);
        private void ClearPairs()
        {
            foreach (var value in pairs.Values) { value.Clear(); pairPool.Push(value); }
            pairs.Clear(); indexedNodes.Clear(); indexedRoads.Clear(); merged.Clear();
        }
        private void IndexNode(Entity node)
        {
            if (!indexedNodes.Add(node) || !Committed(node) || !EntityManager.HasBuffer<ConnectedEdge>(node)) return;
            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                var road = entry.m_Edge;
                if (!Committed(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<Road>(road)) continue;
                var edge = EntityManager.GetComponentData<Edge>(road);
                if (edge.m_Start != node && edge.m_End != node) continue;
                var key = new NativeCrosswalkPair(edge.m_Start, edge.m_End);
                if (!pairs.TryGetValue(key, out var list)) { list = pairPool.Count == 0 ? new List<Entity>() : pairPool.Pop(); pairs.Add(key, list); }
                if (indexedRoads.Add(road)) list.Add(road);
            }
        }
        private void BuildTargets(NativeList<NativeCrosswalkSelection> rows, NativeCrosswalkAppearance mode,
            Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance> result)
        {
            result.Clear(); ClearPairs();
            try
            {
                // Coalesce selected-end membership before expanding parallel
                // native roads, avoiding repeated pair-by-road cross products.
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    var pair = new NativeCrosswalkPair(row.A, row.B);
                    if (merged.TryGetValue(pair, out var previous))
                    { previous.Add |= row.Add; previous.Remove |= row.Remove; merged[pair] = previous; }
                    else merged.Add(pair, row);
                }
                foreach (var row in merged.Values)
                {
                    if (row.A == row.B && Committed(row.A) && EntityManager.HasComponent<Edge>(row.A)) AddRoad(row.A, row, mode, result);
                    else
                    {
                        IndexNode(row.A); // Native CreateReplacement expands this endpoint's ConnectedEdge only.
                        if (pairs.TryGetValue(new NativeCrosswalkPair(row.A, row.B), out var roads))
                            foreach (var road in roads) AddRoad(road, row, mode, result);
                    }
                }
            }
            finally { ClearPairs(); }
        }
        private void AddRoad(Entity road, NativeCrosswalkSelection selection, NativeCrosswalkAppearance mode,
            Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance> result)
        {
            if (!Committed(road) || !EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Composition>(road)) return;
            var edge = EntityManager.GetComponentData<Edge>(road);
            if (edge.m_Start == edge.m_End) return; // Existing saved approach keys cannot distinguish self-loop ends.
            var composition = EntityManager.GetComponentData<Composition>(road);
            AddEnd(road, edge.m_Start, composition.m_StartNode, selection, mode, result);
            AddEnd(road, edge.m_End, composition.m_EndNode, selection, mode, result);
        }
        private void AddEnd(Entity road, Entity node, Entity definition, NativeCrosswalkSelection selection, NativeCrosswalkAppearance mode,
            Dictionary<NativeCrosswalkApproach, NativeCrosswalkAppearance> result)
        {
            if (!Committed(node) || !Live(definition) || !EntityManager.HasComponent<NetCompositionData>(definition)
                || !NativeCrosswalkDragPolicy.Selected(EntityManager.GetComponentData<NetCompositionData>(definition).m_Flags.m_General, selection)) return;
            if (mode == NativeCrosswalkAppearance.Restore && choices.CommittedAppearance(road, node) != 2) return;
            result[new NativeCrosswalkApproach(road, node)] = mode;
        }

        internal void PublishPreview()
        {
            if (Mod.Settings?.CurbsidePreviews != true || consumed == scheduled || !Owns(producer)) return;
            // ToolOutputBarrier registered this producer. Its playback already
            // joined captureFence; never introduce an early main-thread wait.
            if (!captureFence.IsCompleted) return;
            BuildTargets(snapshot, snapshotMode, next);
            bool changed = targets.Count != next.Count;
            if (!changed) foreach (var item in next) if (!targets.TryGetValue(item.Key, out var old) || old != item.Value) { changed = true; break; }
            targets.Clear(); foreach (var item in next) targets.Add(item.Key, item.Value);
            selectedNodes.Clear(); foreach (var item in targets.Keys) selectedNodes.Add(item.Node);
            staleNodes.Clear(); foreach (var node in clipCertificates.Keys) if (!selectedNodes.Contains(node)) staleNodes.Add(node);
            foreach (var node in staleNodes) clipCertificates.Remove(node);
            staleNodes.Clear();
            next.Clear(); consumed = scheduled;
            var swap = previousFits; previousFits = fits; fits = swap; fits.Clear();
            if (changed) World.GetExistingSystemManaged<NativePaintProjectionSystem>()?.SelectionChanged();
        }

        internal void BeforeNativeOutput()
        {
            previewActors.Clear(); renderDirty = true;
            proposalActors.Clear(); proposalSources.Clear();
            if (!armed || !Owns(producer) || tools.applyMode != ApplyMode.Apply) { CancelPending(); return; }
            receipts.Clear();
            using (var actors = temporaryRoads.ToEntityArray(Allocator.Temp))
                foreach (var actor in actors)
                {
                    var temp = EntityManager.GetComponentData<Temp>(actor);
                    if (!Committed(temp.m_Original) || !EntityManager.HasComponent<Edge>(temp.m_Original) || !EntityManager.HasComponent<Road>(temp.m_Original)
                        || (temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel | TempFlags.Replace | TempFlags.Combine)) != 0
                        || EntityManager.HasComponent<Warning>(actor) || EntityManager.HasComponent<Applied>(temp.m_Original)) continue;
                    var edge = EntityManager.GetComponentData<Edge>(temp.m_Original);
                    if (pending.ContainsKey(new NativeCrosswalkApproach(temp.m_Original, edge.m_Start))
                        || pending.ContainsKey(new NativeCrosswalkApproach(temp.m_Original, edge.m_End)))
                    {
                        if (receipts.TryGetValue(temp.m_Original, out var previous) && previous != actor) receipts[temp.m_Original] = Entity.Null;
                        else receipts[temp.m_Original] = actor;
                    }
                }
            sealedApply = receipts.Count != 0;
        }
        protected override void OnUpdate()
        {
            // Scheduled only after native ToolOutputBarrier, and enabled only by
            // accepted native SetAppliedUpgrade. Zero hover/update polling.
            try
            {
                if (!armed || !sealedApply || Mod.Saving || tools.applyMode != ApplyMode.Apply || !Owns(producer)) return;
                foreach (var item in pending)
                {
                    var key = item.Key;
                    if (!receipts.TryGetValue(key.Road, out var temporary) || temporary == Entity.Null || !Committed(key.Road) || !Committed(key.Node)
                        || !EntityManager.HasComponent<Edge>(key.Road) || !EntityManager.HasComponent<Node>(key.Node)
                        || !EntityManager.HasComponent<Applied>(key.Road)
                        || EntityManager.Exists(temporary) && !EntityManager.HasComponent<Deleted>(temporary)) { rejectedReceipts++; continue; }
                    var edge = EntityManager.GetComponentData<Edge>(key.Road);
                    if (edge.m_Start != key.Node && edge.m_End != key.Node) { rejectedReceipts++; continue; }
                    // The accepted native edit can rebuild signal actors and
                    // masks even when the crossing's appearance stays the same.
                    if (EntityManager.HasComponent<MGRFarSideLights>(key.Node))
                        FarSideLightSystem.Queue(World,key.Node);
                    if (item.Value == NativeCrosswalkAppearance.Restore) choices.RestoreHiddenAppearance(key.Road, key.Node);
                    else
                    {
                        if (choices.HasExplicitNativeAppearance(key.Road, key.Node)
                            && choices.CommittedAppearance(key.Road, key.Node) == (byte)item.Value) continue;
                        choices.SetApproachAppearance(key.Road, key.Node, item.Value == NativeCrosswalkAppearance.Lines, item.Value == NativeCrosswalkAppearance.Invisible);
                    }
                    committedChoices++;
                }
            }
            finally { CancelPending(); }
        }
        private void CancelPending() { pending.Clear(); receipts.Clear(); armed = sealedApply = false; Enabled = false; }

        private Entity Original(Entity entity)
        {
            if (!Live(entity)) return Entity.Null;
            if (!EntityManager.HasComponent<Temp>(entity)) return entity;
            var original = EntityManager.GetComponentData<Temp>(entity).m_Original;
            return Committed(original) ? original : Entity.Null;
        }
        private bool Approach(Entity actor, Entity source, out NativeCrosswalkApproach approach)
        {
            approach = default;
            if (Committed(source) && choices.TryResolveApproach(source, out var sourceRoad, out var sourceNode))
            { approach = new NativeCrosswalkApproach(sourceRoad, sourceNode); return true; }
            var owner = Original(EntityManager.GetComponentData<Owner>(actor).m_Owner);
            var curve = EntityManager.GetComponentData<Curve>(actor).m_Bezier;
            if (Committed(owner) && EntityManager.HasComponent<Edge>(owner) && EntityManager.HasComponent<EdgeGeometry>(owner))
            {
                var edge = EntityManager.GetComponentData<Edge>(owner);
                var node = NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(owner), MathUtils.Position(curve, .5f)) ? edge.m_End : edge.m_Start;
                approach = new NativeCrosswalkApproach(owner, node); return true;
            }
            if (!Committed(owner) || !EntityManager.HasBuffer<ConnectedEdge>(owner)) return false;
            float best = float.MaxValue; bool unique = false;
            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(owner, true))
            {
                var road = entry.m_Edge;
                if (!Committed(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<EdgeGeometry>(road)) continue;
                var edge = EntityManager.GetComponentData<Edge>(road);
                if (edge.m_Start != owner && edge.m_End != owner) continue;
                if (!CrosswalkApproachPlacement.TryScore(EntityManager.GetComponentData<EdgeGeometry>(road), edge.m_Start == owner, curve, out float score)) continue;
                if (CrosswalkPreviewChoicePolicy.Better(score, best)) { best = score; approach = new NativeCrosswalkApproach(road, owner); unique = true; }
                else if (CrosswalkPreviewChoicePolicy.Tied(score, best)) unique = false;
            }
            return unique;
        }
        internal bool TryPreview(Entity actor, Entity source, out Entity definition)
        {
            definition = Entity.Null;
            if (targets.Count == 0 || Mod.Settings?.CurbsidePreviews != true || !Owns(producer)
                || !EntityManager.HasComponent<Game.Net.PedestrianLane>(actor) || !EntityManager.HasComponent<Curve>(actor)
                || (EntityManager.GetComponentData<Game.Net.PedestrianLane>(actor).m_Flags & PedestrianLaneFlags.Crosswalk) == 0
                || !Approach(actor, source, out var key) || !targets.TryGetValue(key, out var mode)) { RemovePreview(actor); return false; }
            byte appearance = mode == NativeCrosswalkAppearance.Restore ? choices.RestoredAppearance(key.Road, key.Node) : (byte)mode;
            var prefab = visuals.Original(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab);
            if (Committed(source) && choices.CommittedAppearance(key.Road, key.Node) == appearance)
            {
                bool replaced = EntityManager.HasComponent<MGRReplacedPaint>(source);
                bool alreadyVisible = appearance == 0 && !replaced;
                bool replacementReady = appearance != 0 && replaced
                    && visuals.TrySuppressedPreviewDefinition(source, out var currentDefinition)
                    && TemporaryPreviewWrites.Ready(EntityManager, actor, currentDefinition)
                    && EntityManager.HasComponent<Curve>(source)
                    && MarkingPreviewPolicy.SameCurve(EntityManager.GetComponentData<Curve>(source).m_Bezier, EntityManager.GetComponentData<Curve>(actor).m_Bezier);
                if (alreadyVisible || replacementReady) { RemovePreview(actor); return false; }
            } // Reuse existing output only when its presentation is actually ready.
            if (appearance == 0) { RemovePreview(actor); definition = prefab; SetProposal(actor, source); return true; }
            if (appearance == 1)
            {
                var curve = EntityManager.GetComponentData<Curve>(actor).m_Bezier;
                var fitKey = new FitKey(key, curve, prefab, Mod.Settings.CrosswalkUpgradeWidth, GeometryRevision(key.Node));
                if (!fits.TryGetValue(fitKey, out var fitted))
                {
                    if (previousFits.TryGetValue(fitKey, out fitted)) fitHits++;
                    else
                    {
                        fitCalls++;
                        float nativeWidth = EntityManager.HasComponent<NetLaneGeometryData>(prefab) ? EntityManager.GetComponentData<NetLaneGeometryData>(prefab).m_Size.x : 0;
                        if (!paint.TryPreviewLines(key.Road, key.Node, curve, nativeWidth, out fitted.First, out fitted.Second, out fitted.Width)) { RemovePreview(actor); return false; }
                    }
                    fits[fitKey] = fitted;
                }
                if (!previewActors.TryGetValue(actor, out var previous) || !MarkingPreviewPolicy.SameCurve(previous.First, fitted.First)
                    || !MarkingPreviewPolicy.SameCurve(previous.Second, fitted.Second) || !previous.Width.Equals(fitted.Width))
                { previewActors[actor] = fitted; renderDirty = true; }
            }
            else RemovePreview(actor);
            definition = visuals.PreviewDefinition(prefab);
            if (definition != Entity.Null) SetProposal(actor, source);
            else RemovePreview(actor);
            return definition != Entity.Null;
        }
        private void SetProposal(Entity actor, Entity source)
        {
            RemoveProposal(actor);
            if (source == Entity.Null) return;
            proposalActors[actor] = source;
            proposalSources.TryGetValue(source, out int count); proposalSources[source] = count + 1;
        }
        private void RemoveProposal(Entity actor)
        {
            if (!proposalActors.TryGetValue(actor, out var source)) return;
            proposalActors.Remove(actor);
            int count = proposalSources[source];
            if (count == 1) proposalSources.Remove(source); else proposalSources[source] = count - 1;
        }
        internal bool HidesSource(Entity source) => Mod.Settings?.CurbsidePreviews == true && proposalSources.ContainsKey(source);
        internal void RemovePreview(Entity actor) { if (previewActors.Remove(actor)) renderDirty = true; RemoveProposal(actor); }
        internal void DrawOverlay()
        {
            if (Mod.Saving || Mod.Settings?.CurbsidePreviews != true || targets.Count == 0 || !Owns(producer)) return;
            if (renderDirty)
            {
                drawFence.Complete(); strokes.Clear();
                foreach (var item in previewActors.Values)
                { strokes.Add(new NativeCrosswalkStroke { Curve = item.First, Width = item.Width }); strokes.Add(new NativeCrosswalkStroke { Curve = item.Second, Width = item.Width }); }
                renderDirty = false;
            }
            if (strokes.Length == 0) return;
            var buffer = overlay.GetBuffer(out var dependency);
            drawFence = new DrawNativeCrosswalkPreviewJob { Buffer = buffer, Strokes = strokes }.Schedule(JobHandle.CombineDependencies(drawFence, dependency));
            overlay.AddBufferWriter(drawFence);
        }
        internal void ModeChanged()
        {
            captureFence.Complete();
            drawFence.Complete();
            strokes.Clear(); previewActors.Clear(); renderDirty = false;
            proposalActors.Clear(); proposalSources.Clear();
            targets.Clear(); fits.Clear(); previousFits.Clear(); clipCertificates.Clear();
            consumed = scheduled;
            if (Mod.Settings?.CurbsidePreviews == true)
                World.GetExistingSystemManaged<NativePaintProjectionSystem>()?.SelectionChanged();
        }
        private void ToolChanged(ToolBaseSystem tool) { ResetOperation(); }
        private void PrefabChanged(PrefabBase prefab) { ResetOperation(); }
        private static bool SameSegment(Segment a, Segment b) => MarkingPreviewPolicy.SameCurve(a.m_Left, b.m_Left)
            && MarkingPreviewPolicy.SameCurve(a.m_Right, b.m_Right) && a.m_Length.Equals(b.m_Length);
        private ulong GeometryRevision(Entity node)
        {
            if (!clipCertificates.TryGetValue(node, out var certificate))
            { certificate = new ClipCertificate(); clipCertificates.Add(node, certificate); }
            uint pass = nativeBoundary.LastSystemVersion;
            if (certificate.Ready && certificate.Pass == pass) return certificate.Revision;
            bool changed = !certificate.Ready; int count = 0;
            if (EntityManager.HasBuffer<ConnectedEdge>(node))
                foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(node, true))
                {
                    var road = entry.m_Edge;
                    if (!Committed(road) || !EntityManager.HasComponent<EdgeGeometry>(road)) continue;
                    var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);
                    var value = new ClipEdge { Road = road, Geometry = geometry };
                    if (count == certificate.Edges.Count) { certificate.Edges.Add(value); changed = true; }
                    else
                    {
                        var previous = certificate.Edges[count];
                        if (previous.Road != road || !SameSegment(previous.Geometry.m_Start, geometry.m_Start)
                            || !SameSegment(previous.Geometry.m_End, geometry.m_End)) { certificate.Edges[count] = value; changed = true; }
                    }
                    count++;
                }
            if (count < certificate.Edges.Count) { certificate.Edges.RemoveRange(count, certificate.Edges.Count - count); changed = true; }
            if (changed) certificate.Revision = ++clipRevision;
            certificate.Pass = pass; certificate.Ready = true;
            return certificate.Revision;
        }
        private void ResetOperation()
        {
            captureFence.Complete(); drawFence.Complete();
            CancelPending(); producer = null; targets.Clear(); next.Clear(); previewActors.Clear(); fits.Clear(); previousFits.Clear();
            proposalActors.Clear(); proposalSources.Clear();
            clipCertificates.Clear(); selectedNodes.Clear(); staleNodes.Clear();
            snapshot.Clear(); strokes.Clear(); consumed = scheduled; renderDirty = false; ClearPairs();
        }
        public void PreSerialize(Context context)
        {
            // Preview intent is managed/transient. Keep native selection for
            // reacquisition; never serialize or apply a pending transaction.
            captureFence.Complete(); drawFence.Complete(); CancelPending();
            previewActors.Clear(); strokes.Clear(); renderDirty = true;
            proposalActors.Clear(); proposalSources.Clear();
        }
        protected override void OnGamePreload(Purpose purpose, GameMode mode) { ResetOperation(); base.OnGamePreload(purpose, mode); }
        internal void Release()
        {
            Ready = false; if (!destroyed) { ResetOperation(); paint.AttachNativeCrosswalkPreview(null); }
        }
        protected override void OnDestroy()
        {
            Ready = false; tools.EventToolChanged -= ToolChanged; tools.EventPrefabChanged -= PrefabChanged; ResetOperation();
            paint.AttachNativeCrosswalkPreview(null);
            if (snapshot.IsCreated) snapshot.Dispose(); if (strokes.IsCreated) strokes.Dispose(); base.OnDestroy();
            destroyed = true;
        }
        internal void RecordSnapshot() => Mod.Log.Info("Native crosswalk drag: ready=" + Ready + " captures=" + captureEvents
            + " activeApproaches=" + targets.Count + " fitCalls=" + fitCalls + " fitCacheHits=" + fitHits
            + " commitEvents=" + commitEvents + " choicesWritten=" + committedChoices + " rejectedReceipts=" + rejectedReceipts);
    }

    public partial class CrosswalkVisualSystem
    {
        internal Entity PreviewDefinition(Entity prefab)
            => EntityManager.Exists(prefab) && EntityManager.HasComponent<NetLaneGeometryData>(prefab) ? SuppressedDefinition(prefab) : Entity.Null;
    }
    public partial class IntersectionPaintSystem
    {
        internal void AttachNativeCrosswalkPreview(NativeCrosswalkDragSystem system) { _nativePreviewDrag = system; }
    }
    public partial class CrosswalkUpgradeSystem
    {
        internal bool HasExplicitNativeAppearance(Entity road, Entity node)
        {
            if (NodeIntent(road, node, out _)) return true;
            if (!EntityManager.HasComponent<Edge>(road)) return false;
            var edge = EntityManager.GetComponentData<Edge>(road);
            if (edge.m_Start != node && edge.m_End != node) return false;
            bool start = edge.m_Start == node;
            return EntityManager.HasComponent<MGRCrosswalkRoadStyle>(road)
                && EntityManager.GetComponentData<MGRCrosswalkRoadStyle>(road).TryGet(start, out _)
                || EntityManager.HasComponent<MGRInvisibleCrosswalkStyle>(road)
                && EntityManager.GetComponentData<MGRInvisibleCrosswalkStyle>(road).Has(start);
        }
        internal byte RestoredAppearance(Entity road, Entity node)
        {
            if (!EntityManager.HasComponent<Edge>(road)) return 0;
            var edge = EntityManager.GetComponentData<Edge>(road); byte bit = (byte)(edge.m_Start == node ? 1 : 2);
            var previous = EntityManager.HasComponent<MGRHiddenCrosswalkPrevious>(road) ? EntityManager.GetComponentData<MGRHiddenCrosswalkPrevious>(road) : default;
            return (previous.Have & previous.Line & bit) != 0 ? (byte)1 : (byte)0;
        }
    }
}
