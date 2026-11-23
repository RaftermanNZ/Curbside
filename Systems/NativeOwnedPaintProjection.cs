using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        internal bool HasOwnedPreviewPaint(Entity owner)
            => _marks.ContainsKey(owner) || _joins.ContainsKey(owner)
                || World.GetExistingSystemManaged<SignalWireSystem>()?.HasPreviewWires(owner) == true;
        // Sparse authoritative output lookup. Native preview hiding of a parent
        // must not be mistaken for the user's intent to hide these markings.
        internal bool ReadOwnedPreviewPaint(Entity owner, List<PreviewPaintStroke> output)
        {
            output.Clear();
            bool found = _marks.TryGetValue(owner, out var marks) || _joins.TryGetValue(owner, out marks);
            if (found) for (int i = 0; i < marks.Entities.Count; i++)
            {
                var actor = marks.Entities[i];
                if (!EntityManager.Exists(actor) || EntityManager.HasComponent<Deleted>(actor)
                    || EntityManager.HasComponent<Temp>(actor) || !EntityManager.HasComponent<Curve>(actor)
                    || !EntityManager.HasComponent<PrefabRef>(actor)) continue;
                output.Add(new PreviewPaintStroke { Actor = actor,
                    NativeSource = i < marks.Curves.Count ? marks.Curves[i].NativeSource : Entity.Null,
                    Curve = EntityManager.GetComponentData<Curve>(actor).m_Bezier,
                    Prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab });
            }
            bool wires = World.GetExistingSystemManaged<SignalWireSystem>()?.AppendPreviewWires(owner, output) == true;
            return found || wires;
        }
    }

    public partial class NativePaintProjectionSystem
    {
        private EntityQuery changedOwners, activeOwners;
        private readonly NativeProjectionLeaseIndex<Entity, Entity> ownerLeases = new NativeProjectionLeaseIndex<Entity, Entity>();
        private readonly HashSet<Entity> pendingOwners = new HashSet<Entity>(), ownerWork = new HashSet<Entity>();
        private readonly List<Entity> ownerDependents = new List<Entity>();

        private static EntityQueryDesc OwnerDescription(bool events, bool node) => new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Temp>(), node ? ComponentType.ReadOnly<Node>() : ComponentType.ReadOnly<Edge>() },
            Any = events ? new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() } : Array.Empty<ComponentType>()
        };
        private void CreateOwnerQueries()
        {
            changedOwners = GetEntityQuery(OwnerDescription(true, false), OwnerDescription(true, true));
            activeOwners = GetEntityQuery(OwnerDescription(false, false), OwnerDescription(false, true));
        }
        internal static void OwnerChanged(World world, Entity source)
        {
            var system = world?.GetExistingSystemManaged<NativePaintProjectionSystem>();
            if (system == null || !system.Enabled || !system.Allowed() || Mod.Saving
                || system.ownerLeases.Count == 0 && system.paintOwnerWatch.Count == 0) return;
            system.pendingOwners.Add(source);
        }
        private void ProjectOwners(bool nativeEvents)
        {
            ownerWork.Clear();
            refreshOwners.Drain(EntityManager, ownerWork);
            if (nativeEvents)
                using (var owners = (includeActive ? activeOwners : changedOwners).ToEntityArray(Allocator.Temp))
                    foreach (var owner in owners) ownerWork.Add(owner);
            foreach (var source in pendingOwners)
            {
                ownerLeases.CopyDependents(source, ownerDependents);
                foreach (var owner in ownerDependents) ownerWork.Add(owner);
                paintOwnerWatch.CopyDependents(source, ownerDependents);
                foreach (var actor in ownerDependents) actorWork.Add(actor);
            }
            foreach (var actor in actorWork)
                if (EntityManager.HasComponent<Owner>(actor))
                {
                    var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
                    if (ownerLeases.TryGet(owner, out _)) ownerWork.Add(owner);
                }
            foreach (var owner in ownerWork) ProjectOwner(owner);
        }
        private void ProjectOwner(Entity owner)
        {
            if (!Live(owner) || !EntityManager.HasComponent<Temp>(owner))
            { ownerLeases.Remove(owner); RemoveReplacement(owner); return; }
            var temp = EntityManager.GetComponentData<Temp>(owner);
            var source = temp.m_Original;
            if (!Live(source) || EntityManager.HasComponent<Temp>(source)
                || (temp.m_Flags & (TempFlags.Hidden | TempFlags.Delete | TempFlags.Cancel)) != 0)
            { ownerLeases.Remove(owner); RemoveReplacement(owner); return; }
            ownerLeases.Set(owner, source, source);
            // If native rendering still shows the original owner, its committed
            // marking actors already provide the correct output. Avoid overlap.
            if (!EntityManager.HasComponent<Hidden>(source)) { RemoveReplacement(owner); return; }
            if (!World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().HasOwnedPreviewPaint(source))
            { RemoveReplacement(owner); return; }
            Bezier4x3 original, preview;
            var range = new float2(0, 1);
            if (EntityManager.HasComponent<Curve>(source) && EntityManager.HasComponent<Curve>(owner))
            {
                original = EntityManager.GetComponentData<Curve>(source).m_Bezier;
                preview = EntityManager.GetComponentData<Curve>(owner).m_Bezier;
                if (!NativePreviewSection.TryRange(EntityManager, owner, source, out range))
                { RemoveReplacement(owner); return; }
            }
            else if (EntityManager.HasComponent<Node>(source) && EntityManager.HasComponent<Node>(owner))
            {
                // Preserve existing junction presentation. A translated native
                // node translates its committed strokes; this does not infer a
                // new junction layout from proximity or re-run a city planner.
                original = TranslationFrame(EntityManager.GetComponentData<Node>(source).m_Position);
                preview = TranslationFrame(EntityManager.GetComponentData<Node>(owner).m_Position);
            }
            else { RemoveReplacement(owner); return; }
            PublishReplacement(owner, source, owner, Entity.Null, original, preview, true, range);
        }
        private static Bezier4x3 TranslationFrame(float3 origin) => new Bezier4x3(origin,
            origin + new float3(0, 0, 1), origin + new float3(0, 0, 2), origin + new float3(0, 0, 3));
        private void ClearOwnerWork() { ownerWork.Clear(); ownerDependents.Clear(); pendingOwners.Clear(); ownerPaths.Clear(); ambiguousOwnerPaths.Clear(); }
    }

    public partial class SignalWireSystem
    {
        internal bool HasPreviewWires(Entity owner) => entries.ContainsKey(owner);
        internal bool AppendPreviewWires(Entity owner, List<PreviewPaintStroke> output)
        {
            if (!entries.TryGetValue(owner, out var entry)) return false;
            foreach (var actor in entry.Wires)
                if (EntityManager.Exists(actor) && !EntityManager.HasComponent<Deleted>(actor)
                    && EntityManager.HasComponent<Curve>(actor) && EntityManager.HasComponent<PrefabRef>(actor))
                    output.Add(new PreviewPaintStroke { Actor = actor,
                        Curve = EntityManager.GetComponentData<Curve>(actor).m_Bezier,
                        Prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab });
            return true;
        }
    }
}
