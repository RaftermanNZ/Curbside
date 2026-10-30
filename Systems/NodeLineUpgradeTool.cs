using System;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Entities;

using Unity.Jobs;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    public struct MGRHideNodeLines : IComponentData, IEmptySerializable { }
    public struct MGRJunctionEdgeLines : IComponentData, IEmptySerializable { }
    public struct MGRRemoveJunctionEdges : IComponentData, IEmptySerializable { }

    internal static class NodeLinePolicy

    {

        internal static bool IsLaneLine(string name)

        {

            if (string.IsNullOrEmpty(name)) return false;

            if (IsProtected(name)) return false;

            foreach (var word in Lines) if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;

        }
        internal static bool IsLineGeometry(NetLaneGeometryPrefab prefab)
        {
            if(prefab==null||IsProtected(prefab.name))return false;
            if(IsLaneLine(prefab.name))return true;
            if(prefab.m_Meshes==null||prefab.m_Meshes.Length==0)return false;
            bool any=false;
            foreach(var info in prefab.m_Meshes)
            {
                if(info?.m_Mesh==null)continue;
                if(!info.m_Mesh.Has<DecalProperties>()||!IsLaneLine(info.m_Mesh.name)||IsProtected(info.m_Mesh.name))return false;
                any=true;
            }
            return any;
        }

        internal static bool IsProtected(string name)

        {

            if (string.IsNullOrEmpty(name)) return true;

            foreach (var word in Protected) if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;

        }

        internal static bool IsPaintFill(string name)

        {

            if (IsProtected(name)) return false;

            foreach (var word in Fill) if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;

        }

        private static readonly string[] Fill = { "hatch", "chevron", "painted triangle", "paint fill", "painted island", "island marking", "junction fill" };

        private static readonly string[] Protected = { "stop", "yield", "crosswalk", "crossing", "zebra", "arrow" };

        private static readonly string[] Lines = { "edge line", "center line", "centre line", "lane line", "divider line", "lane divider", "merge line", "bay line", "guide line", "guideline", "guidance line", "dashed line", "solid line" };

    }

    public partial class NodeLineUpgradeTool : ToolBaseSystem

    {

        internal const string AssetName = "MGR Hide Node Lines";
        internal const string EdgeAssetName = "MGR Junction Edge Lines";

        private PrefabBase _selected;

        public override string toolID => "MGRNodeLineUpgrade";

        public override PrefabBase GetPrefab() => _selected;

        public override bool TrySetPrefab(PrefabBase prefab) { if (prefab == null || prefab.name != AssetName&&prefab.name!=EdgeAssetName) return false; if (_selected != prefab) { _selected = prefab; m_ToolSystem.EventPrefabChanged?.Invoke(prefab); } return true; }

        protected override void OnStartRunning() { base.OnStartRunning(); applyAction.shouldBeEnabled = true; secondaryApplyAction.shouldBeEnabled = true; cancelAction.shouldBeEnabled = true; }

        public override void InitializeRaycast() { base.InitializeRaycast(); m_ToolRaycastSystem.typeMask = TypeMask.Net; m_ToolRaycastSystem.netLayerMask = Layer.Road; m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground; }

        protected override JobHandle OnUpdate(JobHandle deps)

        {

            if(m_ToolSystem.activeTool!=this || Mod.Saving) return deps;

            bool remove = UpgradeInputPolicy.RemovePressed(secondaryApplyAction.WasPressedThisFrame());

            if (UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame())) { m_ToolSystem.activeTool = m_DefaultToolSystem; return deps; }

            if (!m_HasFocus || UpgradeInputPolicy.PointerBlocked || !m_ToolRaycastSystem.GetRaycastResult(out var hit)) return deps;

            deps.Complete(); var node = hit.m_Owner;

            if (EntityManager.HasComponent<Temp>(node)) node = EntityManager.GetComponentData<Temp>(node).m_Original;

            for (int i = 0; i < 8 && !EntityManager.HasComponent<Node>(node) && !EntityManager.HasComponent<Edge>(node) && EntityManager.HasComponent<Owner>(node); i++) node = EntityManager.GetComponentData<Owner>(node).m_Owner;

            if (EntityManager.HasComponent<Edge>(node))

            {

                var edge = EntityManager.GetComponentData<Edge>(node); bool end;

                if (EntityManager.HasComponent<EdgeGeometry>(node)) end = NativeCrosswalkTargeting.NearEnd(EntityManager.GetComponentData<EdgeGeometry>(node), hit.m_Hit.m_HitPosition);

                else if (EntityManager.HasComponent<Curve>(node)) { MathUtils.Distance(EntityManager.GetComponentData<Curve>(node).m_Bezier, hit.m_Hit.m_HitPosition, out float t); end = t > .5f; } else return default;

                node = end ? edge.m_End : edge.m_Start;

            }

            if (!EntityManager.HasComponent<Node>(node) || EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node) || !EntityManager.HasBuffer<ConnectedEdge>(node)) return default;

            var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>(); var buffer = overlay.GetBuffer(out var pending); pending.Complete(); bool roadFound = false;

            foreach (var entry in EntityManager.GetBuffer<ConnectedEdge>(node, true))

            {

                var road = entry.m_Edge; if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<Curve>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Temp>(road)) continue;

                roadFound = true; var edge = EntityManager.GetComponentData<Edge>(road); var range = edge.m_Start == node ? new float2(0, .12f) : new float2(.88f, 1);

                buffer.DrawCurve(new UnityEngine.Color(.15f, 1f, .3f, .8f), MathUtils.Cut(EntityManager.GetComponentData<Curve>(road).m_Bezier, range), 2f);

            }

            if (!roadFound || (!applyAction.WasPressedThisFrame() && !remove)) return default;

            bool edges=_selected?.name==EdgeAssetName;
            bool has=edges?EntityManager.HasComponent<MGRJunctionEdgeLines>(node):EntityManager.HasComponent<MGRHideNodeLines>(node);
            if(!edges&&has==!remove){UpgradeFeedback.Report(_selected.name,remove?"This junction upgrade is already removed.":"This junction upgrade is already applied.");return default;}
            if(edges)
            {
                if(remove){if(has)EntityManager.RemoveComponent<MGRJunctionEdgeLines>(node);if(!EntityManager.HasComponent<MGRRemoveJunctionEdges>(node))EntityManager.AddComponent<MGRRemoveJunctionEdges>(node);}
                else {if(EntityManager.HasComponent<MGRRemoveJunctionEdges>(node))EntityManager.RemoveComponent<MGRRemoveJunctionEdges>(node);if(!has)EntityManager.AddComponent<MGRJunctionEdgeLines>(node);}
            }
            else {if(remove)EntityManager.RemoveComponent<MGRHideNodeLines>(node);else EntityManager.AddComponent<MGRHideNodeLines>(node);}
            foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(EntityManager.HasComponent<Road>(entry.m_Edge)){CurbLineEmissionSystem.QueueRoad(entry.m_Edge);SegmentUpgradePolicy.QueuePaint(EntityManager,entry.m_Edge);}
            UpgradeFeedback.Report(_selected.name,edges?(remove?"Junction edge paint removed, including native edge lines. Centerlines, crossings and stop bars are kept.":"Junction edge lines applied; native hiding choices do not suppress the addition."):(remove?"Junction line visibility restored.":"Junction lane lines hidden; crossings and stop bars kept."));

            JunctionMarkingSystem.RequestNodeRefresh(node);

            World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().QueueNodeRefresh(node);

            return default;

        }

    }

}

