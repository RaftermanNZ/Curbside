using Colossal.Mathematics;
using Game;using Game.Common;using Game.Net;using Game.Prefabs;using Game.Tools;
using Unity.Collections;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    // Render-only hover feedback. Native NetTool still owns all validity and input.
    public partial class CrosswalkPreviewSystem:GameSystemBase
    {
        private EntityQuery temporary;private Entity lastPreview;private readonly ExternalPaintOwnership external=new ExternalPaintOwnership();
        protected override void OnCreate()
        {
            base.OnCreate();temporary=GetEntityQuery(ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<Game.Net.LaneGeometry>(),ComponentType.ReadOnly<Game.Net.PedestrianLane>(),ComponentType.ReadOnly<Curve>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Deleted>());
        }
        protected override void OnUpdate()
        {
            var visual=World.GetOrCreateSystemManaged<CrosswalkVisualSystem>();
            Dependency.Complete();visual.RestorePreviewReferences();World.GetOrCreateSystemManaged<IntersectionPaintSystem>().RestorePreviewPaint();
            if(Mod.Saving||Mod.Settings==null)return;
            var tools=World.GetOrCreateSystemManaged<ToolSystem>();
            var choices=World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
            if(!(tools.activeTool is NetToolSystem)||tools.activeTool.GetPrefab()?.name!="Crosswalk"||(!choices.NativeLineMode&&!choices.NativeInvisibleMode)||Game.Input.InputManager.instance.mouseOverUI||Game.Input.InputManager.instance.hasInputFieldFocus){lastPreview=Entity.Null;return;}
            if(!World.GetOrCreateSystemManaged<NativeCrosswalkResetSystem>().TryPreviewTarget(out var road,out var node,out var source,out var point,out var axis))return;
            var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();float best=8f;
            // Only native temporary crossing geometry is queried; no city-wide lane scan.
            using(var lanes=temporary.ToEntityArray(Allocator.Temp))foreach(var lane in lanes)
            {
                var temp=EntityManager.GetComponentData<Temp>(lane);
                if(external.Contains(EntityManager,lane))continue;
                if((temp.m_Flags&(TempFlags.Delete|TempFlags.Cancel|TempFlags.Hidden))!=0)continue;
                var prefab=visual.Original(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab);
                if(!prefabs.TryGetPrefab<NetLaneGeometryPrefab>(prefab,out var asset)||asset==null||!CrosswalkPaintPolicy.IsPaint(true,asset.name))continue;
                var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                if(math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(curve,.5f).xz),axis))<.7f)continue;
                float distance=MathUtils.Distance(curve,point,out _);
                if(distance<best){best=distance;source=lane;}
            }
            if(source!=Entity.Null&&external.Contains(EntityManager,source))return;
            if(source==Entity.Null||!EntityManager.HasComponent<Game.Net.PedestrianLane>(source))return;
            if((EntityManager.GetComponentData<Game.Net.PedestrianLane>(source).m_Flags&PedestrianLaneFlags.Crosswalk)==0)return;
            var original=visual.Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab);
            if(!EntityManager.HasComponent<NetLaneGeometryData>(original))return;
            var path=EntityManager.GetComponentData<Curve>(source).m_Bezier;
            float nativeWidth=EntityManager.GetComponentData<NetLaneGeometryData>(original).m_Size.x;
            if(!World.GetOrCreateSystemManaged<IntersectionPaintSystem>().TryPreviewLines(road,node,path,nativeWidth,out var first,out var second,out float width))return;
            var overlay=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var pending);pending.Complete();
            // Neutral white paint guides do not replace native valid/invalid highlighting.
            if(choices.NativeLineMode){overlay.DrawCurve(new UnityEngine.Color(1,1,1,.9f),first,width);overlay.DrawCurve(new UnityEngine.Color(1,1,1,.9f),second,width);}
            else foreach(var guide in new[]{first,second}){overlay.DrawCurve(new UnityEngine.Color(.3f,.8f,1,.8f),MathUtils.Cut(guide,new float2(0,.12f)),.08f);overlay.DrawCurve(new UnityEngine.Color(.3f,.8f,1,.8f),MathUtils.Cut(guide,new float2(.88f,1)),.08f);}
            if(choices.NativeInvisibleMode)World.GetOrCreateSystemManaged<IntersectionPaintSystem>().HidePreviewPaint(source);
            if(!EntityManager.HasComponent<MGRReplacedPaint>(source))visual.SuppressPreview(source);
            if(lastPreview!=source&&ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("crosswalk.preview",source+" road="+road+" node="+node);
            lastPreview=source;
        }
    }
    public partial class CrosswalkPreviewRestoreSystem:GameSystemBase
    {
        protected override void OnUpdate(){World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestorePreviewReferences();World.GetOrCreateSystemManaged<IntersectionPaintSystem>().RestorePreviewPaint();}
    }
}
