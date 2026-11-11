using System.Collections.Generic;
using Game.Common;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private static readonly HashSet<Entity> waitingSignalDefinitions = new HashSet<Entity>();
        private readonly HashSet<Entity> signalPresentationChecks = new HashSet<Entity>();
        private readonly List<Entity> signalPresentationChildren = new List<Entity>();
        private long signalPresentationReviews;
        internal static void WaitForSignalDefinition(Entity node) => waitingSignalDefinitions.Add(node);
        private void ReviewSignalPresentation(Entity node)
        {
            // This path repairs already-proven redundant visuals; it must not
            // release suppression while native tool previews hide originals.
            if(EntityManager.HasComponent<Game.Tools.Hidden>(node))return;
            CopySignalChildren(node,signalPresentationChildren);
            foreach(var actor in signalPresentationChildren)
                if(_moved.ContainsKey(actor)&&EntityManager.HasComponent<Game.Tools.Hidden>(actor))return;
            var roads=ReadApproaches(node);
            NodeDuplicateSignals(node,roads);
            EdgeMedianSignalsForApproaches(node,true,roads);
            RememberSignalNode(node);
            signalPresentationReviews++;
        }
    }
}
