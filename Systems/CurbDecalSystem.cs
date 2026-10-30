using System;

using System.Collections.Generic;

using System.IO;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

using UnityEngine;

using UnityEngine.Rendering;

namespace MedianGrassReplacement.Systems

{

    internal static class DecalStripPolicy

    {

        internal static float RoadLayerMask => math.asfloat((uint)Game.Rendering.DecalLayers.Roads);

        internal const int MaxProjectors = 2048, MaxPerRoad = 128;

        internal const string LimitWarning="Road-wear coverage reached this section's safety limit. Increase repeat length or reduce overlap.";

        internal static int Capacity(bool unlimited,int limit,int total,int replacing)

        {

            if(unlimited) return MaxPerRoad;

            long remaining=(long)math.clamp(limit,16,MaxProjectors)-total+replacing;

            return (int)System.Math.Max(0L,System.Math.Min((long)MaxPerRoad,remaining));

        }

        internal static float SideRotation(float degrees, bool left, bool orient)

        {

            float result = orient && !left ? degrees+180f : degrees;

            return result > 180f ? result-360f : result;

        }

        internal static float2 Footprint(float width, float length, float degrees)

        {

            float angle = math.radians(degrees), c = math.abs(math.cos(angle)), s = math.abs(math.sin(angle));

            return new float2(c * width + s * length, s * width + c * length);

        }

        internal static bool Fits(Bezier4x3 curve, float t, float width, float length, float degrees)

        {

            float2 center = MathUtils.Position(curve,t).xz;

            float2 forward = math.normalizesafe(MathUtils.Tangent(curve,t).xz);

            float2 right = new float2(forward.y,-forward.x);

            float angle = math.radians(degrees), c = math.cos(angle), s = math.sin(angle);

            float2 along = (forward*c+right*s)*length*.5f, across=(right*c-forward*s)*width*.5f;

            float2 start = math.normalizesafe(MathUtils.Tangent(curve,0).xz), end = math.normalizesafe(MathUtils.Tangent(curve,1).xz);

            if (math.lengthsq(start)<.5f || math.lengthsq(end)<.5f) return false;

            for(int a=-1;a<=1;a+=2) for(int b=-1;b<=1;b+=2)

            {

                float2 corner=center+along*a+across*b;

                if(math.dot(corner-curve.a.xz,start)<-.001f || math.dot(corner-curve.d.xz,end)>.001f) return false;

            }

            return true;

        }

        internal static float Step(float length, float overlap) => length * (1f - math.clamp(overlap, 0, 50) * .01f);

        internal static int Count(float length, float tile, float overlap) => length < .1f ? 0 : math.max(1, (int)math.ceil(math.max(0, length - tile) / Step(tile, overlap)) + 1);

    }

    // Decal volumes share one mesh, material and texture; native renderer culling applies.

    // Only derived visuals live here: nothing is added to city serialization.

    public partial class CurbDecalSystem : GameSystemBase

    {

        internal static string Status { get; private set; } = "Off";

        private static bool _requested;

        internal static void RequestRefresh() { _requested = true; }

        private EntityQuery _roads, _nodes, _deleted;

        internal void QueueRoad(Entity road) { Remove(road); if (_build?.Road == road) CancelBuild(); EnqueueRoad(road); }

        private readonly HashSet<Entity> _pending = new HashSet<Entity>();

        private readonly Queue<Entity> _roadQueue=new Queue<Entity>();

        private void EnqueueRoad(Entity road) {if(_pending.Add(road)) _roadQueue.Enqueue(road);}

        private sealed class RoadVisual { internal GameObject Root; internal int Hash, Count; }

        private readonly Dictionary<Entity, RoadVisual> _visuals = new Dictionary<Entity, RoadVisual>();

        private sealed class Build { internal Entity Road; internal RoadVisual Visual; internal List<Tile> Tiles; internal int Cursor; internal bool Limited; }

        private struct Tile { internal float3 Position, Tangent; internal float Length, Rotation; }

        private Build _build;

        private Material _material;

        private Mesh _volume;

        private Texture2D _texture;

        private bool _active, _initial = true, _enabled;

        private int _delay, _total;

        protected override void OnCreate()

        {

            base.OnCreate();

            _roads = GetEntityQuery(ComponentType.ReadOnly<MGRCurbDecalSides>(), ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Composition>(), ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            _nodes = GetEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            _nodes.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.ReadOnly<ConnectedEdge>() });

            _deleted = GetEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Deleted>());

        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode) { base.OnGameLoadingComplete(purpose, mode); Clear(); _active = (mode & GameMode.GameOrEditor) != GameMode.None; _initial = true; _requested = true; }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _active = false; Clear(); }

        protected override void OnDestroy() { Clear(); base.OnDestroy(); }

        internal void Clear()

        {

            CancelBuild(); foreach (var visual in _visuals.Values) Destroy(visual); _visuals.Clear(); _pending.Clear();_roadQueue.Clear(); _total = 0;

            if (_volume != null) UnityEngine.Object.Destroy(_volume); _volume = null;

            if (_material != null) UnityEngine.Object.Destroy(_material); if (_texture != null) UnityEngine.Object.Destroy(_texture);

            _material = null; _texture = null; _enabled = false; Status = "Off";

        }

        private static void Destroy(RoadVisual visual) { if (visual?.Root != null) { visual.Root.SetActive(false); UnityEngine.Object.Destroy(visual.Root); } }

        private void CancelBuild() { if (_build != null) Destroy(_build.Visual); _build = null; }

        private void Remove(Entity road) { if (_visuals.TryGetValue(road, out var v)) { Destroy(v); _total -= v.Count; _visuals.Remove(road); } }

        private bool Live(Entity road) => EntityManager.Exists(road) && !EntityManager.HasComponent<Deleted>(road) && !EntityManager.HasComponent<Game.Tools.Temp>(road);

        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("CurbDecalSystem",started);}
        }
        private void UpdateMeasured()

        {

            if (Mod.Saving) return;

            if (!_active || Mod.Settings == null) return;

            if (_requested) { _requested = false; _delay = 12; }

            if (_delay > 0) { if (--_delay != 0) return; Clear(); _initial = true; _enabled = true; if (!Prepare()) return; }

            if (!_enabled || _material == null) return;

            using (var items = _deleted.ToEntityArray(Allocator.Temp)) foreach (var road in items) { Remove(road); if (_build?.Road == road) CancelBuild(); }

            if (_initial) { using (var items = _roads.ToEntityArray(Allocator.Temp)) foreach (var road in items) EnqueueRoad(road); _initial = false; }

            else

            {

                try

                {

                    _roads.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Composition>() });

                    using (var items = _roads.ToEntityArray(Allocator.Temp)) foreach (var road in items) EnqueueRoad(road);

                    _roads.SetChangedVersionFilter(ComponentType.ReadOnly<Edge>());

                    using (var items = _roads.ToEntityArray(Allocator.Temp)) foreach (var road in items) EnqueueRoad(road);

                    _roads.SetChangedVersionFilter(ComponentType.ReadOnly<Game.Net.SubLane>());

                    using (var items = _roads.ToEntityArray(Allocator.Temp)) foreach (var road in items) EnqueueRoad(road);

                }

                finally { _roads.ResetFilter(); }

                using (var nodes = _nodes.ToEntityArray(Allocator.Temp)) foreach (var node in nodes) foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(node, true)) EnqueueRoad(edge.m_Edge);

            }

            Dependency.Complete();

            // Allocation and projector registration are spread over frames, including paused simulation.

            if (_build == null && _pending.Count > 0)

            {

                Entity road=_roadQueue.Dequeue();

                _pending.Remove(road);

                if (Live(road)) {if(!Begin(road)) EnqueueRoad(road);} else Remove(road);

            }

            if (_build == null) return;

            int budget = 12;

            while (budget-- > 0 && _build.Cursor < _build.Tiles.Count)

            {

                var tile = _build.Tiles[_build.Cursor++]; var go = new GameObject("Curbside decal") { hideFlags = HideFlags.HideAndDontSave };

                go.transform.SetParent(_build.Visual.Root.transform, false);

                var tangent = math.normalizesafe(tile.Tangent, new float3(0, 0, 1));

                var normal = math.normalizesafe(new float3(-tangent.x * tangent.y, tangent.x * tangent.x + tangent.z * tangent.z, -tangent.z * tangent.y), new float3(0, 1, 0));

                go.transform.SetPositionAndRotation(tile.Position + normal * .25f, Quaternion.LookRotation(tangent, normal) * Quaternion.AngleAxis(tile.Rotation, Vector3.up));

                go.transform.localScale = new Vector3(Mod.Settings.CurbDecalWidth, 2f, tile.Length);

                go.AddComponent<MeshFilter>().sharedMesh = _volume;

                var renderer = go.AddComponent<MeshRenderer>();

                renderer.sharedMaterial = _material;

                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;

                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                renderer.renderingLayerMask = (uint)Game.Rendering.DecalLayers.Roads;

            }

            if (_build.Cursor == _build.Tiles.Count)

            {

                var finished = _build; _build = null; if (!Live(finished.Road)) { Destroy(finished.Visual); return; }

                Remove(finished.Road); _visuals[finished.Road] = finished.Visual; _total += finished.Visual.Count; finished.Visual.Root.SetActive(true);

                Status = _total + " mesh decals (no city-wide limit)"+(finished.Limited?" "+DecalStripPolicy.LimitWarning:"");

            }

        }

        private bool Prepare()

        {

            var s = Mod.Settings;

            if (!s.CurbDecalsEnabled || string.IsNullOrEmpty(s.CurbDecalTexture)) { _enabled = false; return false; }

            if (s.CurbDecalTexture != DecalCatalog.BundledValue && Path.GetFileName(s.CurbDecalTexture) != s.CurbDecalTexture) { Status = "Select a PNG from the decal folder."; return false; }

            string error;
            _texture = s.CurbDecalTexture==DecalCatalog.BundledValue ? FileTextureLoader.LoadBundledRoadWear(out error) : FileTextureLoader.LoadPng(Path.Combine(s.CurbDecalFolder, s.CurbDecalTexture), false, out error);

            if (_texture == null) { Status = error; return false; }

            Shader shader = Shader.Find("BH/Decals/DefaultDecalShader");

            if (shader == null || !shader.isSupported) { Status = "The game decal shader is unavailable."; UnityEngine.Object.Destroy(_texture); _texture = null; Mod.Log.Warn("Curbside decals: " + Status); return false; }

            _material = new Material(shader) { name = "Curbside decal slot 1", hideFlags = HideFlags.HideAndDontSave, enableInstancing = false };

            if (!_material.HasProperty("_BaseColorMap") || _material.FindPass("DBufferMesh") < 0) { Status = "The decal shader is incompatible."; UnityEngine.Object.Destroy(_material); UnityEngine.Object.Destroy(_texture); _material = null; _texture = null; return false; }

            _material.SetTexture("_BaseColorMap", _texture); _material.SetColor("_BaseColor", Color.white);

            foreach (var property in new[] { "_AffectNormal", "_AffectMetal", "_AffectAO", "_AffectSmoothness" }) if (_material.HasProperty(property)) _material.SetFloat(property, 0);

            _material.SetFloat("_AffectAlbedo", 1); _material.EnableKeyword("_MATERIAL_AFFECTS_ALBEDO");

            // The shipped shader retains combined-feature variants, not albedo-only.

            // Color masks below keep normals and gloss untouched.

            _material.EnableKeyword("_MATERIAL_AFFECTS_NORMAL"); _material.EnableKeyword("_MATERIAL_AFFECTS_MASKMAP"); _material.DisableKeyword("ENABLE_VT");

            _material.SetFloat("_DecalColorMask0", 15); _material.SetFloat("_DecalColorMask1", 0); _material.SetFloat("_DecalColorMask2", 0); _material.SetFloat("_DecalColorMask3", 0);

            _material.SetFloat("_DecalStencilRef", 16); _material.SetFloat("_DecalStencilWriteMask", 16); _material.SetFloat("_NormalOpacity", 0); _material.SetFloat("_MetallicOpacity", 0);

            _material.SetFloat("colossal_DecalLayerMask", DecalStripPolicy.RoadLayerMask); _material.SetVector("colossal_TextureArea", new Vector4(0, 0, 1, 1));

            _material.SetVector("colossal_MeshSize", Vector4.one); _material.SetFloat("colossal_LodDistanceFactor", 1);

            _material.SetShaderPassEnabled("DBufferProjector", false); _material.SetShaderPassEnabled("DBufferMesh", true); _material.SetShaderPassEnabled("DecalProjectorForwardEmissive", false);

            _volume = CreateVolume();

            Status = "Ready: use the Curbside decals road upgrade"; return true;

        }

        // The native mesh decal pass reconstructs road depth in object space.

        // Use a real unit volume with ordinary per-renderer object matrices;

        // HDRP's generic projector instancing does not supply those inputs.

        internal static Vector3[] VolumeVertices() => new[] {

            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),

            new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),

            new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),

            new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };

        internal static int[] VolumeTriangles() => new[] {

            0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,4,7, 0,7,3,

            1,2,6, 1,6,5, 0,1,5, 0,5,4, 3,7,6, 3,6,2 };

        private static Mesh CreateVolume()

        {

            var mesh = new Mesh { name = "Curbside shared decal volume", hideFlags = HideFlags.HideAndDontSave };

            mesh.vertices = VolumeVertices(); mesh.triangles = VolumeTriangles();

            var normals = new Vector3[8]; var tangents = new Vector4[8];

            for (int i = 0; i < 8; i++) { normals[i] = Vector3.up; tangents[i] = new Vector4(1,0,0,1); }

            mesh.normals = normals; mesh.tangents = tangents; mesh.RecalculateBounds(); mesh.UploadMeshData(true);

            return mesh;

        }

        private bool Begin(Entity road)

        {

            if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<MGRCurbDecalSides>(road)) { Remove(road); return true; }

            if(!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasBuffer<Game.Net.SubLane>(road)) return false;

            var composed=EntityManager.GetComponentData<Composition>(road).m_Edge;

            if(!EntityManager.HasComponent<NetCompositionData>(composed)||!EntityManager.HasBuffer<NetCompositionLane>(composed)) return false;

            float roadWidth=EntityManager.GetComponentData<NetCompositionData>(composed).m_Width;

            if(!math.isfinite(roadWidth)||roadWidth<=.1f||EntityManager.GetBuffer<NetCompositionLane>(composed,true).Length==0) return false;

            var curves = new List<Bezier4x3>(); var leftSides = new List<bool>(); var s = Mod.Settings;

            var curbSystem = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>(); curbSystem.RefreshDefinitionsIfNeeded();

            var footprint = DecalStripPolicy.Footprint(s.CurbDecalWidth, s.CurbDecalLength, s.CurbDecalRotation);

            if (!curbSystem.TryDecalCurves(road, RoadSurfacePolicy.DecalOffset(EntityManager,road), footprint.x, s.CurbDecalSetback, curves, leftSides)) { Remove(road); return true; }

            int hash = 17; foreach (var curve in curves) unchecked { hash = hash * 31 + curve.GetHashCode(); }

            if (_visuals.TryGetValue(road, out var previous) && previous.Hash == hash) return true;

            int capacity=DecalStripPolicy.MaxPerRoad;

            if(capacity<=0) { Status=DecalStripPolicy.LimitWarning; Remove(road); return true; }

            var tiles = new List<Tile>(); int curveIndex=0, placementIndex=0; bool limited=false;

            foreach (var curve in curves)

            {

                float rotation=DecalStripPolicy.SideRotation(s.CurbDecalRotation,leftSides[placementIndex++],s.OrientDecalsToCurb);

                // A distance lookup gives even repeats on curved roads instead of spacing by Bezier t.

                const int samples = 64; var distances = new float[samples + 1]; var before = curve.a;

                for (int i = 1; i <= samples; i++) { var next = MathUtils.Position(curve, i / (float)samples); distances[i] = distances[i - 1] + math.distance(before, next); before = next; }

                float length = distances[samples]; if (!math.isfinite(length) || length < .1f) continue;

                if (length + .001f < footprint.y) continue;

                float tileLength = s.CurbDecalLength; int wanted = DecalStripPolicy.Count(length, footprint.y, s.CurbDecalOverlap);

                int allowance=math.max(1,(capacity-tiles.Count)/math.max(1,curves.Count-curveIndex++));

                int count=math.min(wanted,math.min(allowance,capacity-tiles.Count));
                limited|=count<wanted;

                for (int i = 0; i < count; i++)

                {

                    float start = count < wanted ? (count == 1 ? (length-footprint.y)*.5f : i*(length-footprint.y)/(count-1)) : math.min(i * DecalStripPolicy.Step(footprint.y, s.CurbDecalOverlap), length - footprint.y);

                    float d = start + footprint.y * .5f; int sample = 1; while (sample < samples && distances[sample] < d) sample++;

                    float t = (sample - 1 + math.saturate((d - distances[sample - 1]) / math.max(.0001f, distances[sample] - distances[sample - 1]))) / samples;

                    // Try a bounded inward adjustment before dropping a tile that

                    // cannot clear the retained segment's two end planes.

                    if (!DecalStripPolicy.Fits(curve,t,s.CurbDecalWidth,tileLength,rotation))

                    {

                        bool fit=false; float target=length*.5f;

                        for(int j=1;j<=16;j++)

                        {

                            float next=math.lerp(d,target,j/16f); int k=1; while(k<samples && distances[k]<next) k++;

                            float candidate=(k-1+math.saturate((next-distances[k-1])/math.max(.0001f,distances[k]-distances[k-1])))/samples;

                            if(!DecalStripPolicy.Fits(curve,candidate,s.CurbDecalWidth,tileLength,rotation)) continue;

                            t=candidate; fit=true; break;

                        }

                        if(!fit) continue;

                    }

                    bool duplicate=false;

                    var position=MathUtils.Position(curve,t);

                    foreach(var existing in tiles) if(math.distancesq(existing.Position,position)<.01f) { duplicate=true; break; }

                    if(duplicate) continue;

                    tiles.Add(new Tile { Position = position, Tangent = MathUtils.Tangent(curve, t), Length = tileLength, Rotation = rotation });

                    if (tiles.Count >= capacity) break;

                }

                if (tiles.Count >= capacity) break;

            }

            if (tiles.Count == 0) { Remove(road); return true; }

            if (tiles.Count > capacity) { Status = DecalStripPolicy.LimitWarning; Remove(road); return true; }

            var holder = new GameObject("Curbside road decals") { hideFlags = HideFlags.HideAndDontSave }; holder.SetActive(false);

            _build = new Build { Road = road, Tiles = tiles, Limited=limited, Visual = new RoadVisual { Root = holder, Hash = hash, Count = tiles.Count } };return true;

        }

    }

}



