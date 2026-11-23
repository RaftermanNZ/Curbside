using Game;

namespace MedianGrassReplacement.Systems

{

    public partial class InvisibleCrosswalkInputSystem : GameSystemBase

    {

        protected override void OnCreate() { base.OnCreate(); Enabled = false; }
        protected override void OnUpdate()
        {
            if (Mod.Saving) return;
            try { World.GetExistingSystemManaged<CrosswalkUpgradeSystem>()?.ApplyPendingNativeMode(); }
            finally { Enabled = false; }
        }

    }

}
