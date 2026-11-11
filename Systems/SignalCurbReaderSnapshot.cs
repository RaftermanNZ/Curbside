using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        // Explicit diagnostic snapshot only. No query or check is scheduled
        // during ordinary tool movement. Records why native bounds are absent.
        private void RecordSignalCurbReaderSnapshot()
        {
            int reported = 0;
            var marking = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            using (var nodes = _upgrades.ToEntityArray(Allocator.Temp)) foreach (var node in nodes)
            {
                if (!EntityManager.HasComponent<MGRSignalStyle>(node) ||
                    !GardenCityLightPolicy.IsWireStyle(EntityManager.GetComponentData<MGRSignalStyle>(node).Value) ||
                    !EntityManager.HasBuffer<ConnectedEdge>(node)) continue;
                foreach (var connection in EntityManager.GetBuffer<ConnectedEdge>(node, true))
                {
                    var road = connection.m_Edge;
                    if (!Live(road) || !EntityManager.HasComponent<Edge>(road) || !EntityManager.HasComponent<Composition>(road)) continue;
                    var edge = EntityManager.GetComponentData<Edge>(road); bool start = edge.m_Start == node;
                    if (!start && edge.m_End != node) continue;
                    var composition = EntityManager.GetComponentData<Composition>(road);
                    var native = start ? composition.m_StartNode : composition.m_EndNode;
                    bool data = EntityManager.HasComponent<NetCompositionData>(native);
                    bool lanes = EntityManager.HasBuffer<NetCompositionLane>(native), pieces = EntityManager.HasBuffer<NetCompositionPiece>(native);
                    bool geometry = start ? EntityManager.HasComponent<StartNodeGeometry>(road) : EntityManager.HasComponent<EndNodeGeometry>(road);
                    var anchors = data ? ReadSignalCurbAnchors(native, marking) : new System.Collections.Generic.List<float3>();
                    var pavement = ReadJunctionFootprint(road, start, composition, marking, out var sidewalk);
                    Mod.Log.Info("Signal curb-reader snapshot: node=" + node + " centre=" + EntityManager.GetComponentData<Node>(node).m_Position + " road=" + road + " composition=" + native +
                        " data=" + data + " geometry=" + geometry +
                        " laneBuffer=" + lanes + " laneCount=" + (lanes ? EntityManager.GetBuffer<NetCompositionLane>(native, true).Length : -1) +
                        " pieceCount=" + (pieces ? EntityManager.GetBuffer<NetCompositionPiece>(native, true).Length : -1) +
                        " anchors=" + anchors.Count + " width=" + (data ? EntityManager.GetComponentData<NetCompositionData>(native).m_Width : float.NaN) +
                        " middleRadius=" + (!geometry ? float.NaN : start ? EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry.m_MiddleRadius : EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry.m_MiddleRadius) +
                        " pavementVertices=" + (pavement?.Outline.Count ?? 0) + " sidewalkVertices=" + (sidewalk?.Outline.Count ?? 0));
                    foreach (var boundary in new[] { pavement, sidewalk })
                        if (boundary?.NativeCurves != null) for (int side = 0; side < boundary.NativeCurves.Length; side++)
                        {
                            var curve = boundary.NativeCurves[side];
                            Mod.Log.Info("Signal corner boundary snapshot: node=" + node + " road=" + road +
                                " kind=" + (ReferenceEquals(boundary, pavement) ? "curb" : "back") + " side=" + side +
                                " a=" + curve.a + " b=" + curve.b + " c=" + curve.c + " d=" + curve.d);
                        }
                    if (++reported == 64) return;
                }
            }
        }
    }
}
