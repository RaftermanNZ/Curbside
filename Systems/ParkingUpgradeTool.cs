using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Entities;

using Unity.Jobs;

namespace MedianGrassReplacement.Systems

{

    // Stored on the runtime road section, never on its shared prefab.

    public struct MGRParkingUpgrade : IComponentData, ISerializable

    {

        public bool Marked;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Marked); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Marked); }

    }

    public enum ParkingBayStyle : byte { BoundaryAndTs = 0, TsOnly = 1, CurbDividers = 2, FullDepthTs = 3, DividersOnly = 4 }

    public struct MGRParkingStyle : IComponentData, ISerializable

    {

        public byte Value;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Value); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Value); }

    }

    public partial class ParkingUpgradeTool : ToolBaseSystem

    {

        private PrefabBase _selected;

        public override string toolID => "MGRParkingUpgrade";

        internal void Select(PrefabBase prefab) { if (_selected == prefab) return; _selected = prefab; m_ToolSystem.EventPrefabChanged?.Invoke(prefab); }

        public override PrefabBase GetPrefab() => _selected;

        public override bool TrySetPrefab(PrefabBase prefab) { if (prefab == null || !CrosswalkUpgradeSystem.IsParkingName(prefab.name)) return false; Select(prefab); return true; }

        protected override void OnStartRunning() { base.OnStartRunning(); applyAction.shouldBeEnabled = true; secondaryApplyAction.shouldBeEnabled = true; cancelAction.shouldBeEnabled = true; }

        public override void InitializeRaycast() { base.InitializeRaycast(); m_ToolRaycastSystem.typeMask = TypeMask.Net; m_ToolRaycastSystem.netLayerMask = Layer.Road; m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground; }

        protected override JobHandle OnUpdate(JobHandle deps)

        {

            if(m_ToolSystem.activeTool!=this || Mod.Saving) return deps;

            bool remove = UpgradeInputPolicy.RemovePressed(secondaryApplyAction.WasPressedThisFrame());

            if (UpgradeInputPolicy.CancelPressed(cancelAction.WasPressedThisFrame())) { m_ToolSystem.activeTool = m_DefaultToolSystem; return deps; }

            if (!m_HasFocus || UpgradeInputPolicy.PointerBlocked || !m_ToolRaycastSystem.GetRaycastResult(out var hit)) return deps;

            deps.Complete();if(!SectionUpgradeTargeting.TryRoad(EntityManager,hit.m_Owner,hit.m_Hit.m_HitPosition,out var road))return default;

            if (!EntityManager.HasComponent<Road>(road) || !EntityManager.HasComponent<Curve>(road) || !EntityManager.HasBuffer<Game.Net.SubLane>(road) || EntityManager.HasComponent<Deleted>(road) || EntityManager.HasComponent<Temp>(road)) return default;

            World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().RetainPaintTarget(road);
            bool left = RoadSideUpgrades.IsLeft(EntityManager, road, hit.m_Hit.m_HitPosition);

            bool eligible = (left ? RoadSideUpgrades.Parking(EntityManager, road).Left : RoadSideUpgrades.Parking(EntityManager, road).Right) != 0; foreach (var lane in EntityManager.GetBuffer<Game.Net.SubLane>(road, true))

            {

                if (!EntityManager.HasComponent<Game.Net.ParkingLane>(lane.m_SubLane)) continue;

                var flags = EntityManager.GetComponentData<Game.Net.ParkingLane>(lane.m_SubLane).m_Flags;

                if ((flags & (ParkingLaneFlags.ParkingDisabled | ParkingLaneFlags.VirtualLane | ParkingLaneFlags.SpecialVehicles)) == 0 && EntityManager.HasComponent<Curve>(lane.m_SubLane) && RoadSideUpgrades.IsLeft(EntityManager, road, MathUtils.Position(EntityManager.GetComponentData<Curve>(lane.m_SubLane).m_Bezier, .5f)) == left) eligible = true;

            }

            if (!eligible) return default;

            var overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>(); var buffer = overlay.GetBuffer(out var pending); pending.Complete(); buffer.DrawCurve(new UnityEngine.Color(.15f, 1f, .3f, .8f), RoadSideUpgrades.Preview(EntityManager, road, left), 2f);

            if (applyAction.WasPressedThisFrame() || remove)

            {

                bool marked = _selected != null && _selected.name != CrosswalkUpgradeSystem.UnmarkedName;

                byte mode = remove ? (byte)0 : !marked ? (byte)1 : (byte)(_selected.name == CrosswalkUpgradeSystem.TOnlyName ? 3 : _selected.name == CrosswalkUpgradeSystem.NotchedName ? 4 : _selected.name == CrosswalkUpgradeSystem.FullTName ? 5 : _selected.name == CrosswalkUpgradeSystem.DividerOnlyName ? 6 : 2);

                World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().Forget(road);
                // Applying a bay tool supersedes our ban on this side only.
                // Removing a bay never removes an independently applied ban.
                if(!remove)ParkingBanPolicy.ClearSide(EntityManager,road,left);
                var sides = RoadSideUpgrades.SetParking(RoadSideUpgrades.Parking(EntityManager, road), left, mode);

                if (sides.Left == 0 && sides.Right == 0)

                {

                    if (EntityManager.HasComponent<MGRParkingUpgrade>(road)) EntityManager.RemoveComponent<MGRParkingUpgrade>(road);

                    if (EntityManager.HasComponent<MGRParkingStyle>(road)) EntityManager.RemoveComponent<MGRParkingStyle>(road);

                    if (EntityManager.HasComponent<MGRParkingSides>(road)) EntityManager.RemoveComponent<MGRParkingSides>(road);

                }

                else

                {

                    if (!EntityManager.HasComponent<MGRParkingSides>(road)) EntityManager.AddComponent<MGRParkingSides>(road);

                    EntityManager.SetComponentData(road, sides);

                    if (!EntityManager.HasComponent<MGRParkingUpgrade>(road)) EntityManager.AddComponent<MGRParkingUpgrade>(road);

                    EntityManager.SetComponentData(road, new MGRParkingUpgrade { Marked = sides.Left >= 2 || sides.Right >= 2 });

                }

                FixedParkingSystem.QueueRoad(road); CurbLineEmissionSystem.QueueRoad(road);SegmentUpgradePolicy.QueuePaint(EntityManager,road);
                UpgradeFeedback.Report(_selected.name,remove?"Parking bay upgrade removed on this side.":"Parking bays applied on this side; No parking cleared here.");

                Mod.Debug(remove ? "Parking upgrade removed; native parking restored." : marked ? "Road section selected for marked parking bays." : "Road section restored to unmarked native parking.");

            }

            return default;

        }

    }

}

