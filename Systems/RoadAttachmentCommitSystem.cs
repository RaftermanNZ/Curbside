using Game;

namespace MedianGrassReplacement.Systems

{

    public partial class RoadAttachmentCommitSystem : GameSystemBase

    {

        protected override void OnUpdate() {World.GetOrCreateSystemManaged<RoadAttachmentTransferSystem>().CommitSelections();}

    }

}

