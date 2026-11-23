using System;
using System.Collections.Generic;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        private struct OwnerProofInput { internal Entity Owner; internal OwnerInput Input; }
        private struct CertificateEntity { internal Entity Entity; internal uint Mask; }
        private readonly Dictionary<Entity, Curve> semanticCurveScratch = new Dictionary<Entity, Curve>(CertificateEntities);

        // A stamp is a possible write, not an entity-value comparison. Repair
        // only provenance classes represented by retained owner inputs.
        // Lane values, prefab values, membership projection and structural moves
        // retain the original full-validation fallback whenever not provable.
        private bool RevalidateStampOnlyInputs(EntityManager em, InputCertificate proof, uint changedMask)
        {
            semanticCurveScratch.Clear();
            certificateChunks.Clear();
            bool curvesPrepared = false;
            try
            {
            if(proof.Tracked.Count==0 || proof.SemanticInputs.Count==0) {  return false; }
            uint supported=Bit(ReadType.Node)|Bit(ReadType.Composition)|Bit(ReadType.EdgeGeometry)
                |Bit(ReadType.SubLane)|Bit(ReadType.ConnectedEdge)|Bit(ReadType.CarPaint)|Bit(ReadType.LaneRoute)|Bit(ReadType.Curve);
            if((changedMask & ~supported)!=0)
            {  return false; }
            // Reuse the bounded capture scratch for direct chunk lookup. A
            // repair must not rescan every saved chunk for every tracked lane.
            for (int i = 0; i < proof.ChunkCount; i++)
                certificateChunks.Add(proof.Chunks[i].Sequence, proof.Chunks[i]);
            foreach(var tracked in proof.Tracked)
            {
                uint mask=tracked.Mask & changedMask;if(mask==0)continue;
                if(!em.Exists(tracked.Entity)) {  return false; }
                // A type changed in a different chunk does not dirty this entity.
                var chunk=em.GetStorageInfo(tracked.Entity).Chunk;uint dirty=0;
                if (!certificateChunks.TryGetValue(chunk.SequenceNumber, out var saved)) return false;
                for(int t=0;t<certificateHandles.Length;t++)
                    if((mask & (1u<<t))!=0 && chunk.GetChangeVersion(ref certificateHandles[t])!=saved.Versions[t])dirty|=1u<<t;
                if(dirty==0)continue;
                if ((dirty & Bit(ReadType.Curve)) != 0)
                {
                    if (!curvesPrepared)
                    {
                        if (!PrepareRetainedLaneCurves(proof)) return false;
                        curvesPrepared = true;
                    }
                    if (!SameRetainedLaneCurve(em, tracked.Entity)) return false;
                    dirty &= ~Bit(ReadType.Curve);
                    if (dirty == 0) continue;
                }
                uint laneBits = dirty & (Bit(ReadType.CarPaint) | Bit(ReadType.LaneRoute));
                if (laneBits != 0 && !SameRetainedLanePaintReads(em, proof, tracked.Entity, laneBits)) return false;
                dirty &= ~(Bit(ReadType.CarPaint) | Bit(ReadType.LaneRoute));
                if (dirty == 0) continue;
                if(!FindSemanticCore(proof,tracked.Entity,out var state))
                {  return false; }
                if((dirty & Bit(ReadType.Node))!=0 && (!state.Node.Present || !em.HasComponent<Node>(tracked.Entity)
                    || !OwnerInputEquality<Node>.Same(state.Node.Data,em.GetComponentData<Node>(tracked.Entity))))
                {  return false; }
                if((dirty & Bit(ReadType.Composition))!=0 && (!state.Composition.Present || !em.HasComponent<Composition>(tracked.Entity)
                    || !OwnerInputEquality<Composition>.Same(state.Composition.Data,em.GetComponentData<Composition>(tracked.Entity))))
                {  return false; }
                if((dirty & Bit(ReadType.EdgeGeometry))!=0 && (!state.Geometry.Present || !em.HasComponent<EdgeGeometry>(tracked.Entity)
                    || !OwnerInputEquality<EdgeGeometry>.Same(state.Geometry.Data,em.GetComponentData<EdgeGeometry>(tracked.Entity))))
                {  return false; }
                if((dirty & (Bit(ReadType.SubLane)|Bit(ReadType.ConnectedEdge)))!=0)
                {
                    OwnerInput input=null;
                    foreach(var owner in proof.SemanticInputs)if(owner.Owner==tracked.Entity){input=owner.Input;break;}
                    if(input==null) {  return false; }
                    if((dirty & Bit(ReadType.ConnectedEdge))!=0 && !BufferSame(em,tracked.Entity,input.Connections))
                    {  return false; }
                    if((dirty & Bit(ReadType.SubLane))!=0)
                    {
                        // Projected snapshots may omit access-only raw members.
                        // Do not infer exact raw membership from that projection.
                        if(input.Members==null || input.MemberCount!=input.Members.Length)
                        {  return false; }
                        if(!BufferSame(em,tracked.Entity,input.Members))
                        {  return false; }
                    }
                }
            }
            return true;
            }
            finally { semanticCurveScratch.Clear(); certificateChunks.Clear(); }
        }
        private bool PrepareRetainedLaneCurves(InputCertificate proof)
        {
            // Build once per repair, not once per dirty lane. Every retained
            // occurrence must agree, including shared owner/endpoint inputs.
            foreach (var owner in proof.SemanticInputs)
            {
                var input = owner.Input;
                if (input.Lanes == null) continue;
                for (int i = 0; i < input.MemberCount; i++)
                {
                    var lane = input.Lanes[i];
                    if (!lane.Curve.Present) continue;
                    if (semanticCurveScratch.TryGetValue(lane.Entity, out var previous))
                    {
                        if (!OwnerInputEquality<Curve>.Same(previous, lane.Curve.Data)) return false;
                    }
                    else
                    {
                        if (semanticCurveScratch.Count == CertificateEntities) return false;
                        semanticCurveScratch.Add(lane.Entity, lane.Curve.Data);
                    }
                }
            }
            return true;
        }
        private bool SameRetainedLaneCurve(EntityManager em, Entity entity)
        {
            if (!semanticCurveScratch.TryGetValue(entity, out var expected)
                || !em.Exists(entity) || !em.HasComponent<Curve>(entity)) return false;
            var current = em.GetComponentData<Curve>(entity);
            return OwnerInputEquality<Curve>.Same(expected, current);
        }
        private static bool SameRetainedLanePaintReads(EntityManager em, InputCertificate proof, Entity entity, uint bits)
        {
            bool found = false;
            foreach (var owner in proof.SemanticInputs)
            {
                var input = owner.Input;
                if (input.Lanes == null) continue;
                for (int i = 0; i < input.MemberCount; i++)
                {
                    var lane = input.Lanes[i];
                    if (lane.Entity != entity) continue;
                    found = true;
                    if (!lane.SamePaintReads(em, entity, (bits & Bit(ReadType.CarPaint)) != 0, (bits & Bit(ReadType.LaneRoute)) != 0)) return false;
                }
            }
            return found;
        }
        private static bool FindSemanticCore(InputCertificate proof,Entity entity,out Core state)
        {
            foreach(var owner in proof.SemanticInputs)
            {
                if(owner.Owner==entity){state=owner.Input.State;return true;}
                var input=owner.Input;
                if(input.Connections!=null && input.ConnectedRoads!=null)
                    for(int i=0;i<input.Connections.Length;i++)
                        if(input.Connections[i].m_Edge==entity){state=input.ConnectedRoads[i];return true;}
            }
            state=default;return false;
        }
    }
}
