using System;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalMastSnapshotPolicy
    {
        internal const int ActorLimit = 128;

        internal static bool Pole(string name) => name != null &&
            (name.IndexOf("WirePole", StringComparison.OrdinalIgnoreCase) >= 0 ||
             name.IndexOf("WireTimber", StringComparison.OrdinalIgnoreCase) >= 0);

        // Decode MeshBatch.m_MeshIndex in the same context used by native
        // BatchDataSystem.UpdateObjectData. Invalid data is reported, not read.
        internal static int MeshIndex(int local, int count, bool grouped, int first, int last)
        {
            if (local < 0 || count < 0) return -1;
            if (!grouped) return local < count ? local : -1;
            if (first < 0 || last < first || last > count || local >= last - first) return -1;
            return first + local;
        }

        internal static bool Requirements(uint required, uint available, uint locale, uint currentLocale)
            => (required & available) == required && (locale & currentLocale) == locale;

        internal static bool NormalLayersAllowed(bool nativeOverridden, bool marker, bool outsideConnection, bool editor)
            => !nativeOverridden && (!marker || outsideConnection || editor);
    }

    public partial class FarSideLightSystem
    {
        // Called only by the existing user-requested snapshot, never OnUpdate.
        private void RecordMastSnapshot(Entity node, Entity actor, StaticObjectPrefab model, ref int reported)
        {
            var manager = EntityManager;
            byte style = manager.HasComponent<MGRSignalStyle>(node)
                ? manager.GetComponentData<MGRSignalStyle>(node).Value : (byte)0;
            if (!GardenCityLightPolicy.IsWireStyle(style)) return;
            if (reported++ >= SignalMastSnapshotPolicy.ActorLimit)
            {
                if (reported == SignalMastSnapshotPolicy.ActorLimit + 1)
                    Mod.Log.Info("Mast snapshot limit reached: remaining wire actors omitted; no gameplay state changed.");
                return;
            }

            var prefab = manager.GetComponentData<PrefabRef>(actor).m_Prefab;
            var owner = manager.HasComponent<Owner>(actor) ? manager.GetComponentData<Owner>(actor).m_Owner : Entity.Null;
            var pose = manager.GetComponentData<Transform>(actor);
            bool tracked = _moved.TryGetValue(actor, out var saved);
            Mod.Log.Info("Mast snapshot identity: node=" + node + " actor=" + actor + " style=" + style +
                " owner=" + owner + " currentPrefab=" + prefab + " nativePrefab=" + OriginalPrefab(actor) +
                " ownerHidden=" + (manager.Exists(owner) && manager.HasComponent<Game.Tools.Hidden>(owner)) +
                " toolOverride=" + manager.HasComponent<Game.Tools.Override>(actor) + " highlighted=" + manager.HasComponent<Game.Tools.Highlighted>(actor) +
                " secondary=" + manager.HasComponent<Game.Objects.Secondary>(actor) +
                " alignedSlot=" + (manager.HasComponent<Game.Objects.Aligned>(actor) ? manager.GetComponentData<Game.Objects.Aligned>(actor).m_SubObjectIndex : -1) +
                " tracked=" + tracked + " expectedPrefabMatches=" + (tracked && saved.AppliedPrefab == prefab) +
                " expectedPoseMatches=" + (tracked && saved.Applied.Equals(pose)) +
                " updated=" + manager.HasComponent<Updated>(actor) + " batchesUpdated=" + manager.HasComponent<BatchesUpdated>(actor) +
                " lightReset=" + manager.HasComponent<MGRLightReset>(actor) + " position=" + pose.m_Position + " rotation=" + pose.m_Rotation);

            // Native static-object culling uses Common.Overridden, not the
            // Tools.Override preview marker. Calculate the current native rule
            // read-only to distinguish suppression from stale cached culling.
            bool nativeOverridden = manager.HasComponent<Game.Common.Overridden>(actor);
            bool marker = manager.HasComponent<Game.Objects.Marker>(actor);
            bool outside = manager.HasComponent<Game.Objects.OutsideConnection>(actor);
            bool editor = (Game.SceneFlow.GameManager.instance.gameMode & Game.GameMode.Editor) != 0;
            bool geometryReady = manager.HasComponent<ObjectGeometryData>(prefab);
            MeshLayer prefabLayers = geometryReady ? manager.GetComponentData<ObjectGeometryData>(prefab).m_Layers : 0;
            MeshLayer resolvedLayers = 0;
            if (geometryReady)
            {
                var references = GetComponentLookup<PrefabRef>(true);
                var nets = GetComponentLookup<NetData>(true);
                var netGeometry = GetComponentLookup<NetGeometryData>(true);
                var nativeOwner = manager.HasComponent<Owner>(actor) ? manager.GetComponentData<Owner>(actor) : default;
                resolvedLayers = Game.Net.SearchSystem.GetLayers(nativeOwner, default, prefabLayers, ref references, ref nets, ref netGeometry);
            }
            bool allowed = SignalMastSnapshotPolicy.NormalLayersAllowed(nativeOverridden, marker, outside, editor);
            BoundsMask expectedMask = BoundsMask.Debug;
            if (geometryReady && allowed) expectedMask |= CommonUtils.GetBoundsMask(resolvedLayers);
            Mod.Log.Info("Mast snapshot native admission: actor=" + actor + " nativeOverridden=" + nativeOverridden +
                " marker=" + marker + " outsideConnection=" + outside + " editor=" + editor + " geometryReady=" + geometryReady +
                " prefabLayers=" + prefabLayers + " resolvedLayers=" + resolvedLayers + " normalLayersAllowed=" + allowed +
                " expectedStaticMask=" + expectedMask + " hasTransformFrames=" + manager.HasBuffer<Game.Objects.TransformFrame>(actor));

            if (manager.HasComponent<Game.Objects.Relative>(actor))
            {
                var relative = manager.GetComponentData<Game.Objects.Relative>(actor);
                Mod.Log.Info("Mast snapshot relative: actor=" + actor + " position=" + relative.m_Position +
                    " rotation=" + relative.m_Rotation + " bone=" + relative.m_BoneIndex);
            }
            if (manager.HasComponent<Game.Objects.NetObject>(actor))
                Mod.Log.Info("Mast snapshot net state: actor=" + actor + " flags=" + manager.GetComponentData<Game.Objects.NetObject>(actor).m_Flags);
            if (manager.HasComponent<CullingInfo>(actor))
            {
                var culling = manager.GetComponentData<CullingInfo>(actor);
                Mod.Log.Info("Mast snapshot culling: actor=" + actor + " bounds=" + culling.m_Bounds +
                    " radius=" + culling.m_Radius + " index=" + culling.m_CullingIndex + " mask=" + culling.m_Mask +
                    " minLod=" + culling.m_MinLod + " passed=" + culling.m_PassedCulling);
            }

            int authoredPoles = 0;
            var authored = model == null ? null : model.m_Meshes;
            if (authored != null)
                for (int part = 0; part < authored.Length; part++)
                {
                    var mesh = authored[part];
                    if (mesh == null) continue;
                    string name = mesh.m_Mesh == null ? "missing" : mesh.m_Mesh.name;
                    if (SignalMastSnapshotPolicy.Pole(name)) authoredPoles++;
                    Mod.Log.Info("Mast snapshot authored part: actor=" + actor + " part=" + part + " mesh=" + name +
                        " pole=" + SignalMastSnapshotPolicy.Pole(name) + " requireState=" + mesh.m_RequireState +
                        " position=" + mesh.m_Position + " rotation=" + mesh.m_Rotation);
                }

            bool hasParts = manager.HasBuffer<SubMesh>(prefab);
            bool hasGroups = manager.HasBuffer<SubMeshGroup>(prefab);
            bool hasActorGroups = manager.HasBuffer<MeshGroup>(actor);
            bool hasBatches = manager.HasBuffer<MeshBatch>(actor);
            int partCount = hasParts ? manager.GetBuffer<SubMesh>(prefab, true).Length : 0;
            int groupCount = hasGroups ? manager.GetBuffer<SubMeshGroup>(prefab, true).Length : 0;
            int actorGroupCount = hasActorGroups ? manager.GetBuffer<MeshGroup>(actor, true).Length : 0;
            int batchCount = hasBatches ? manager.GetBuffer<MeshBatch>(actor, true).Length : 0;
            Mod.Log.Info("Mast snapshot summary: actor=" + actor + " authoredPoleParts=" + authoredPoles +
                " subMeshBuffer=" + hasParts + " subMeshes=" + partCount + " prefabGroupBuffer=" + hasGroups +
                " prefabGroups=" + groupCount + " actorGroupBuffer=" + hasActorGroups + " actorGroups=" + actorGroupCount +
                " meshBatchBuffer=" + hasBatches + " meshBatches=" + batchCount + " controlledHidden=" + _hiddenSignals.ContainsKey(actor));

            if (hasGroups)
                for (int g = 0; g < groupCount; g++)
                {
                    var group = manager.GetBuffer<SubMeshGroup>(prefab, true)[g];
                    Mod.Log.Info("Mast snapshot prefab group: actor=" + actor + " group=" + g +
                        " range=" + group.m_SubMeshRange + " children=" + group.m_SubGroupCount + " flags=" + group.m_Flags);
                }
            if (hasActorGroups)
                for (int g = 0; g < actorGroupCount; g++)
                {
                    var group = manager.GetBuffer<MeshGroup>(actor, true)[g];
                    Mod.Log.Info("Mast snapshot actor group: actor=" + actor + " group=" + g +
                        " prefabGroup=" + group.m_SubMeshGroup + " meshOffset=" + group.m_MeshOffset + " colorOffset=" + group.m_ColorOffset);
                }

            for (int part = 0; part < partCount; part++)
            {
                var mesh = manager.GetBuffer<SubMesh>(prefab, true)[part];
                Mod.Log.Info("Mast snapshot initialized part: actor=" + actor + " part=" + part + " mesh=" + mesh.m_SubMesh +
                    " requiredFlags=" + mesh.m_Flags + " localeFlags=" + mesh.m_LocaleFlags +
                    " position=" + mesh.m_Position + " rotation=" + mesh.m_Rotation + " randomSeed=" + mesh.m_RandomSeed);
                if (manager.Exists(mesh.m_SubMesh) && manager.HasComponent<MeshData>(mesh.m_SubMesh))
                {
                    var data = manager.GetComponentData<MeshData>(mesh.m_SubMesh);
                    int groups = manager.HasBuffer<BatchGroup>(mesh.m_SubMesh) ? manager.GetBuffer<BatchGroup>(mesh.m_SubMesh, true).Length : 0;
                    Mod.Log.Info("Mast snapshot mesh resource: actor=" + actor + " part=" + part +
                        " availableLayers=" + data.m_AvailableLayers + " defaultLayers=" + data.m_DefaultLayers +
                        " availableTypes=" + data.m_AvailableTypes + " meshState=" + data.m_State + " minLod=" + data.m_MinLod +
                        " lodBias=" + data.m_LodBias + " bounds=" + data.m_Bounds + " batchGroups=" + groups);
                    for (int g = 0; g < groups && g < 32; g++)
                    {
                        var group = manager.GetBuffer<BatchGroup>(mesh.m_SubMesh, true)[g];
                        Mod.Log.Info("Mast snapshot resource group: actor=" + actor + " part=" + part + " group=" + g +
                            " nativeGroup=" + group.m_GroupIndex + " merge=" + group.m_MergeIndex + " layer=" + group.m_Layer +
                            " type=" + group.m_Type + " partition=" + group.m_Partition);
                    }
                }
            }

            for (int b = 0; b < batchCount; b++)
            {
                var batch = manager.GetBuffer<MeshBatch>(actor, true)[b];
                int first = 0, last = partCount;
                bool grouped = false, validGroup = true;
                if (hasActorGroups && hasGroups)
                {
                    validGroup = batch.m_MeshGroup < actorGroupCount;
                    if (validGroup)
                    {
                        int g = manager.GetBuffer<MeshGroup>(actor, true)[batch.m_MeshGroup].m_SubMeshGroup;
                        validGroup = g < groupCount;
                        if (validGroup)
                        {
                            var range = manager.GetBuffer<SubMeshGroup>(prefab, true)[g].m_SubMeshRange;
                            first = range.x; last = range.y; grouped = true;
                        }
                    }
                }
                int decoded = validGroup ? SignalMastSnapshotPolicy.MeshIndex(batch.m_MeshIndex, partCount, grouped, first, last) : -1;
                Mod.Log.Info("Mast snapshot draw entry: actor=" + actor + " batch=" + b + " nativeGroup=" + batch.m_GroupIndex +
                    " instance=" + batch.m_InstanceIndex + " meshGroup=" + batch.m_MeshGroup + " localPart=" + batch.m_MeshIndex +
                    " decodedPart=" + decoded + " validGroup=" + validGroup + " tile=" + batch.m_TileIndex);
            }
        }
    }
}
