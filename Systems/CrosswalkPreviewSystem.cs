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
        { // Native-preview-only isolation test.
         }
    }
    public partial class CrosswalkPreviewRestoreSystem:GameSystemBase
    {
        protected override void OnUpdate(){ // Native-preview-only isolation test.
         }
    }
}
