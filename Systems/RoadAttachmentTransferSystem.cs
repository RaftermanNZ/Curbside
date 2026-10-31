using System.Collections.Generic;

using Colossal.Mathematics;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Tools;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    // Shared selection transfer. Native render entities are always regenerated;

    // only opt-in side masks follow the game's committed road replacements.

    public partial class RoadAttachmentTransferSystem : GameSystemBase

    {
        private readonly Dictionary<Entity,Selection> _sourceSelections=new Dictionary<Entity,Selection>();


        private struct Selection { internal byte Powerlines,Decals,ParkingBans,ParkingLeft,ParkingRight,CenterStyle,DividerStyle,EdgeStyle,EdgeLeft,EdgeRight,SurfaceRoad,SurfaceWalk,HatchSides,StreetLighting,LineVisibility,HiddenLeft,HiddenRight,LightingPrevious;internal bool HideCenter,LightingOwned; }

        private EntityQuery _previews,_targets;

        private sealed class Commit {internal Entity Original;internal Bezier4x3 Curve;internal Selection Selection;internal int Tries=4;}

        private readonly Dictionary<Entity,Commit> _commits=new Dictionary<Entity,Commit>();

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode) {base.OnGamePreload(purpose,mode);_commits.Clear();_sourceSelections.Clear();}

        private Entity Original(Entity entity)

        {

            for(int i=0;i<4 && EntityManager.HasComponent<Temp>(entity);i++) {var next=EntityManager.GetComponentData<Temp>(entity).m_Original;if(next==Entity.Null || next==entity) break;entity=next;}return entity;

        }

        private bool ApplySelection(Entity road,Selection selection)

        {

            bool changed=false;

            if((selection.SurfaceRoad|selection.SurfaceWalk)!=0){var desired=new MGRSurfaceSlots{Road=selection.SurfaceRoad,Sidewalk=selection.SurfaceWalk};if(!EntityManager.HasComponent<MGRSurfaceSlots>(road)||EntityManager.GetComponentData<MGRSurfaceSlots>(road).Road!=desired.Road||EntityManager.GetComponentData<MGRSurfaceSlots>(road).Sidewalk!=desired.Sidewalk){if(!EntityManager.HasComponent<MGRSurfaceSlots>(road))EntityManager.AddComponent<MGRSurfaceSlots>(road);EntityManager.SetComponentData(road,desired);changed=true;}}
            if(selection.HatchSides!=0&&(!EntityManager.HasComponent<MGRParkingHatching>(road)||EntityManager.GetComponentData<MGRParkingHatching>(road).Sides!=selection.HatchSides)){if(!EntityManager.HasComponent<MGRParkingHatching>(road))EntityManager.AddComponent<MGRParkingHatching>(road);EntityManager.SetComponentData(road,new MGRParkingHatching{Sides=selection.HatchSides});CurbLineEmissionSystem.QueueRoad(road);changed=true;}
            if(selection.Powerlines!=0 && (!EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(road)||EntityManager.GetComponentData<MGRVisualPowerlineUpgrade>(road).Sides!=selection.Powerlines))

            {

                if(!EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(road)) EntityManager.AddComponent<MGRVisualPowerlineUpgrade>(road);

                EntityManager.SetComponentData(road,new MGRVisualPowerlineUpgrade {Sides=selection.Powerlines});changed=true;

            }

            if(selection.Decals!=0 && (!EntityManager.HasComponent<MGRCurbDecalSides>(road)||EntityManager.GetComponentData<MGRCurbDecalSides>(road).Mask!=selection.Decals))

            {

                if(!EntityManager.HasComponent<MGRCurbDecalSides>(road)) EntityManager.AddComponent<MGRCurbDecalSides>(road);

                EntityManager.SetComponentData(road,new MGRCurbDecalSides {Mask=selection.Decals});changed=true;

            }

            if(selection.ParkingBans!=0 && (!EntityManager.HasComponent<MGRParkingBanSides>(road)||EntityManager.GetComponentData<MGRParkingBanSides>(road).Mask!=selection.ParkingBans))

            {

                if(!EntityManager.HasComponent<MGRParkingBanSides>(road))EntityManager.AddComponent<MGRParkingBanSides>(road);

                EntityManager.SetComponentData(road,new MGRParkingBanSides{Mask=selection.ParkingBans});changed=true;

            }

            if((selection.ParkingLeft|selection.ParkingRight)!=0)
            {
                var desired=new MGRParkingSides{Left=selection.ParkingLeft,Right=selection.ParkingRight};
                var previous=RoadSideUpgrades.Parking(EntityManager,road);
                if(previous.Left!=desired.Left||previous.Right!=desired.Right||!EntityManager.HasComponent<MGRParkingUpgrade>(road))
                {
                    if(!EntityManager.HasComponent<MGRParkingSides>(road))EntityManager.AddComponent<MGRParkingSides>(road);
                    EntityManager.SetComponentData(road,desired);
                    if(!EntityManager.HasComponent<MGRParkingUpgrade>(road))EntityManager.AddComponent<MGRParkingUpgrade>(road);
                    EntityManager.SetComponentData(road,new MGRParkingUpgrade{Marked=desired.Left>=2||desired.Right>=2});changed=true;
                }
            }
            if(selection.CenterStyle!=0&&CenterlineUpgradePolicy.Mode(EntityManager,road)!=selection.CenterStyle){if(!EntityManager.HasComponent<MGRCenterlineStyle>(road))EntityManager.AddComponent<MGRCenterlineStyle>(road);EntityManager.SetComponentData(road,new MGRCenterlineStyle{Value=selection.CenterStyle});changed=true;}
            if((selection.EdgeLeft|selection.EdgeRight)!=0){var desired=new MGREdgePaint{Left=selection.EdgeLeft,Right=selection.EdgeRight};if(!EntityManager.HasComponent<MGREdgePaint>(road)||EntityManager.GetComponentData<MGREdgePaint>(road).Left!=desired.Left||EntityManager.GetComponentData<MGREdgePaint>(road).Right!=desired.Right){if(!EntityManager.HasComponent<MGREdgePaint>(road))EntityManager.AddComponent<MGREdgePaint>(road);EntityManager.SetComponentData(road,desired);changed=true;}}
            if(selection.EdgeStyle!=0&&SectionEdgePolicy.Mode(EntityManager,road)!=selection.EdgeStyle){if(!EntityManager.HasComponent<MGRSectionEdges>(road))EntityManager.AddComponent<MGRSectionEdges>(road);EntityManager.SetComponentData(road,new MGRSectionEdges{Value=selection.EdgeStyle});changed=true;}
            if(selection.DividerStyle!=0&&LaneDividerPolicy.Mode(EntityManager,road)!=selection.DividerStyle){if(!EntityManager.HasComponent<MGRLaneDivider>(road))EntityManager.AddComponent<MGRLaneDivider>(road);EntityManager.SetComponentData(road,new MGRLaneDivider{Value=selection.DividerStyle});changed=true;}
            if(selection.LightingOwned&&!EntityManager.HasComponent<MGRStreetLightingOwnedFlag>(road))EntityManager.AddComponent<MGRStreetLightingOwnedFlag>(road);
            if(selection.StreetLighting!=0&&StreetLightingSystem.Mode(EntityManager,road)!=selection.StreetLighting){if(!EntityManager.HasComponent<MGRStreetLighting>(road))EntityManager.AddComponent<MGRStreetLighting>(road);EntityManager.SetComponentData(road,new MGRStreetLighting{Value=selection.StreetLighting});changed=true;}
            if(selection.HideCenter&&!EntityManager.HasComponent<MGRHideCenterline>(road)){EntityManager.AddComponent<MGRHideCenterline>(road);changed=true;}
            if((selection.HiddenLeft|selection.HiddenRight)!=0){var previous=EntityManager.HasComponent<RDCSideUpgrade>(road)?EntityManager.GetComponentData<RDCSideUpgrade>(road):default;if(previous.Left!=selection.HiddenLeft||previous.Right!=selection.HiddenRight||!EntityManager.HasComponent<RDCUpgrade>(road)){if(!EntityManager.HasComponent<RDCSideUpgrade>(road))EntityManager.AddComponent<RDCSideUpgrade>(road);EntityManager.SetComponentData(road,new RDCSideUpgrade{Left=selection.HiddenLeft,Right=selection.HiddenRight});if(!EntityManager.HasComponent<RDCUpgrade>(road))EntityManager.AddComponent<RDCUpgrade>(road);EntityManager.SetComponentData(road,new RDCUpgrade{Mask=(byte)(selection.HiddenLeft|selection.HiddenRight)});changed=true;}}
            if(selection.LightingPrevious!=0&&!EntityManager.HasComponent<MGRStreetLightingPrevious>(road))EntityManager.AddComponentData(road,new MGRStreetLightingPrevious{Enabled=selection.LightingPrevious==2});
            if(selection.LineVisibility!=0&&SegmentUpgradePolicy.Mode(EntityManager,road)!=selection.LineVisibility){if(!EntityManager.HasComponent<MGRSegmentLineVisibility>(road))EntityManager.AddComponent<MGRSegmentLineVisibility>(road);EntityManager.SetComponentData(road,new MGRSegmentLineVisibility{Mask=selection.LineVisibility});changed=true;}
            return changed;

        }

        internal void Forget(Entity road){foreach(var key in new List<Entity>(_commits.Keys))if(key==road||_commits[key].Original==road)_commits.Remove(key);}

        internal void CommitSelections()

        {

            if(Mod.Saving || _commits.Count==0) return;Dependency.Complete();

            foreach(var target in new List<Entity>(_commits.Keys))

            {

                var pending=_commits[target];Entity road=Entity.Null;bool created=false;

                if(EntityManager.Exists(target)&&!EntityManager.HasComponent<Temp>(target)&&!EntityManager.HasComponent<Deleted>(target)) {road=target;created=true;}

                else if((!EntityManager.Exists(target)||EntityManager.HasComponent<Deleted>(target)) && EntityManager.HasComponent<Curve>(pending.Original) && !EntityManager.HasComponent<Deleted>(pending.Original))

                {

                    var curve=EntityManager.GetComponentData<Curve>(pending.Original).m_Bezier;

                    if(math.distancesq(curve.a,pending.Curve.a)<.01f && math.distancesq(curve.d,pending.Curve.d)<.01f && math.distancesq(MathUtils.Position(curve,.5f),MathUtils.Position(pending.Curve,.5f))<.01f) road=pending.Original;

                }

                if(road!=Entity.Null)

                {

                    bool changed=ApplySelection(road,pending.Selection);

                    if(changed||created)

                    {

                        if((pending.Selection.ParkingLeft|pending.Selection.ParkingRight)!=0){FixedParkingSystem.QueueRoad(road);CurbLineEmissionSystem.QueueRoad(road);}
                        if((pending.Selection.HiddenLeft|pending.Selection.HiddenRight)!=0)DecalVisibilitySystem.QueueRoad(road);
                        if(pending.Selection.StreetLighting!=0)StreetLightingSystem.QueueRoad(road);
                        if(pending.Selection.LineVisibility!=0||pending.Selection.EdgeStyle!=0||(pending.Selection.EdgeLeft|pending.Selection.EdgeRight)!=0||pending.Selection.DividerStyle!=0||pending.Selection.CenterStyle!=0||pending.Selection.HideCenter){CurbLineEmissionSystem.QueueRoad(road);IntersectionPaintSystem.RequestRefresh();}
                        if(pending.Selection.Powerlines!=0) World.GetOrCreateSystemManaged<VisualPowerlineSystem>().QueueRoad(road);

                        if(pending.Selection.Decals!=0) World.GetOrCreateSystemManaged<CurbDecalSystem>().QueueRoad(road);

                        if(pending.Selection.ParkingBans!=0){FixedParkingSystem.QueueRoad(road);CurbLineEmissionSystem.QueueRoad(road);}

                    }

                    _commits.Remove(target);

                }

                else if(--pending.Tries==0) _commits.Remove(target);

            }

        }

        protected override void OnCreate()

        {

            base.OnCreate();

            // Deletion/replacement source previews need not carry a Road or

            // Curve component themselves. Read those from their original.

            _previews=GetEntityQuery(ComponentType.ReadOnly<Temp>(),ComponentType.Exclude<Deleted>());

            _targets=GetEntityQuery(ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Curve>(),ComponentType.ReadOnly<Temp>(),ComponentType.Exclude<Deleted>());

            RequireForUpdate(_targets);

        }

        private static byte Reverse(byte sides) => (byte)(((sides&1)<<1)|((sides&2)>>1));

        private static Selection ReverseSelection(Selection selection)
        {
            selection.HatchSides=Reverse(selection.HatchSides);
            selection.Powerlines=Reverse(selection.Powerlines);selection.Decals=Reverse(selection.Decals);selection.ParkingBans=Reverse(selection.ParkingBans);
            byte edgePaint=selection.EdgeLeft;selection.EdgeLeft=selection.EdgeRight;selection.EdgeRight=edgePaint;
            byte hidden=selection.HiddenLeft;selection.HiddenLeft=selection.HiddenRight;selection.HiddenRight=hidden;
            byte parking=selection.ParkingLeft;selection.ParkingLeft=selection.ParkingRight;selection.ParkingRight=parking;return selection;
        }

        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdatePerformanceCore();}finally{ModDiagnostics.RuntimeCosts.End("RoadAttachmentTransferSystem",started);}
        }
        private void UpdatePerformanceCore()
        {

            if(Mod.Saving || _previews.IsEmptyIgnoreFilter){_sourceSelections.Clear();return;}
            Dependency.Complete();var sources=_sourceSelections;sources.Clear();

            using(var previews=_previews.ToEntityArray(Allocator.Temp)) foreach(var preview in previews)

            {

                var source=Original(EntityManager.GetComponentData<Temp>(preview).m_Original);

                if(!EntityManager.HasComponent<Road>(source)||!EntityManager.HasComponent<Curve>(source)) continue;

                var selection=new Selection();

                if(EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(source)) selection.Powerlines=EntityManager.GetComponentData<MGRVisualPowerlineUpgrade>(source).Sides;

                if(EntityManager.HasComponent<MGRCurbDecalSides>(source)) selection.Decals=EntityManager.GetComponentData<MGRCurbDecalSides>(source).Mask;

                if(EntityManager.HasComponent<MGRParkingBanSides>(source))selection.ParkingBans=EntityManager.GetComponentData<MGRParkingBanSides>(source).Mask;

                var parking=RoadSideUpgrades.Parking(EntityManager,source);selection.ParkingLeft=parking.Left;selection.ParkingRight=parking.Right;
                if(EntityManager.HasComponent<RDCSideUpgrade>(source)){var hidden=EntityManager.GetComponentData<RDCSideUpgrade>(source);selection.HiddenLeft=hidden.Left;selection.HiddenRight=hidden.Right;}else if(EntityManager.HasComponent<RDCUpgrade>(source)){selection.HiddenLeft=selection.HiddenRight=EntityManager.GetComponentData<RDCUpgrade>(source).Mask;}
                if(EntityManager.HasComponent<MGRStreetLightingPrevious>(source))selection.LightingPrevious=EntityManager.GetComponentData<MGRStreetLightingPrevious>(source).Enabled?(byte)2:(byte)1;
                if(EntityManager.HasComponent<MGRSurfaceSlots>(source)){var surface=EntityManager.GetComponentData<MGRSurfaceSlots>(source);selection.SurfaceRoad=surface.Road;selection.SurfaceWalk=surface.Sidewalk;}
                if(EntityManager.HasComponent<MGRParkingHatching>(source))selection.HatchSides=EntityManager.GetComponentData<MGRParkingHatching>(source).Sides;
                selection.EdgeLeft=EdgeUpgradePolicy.Mode(EntityManager,source,true);selection.EdgeRight=EdgeUpgradePolicy.Mode(EntityManager,source,false);
                selection.EdgeStyle=SectionEdgePolicy.Mode(EntityManager,source);selection.DividerStyle=LaneDividerPolicy.Mode(EntityManager,source);selection.LightingOwned=EntityManager.HasComponent<MGRStreetLightingOwnedFlag>(source);
                selection.LineVisibility=SegmentUpgradePolicy.Mode(EntityManager,source);selection.CenterStyle=CenterlineUpgradePolicy.Mode(EntityManager,source);selection.StreetLighting=StreetLightingSystem.Mode(EntityManager,source);selection.HideCenter=EntityManager.HasComponent<MGRHideCenterline>(source);
                if((selection.SurfaceRoad|selection.SurfaceWalk|selection.HatchSides)!=0||selection.LineVisibility!=0||selection.EdgeStyle!=0||(selection.EdgeLeft|selection.EdgeRight)!=0||selection.DividerStyle!=0||selection.CenterStyle!=0||selection.StreetLighting!=0||selection.HideCenter||(selection.Powerlines|selection.Decals|selection.ParkingBans|selection.ParkingLeft|selection.ParkingRight|selection.HiddenLeft|selection.HiddenRight)!=0) sources[source]=selection;

            }

            if(sources.Count==0) return;

            using(var targets=_targets.ToEntityArray(Allocator.Temp)) foreach(var road in targets)

            {

                if(!EntityManager.HasComponent<Road>(road) && (!EntityManager.HasComponent<PrefabRef>(road) || !EntityManager.HasComponent<RoadData>(EntityManager.GetComponentData<PrefabRef>(road).m_Prefab))) continue;

                var temp=EntityManager.GetComponentData<Temp>(road);if((temp.m_Flags&(TempFlags.Delete|TempFlags.Cancel))!=0) continue;

                var target=EntityManager.GetComponentData<Curve>(road).m_Bezier;Selection selection=default;bool reverse=false,found=false;

                var original=Original(temp.m_Original);

                if(sources.TryGetValue(original,out selection))

                {

                    var source=EntityManager.GetComponentData<Curve>(original).m_Bezier;

                    MathUtils.Distance(source,MathUtils.Position(target,.5f),out float t);

                    reverse=math.dot(MathUtils.Tangent(source,t),MathUtils.Tangent(target,.5f))<0;found=true;

                }

                else if(temp.m_Original==Entity.Null)

                {

                    bool ambiguous=false;
                    foreach(var source in sources)
                    {
                        if(!RoadAttachmentPolicy.TryInherit(EntityManager.GetComponentData<Curve>(source.Key).m_Bezier,target,1,out byte orientation))continue;
                        var candidate=orientation==2?ReverseSelection(source.Value):source.Value;
                        if(found&&!selection.Equals(candidate)){ambiguous=true;break;}
                        selection=candidate;reverse=false;found=true;
                    }
                    if(ambiguous)continue; // Different section choices cannot be represented by one merged component.


                }

                if(!found) continue;

                if(reverse) selection=ReverseSelection(selection);

                ApplySelection(road,selection);

                _commits[road]=new Commit {Original=original,Curve=target,Selection=selection};

            }

        }

    }

}
