using Game;

namespace MedianGrassReplacement.Systems

{

    public partial class InvisibleCrosswalkInputSystem : GameSystemBase

    {

        protected override void OnUpdate() {World.GetOrCreateSystemManaged<NativeCrosswalkResetSystem>().CaptureCrosswalkInput();}

    }

}

