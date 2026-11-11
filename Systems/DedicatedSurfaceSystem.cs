using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MedianGrassReplacement.Systems
{
    // Stable names are save identities. Register during prefab loading, before
    // the game's PrefabRef deserialization, rather than after a city has loaded.
    public partial class DedicatedSurfaceSystem : GameSystemBase
    {
        internal const string Roadway = "Curbside Roadway Surface";
        internal const string Sidewalk = "Curbside Sidewalk Surface";
        internal const string FallbackCategory = "Curbside Surfaces";
        private PrefabSystem _prefabs;
        private EntityQuery _surfaces, _configuration, _menus;
        private SurfacePrefab _roadway, _sidewalk;
        private UIGroupPrefab _initialGroup;
        private bool _failed;
        private readonly List<UnityEngine.Object> _resources = new List<UnityEngine.Object>();
        internal bool RegistrationFailed => _failed;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _surfaces = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<SurfaceData>());
            _configuration = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<AreasConfigurationData>());
            _menus = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<UIAssetMenuData>());
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (!_failed) Enabled = true;
        }

        // Exact native pavement identities; never borrow another pack's material.
        private SurfacePrefab Donor(string name)
        {
            using (var entities = _surfaces.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    if (_prefabs.TryGetPrefab<SurfacePrefab>(entity, out var prefab)
                        && prefab.name == name && prefab.isBuiltin) return prefab;
            return null;
        }

        internal UIGroupPrefab CurrentSurfaceGroup()
        {
            foreach (string name in new[] { "Pavement Surface 02", "Pavement Surface 01" })
            {
                var donor = Donor(name);
                if (donor != null && donor.TryGet<UIObject>(out var ui)
                    && ui.active && !ui.m_IsDebugObject && ui.m_Group != null) return ui.m_Group;
            }
            return _initialGroup;
        }

        private UIGroupPrefab ResolveInitialGroup()
        {
            var existing = CurrentSurfaceGroup();
            if (existing != null) return existing;
            // Older detailing mods expose surfaces only after loading. Keep a
            // native Landscaping category available when no provider does so.
            using (var entities = _menus.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    if (!_prefabs.TryGetPrefab<UIAssetMenuPrefab>(entity, out var menu) || menu.name != "Landscaping") continue;
                    var category = ScriptableObject.CreateInstance<UIAssetCategoryPrefab>();
                    category.name = FallbackCategory;
                    category.m_Menu = menu;
                    var ui = category.AddComponent<UIObject>();
                    // UIAssetCategoryPrefab adds itself to m_Menu. UIObject's
                    // group stays empty so native initialization adds it once.
                    ui.m_Icon = "coui://mgr-icons/PlaceableSurfaces.svg";
                    ui.m_Priority = 50;
                    if (_prefabs.AddPrefab(category)) return category;
                    UnityEngine.Object.Destroy(category);
                    throw new InvalidOperationException("Could not register the Curbside surface category.");
                }
            return null;
        }

        protected override void OnUpdate()
        {
            if (_failed || (_roadway != null && _sidewalk != null)) { Enabled = false; return; }
            if (_configuration.IsEmptyIgnoreFilter) return;
            Material template = null;
            using (var entities = _configuration.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    if (_prefabs.TryGetPrefab<AreasConfigurationPrefab>(entity, out var config)) { template = config.m_RenderedAreaMaterial; break; }
            if (template == null) return;
            try
            {
                if (_initialGroup == null) _initialGroup = ResolveInitialGroup();
                if (_initialGroup == null) return;
                if (_roadway == null) _roadway = Create(Roadway, SurfaceTextureCatalog.BundledRoadPath, "PlaceableRoadway.svg", template, .16f, 20);
                if (_sidewalk == null) _sidewalk = Create(Sidewalk, SurfaceTextureCatalog.BundledSidewalkPath, "PlaceableSidewalk.svg", template, .2f, 21);
                Mod.Log.Info("Dedicated sidewalk and roadway surface prefabs registered with stable save identities.");
                Enabled = false;
            }
            catch (Exception error)
            {
                _failed = true; Enabled = false;
                Mod.Log.Error(error, "Could not register Curbside's dedicated surfaces.");
            }
        }

        private SurfacePrefab Create(string name, string path, string icon, Material template, float uv, int priority)
        {
            SurfacePrefab prefab = null; Material material = null; Texture2D texture = null;
            try
            {
                texture = FileTextureLoader.LoadPng(path, false, out var error, false, 128L * 1024 * 1024, 4096);
                if (texture == null) throw new InvalidOperationException(error);
                // These belong to persistent surface prefabs, not reversible
                // global replacements. Keep them outside that recovery namespace.
                texture.name = name + " Albedo";
                material = new Material(template) { name = name + " Material", hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture("_BaseColorMap", texture);
                material.SetTextureScale("_BaseColorMap", Vector2.one);
                material.SetTextureOffset("_BaseColorMap", Vector2.zero);
                prefab = ScriptableObject.CreateInstance<SurfacePrefab>();
                prefab.name = name;
                prefab.m_Color = new Color(1f, 1f, 1f, .05f);
                var rendered = prefab.AddComponent<RenderedArea>();
                // The native AreaBatchSystem copies this owned material. Empty
                // AssetReferences prevent it streaming a donor texture over ours.
                rendered.m_Material = material;
                rendered.m_Version = RenderedArea.Version.OverrideMaterial;
                rendered.m_BaseColor = Color.white;
                rendered.m_Metallic = 0f;
                rendered.m_Smoothness = .25f;
                rendered.m_NormalOpacity = 0f;
                rendered.m_MetallicOpacity = 1f;
                rendered.m_UVScale = uv;
                rendered.m_RendererPriority = -96;
                var ui = prefab.AddComponent<UIObject>();
                ui.m_Group = _initialGroup;
                ui.m_Priority = priority;
                ui.m_Icon = "coui://mgr-icons/" + icon;
                if (!_prefabs.AddPrefab(prefab)) throw new InvalidOperationException("Native prefab registration rejected " + name);
                _resources.Add(material);
                _resources.Add(texture);
                return prefab;
            }
            catch
            {
                if (prefab != null) { foreach (var component in prefab.components) UnityEngine.Object.Destroy(component); UnityEngine.Object.Destroy(prefab); }
                if (material != null) UnityEngine.Object.Destroy(material);
                if (texture != null) UnityEngine.Object.Destroy(texture);
                throw;
            }
            // On success, the native prefab and area-batch copies own these
            // resources for the world's lifetime. City switches must retain them.
        }

        protected override void OnDestroy()
        {
            // No city-preload cleanup: registered prefabs survive city switches.
            foreach (var resource in _resources) if (resource != null) UnityEngine.Object.Destroy(resource);
            _resources.Clear();
            base.OnDestroy();
        }

        internal bool AttachToSurfaceMenu()
        {
            if (_roadway == null || _sidewalk == null) return false;
            var group = CurrentSurfaceGroup();
            if (group == null || !_prefabs.TryGetEntity(group, out var groupEntity)
                || !EntityManager.HasBuffer<UIGroupElement>(groupEntity)) return false;
            foreach (var prefab in new[] { _roadway, _sidewalk })
            {
                if (!_prefabs.TryGetEntity(prefab, out var entity) || !EntityManager.HasComponent<UIObjectData>(entity)
                    || EntityManager.HasComponent<Game.Common.Created>(entity)) return false;
                var data = EntityManager.GetComponentData<UIObjectData>(entity);
                if (data.m_Group != groupEntity && data.m_Group != Entity.Null && EntityManager.Exists(data.m_Group))
                {
                    if (EntityManager.HasBuffer<UIGroupElement>(data.m_Group))
                    {
                        var old = EntityManager.GetBuffer<UIGroupElement>(data.m_Group);
                        for (int i = old.Length - 1; i >= 0; i--) if (old[i].m_Prefab == entity) old.RemoveAt(i);
                    }
                    if (EntityManager.HasBuffer<UnlockRequirement>(data.m_Group))
                    {
                        var old = EntityManager.GetBuffer<UnlockRequirement>(data.m_Group);
                        for (int i = old.Length - 1; i >= 0; i--) if (old[i].m_Prefab == entity) old.RemoveAt(i);
                    }
                }
                bool found = false;
                var elements = EntityManager.GetBuffer<UIGroupElement>(groupEntity);
                for (int i = 0; i < elements.Length; i++) if (elements[i].m_Prefab == entity) { found = true; break; }
                if (!found) group.AddElement(EntityManager, entity);
                var ui = prefab.GetComponent<UIObject>();
                ui.m_Group = group;
                data.m_Group = groupEntity;
                data.m_Priority = ui.m_Priority;
                EntityManager.SetComponentData(entity, data);
                // Only our decorative paint surfaces are immediately available.
                if (EntityManager.HasComponent<Locked>(entity)) EntityManager.SetComponentEnabled<Locked>(entity, false);
                if (!EntityManager.HasComponent<Unlock>(entity)) EntityManager.AddComponent<Unlock>(entity);
            }
            Mod.Log.Info("Curbside sidewalk and roadway entries attached to surface group: " + group.name);
            return true;
        }
    }

    // Provider menus (including Extra Detailing Tools) finish during loading.
    // Reconcile once afterward; no rendering scans or permanent frame polling.
    public partial class DedicatedSurfaceMenuSystem : GameSystemBase
    {
        private int _attempts;
        protected override void OnCreate() { base.OnCreate(); Enabled = false; }
        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); Enabled = false; }
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _attempts = 0;
            Enabled = (mode & GameMode.GameOrEditor) != GameMode.None;
        }
        protected override void OnUpdate()
        {
            var surfaces = World.GetOrCreateSystemManaged<DedicatedSurfaceSystem>();
            if (surfaces.RegistrationFailed || surfaces.AttachToSurfaceMenu()) { Enabled = false; return; }
            if (++_attempts >= 8)
            {
                Mod.Log.Warn("Dedicated surfaces could not finish attaching to the surface menu after loading.");
                Enabled = false;
            }
        }
    }
}
