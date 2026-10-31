using System.Collections.Generic;
using Game.Common;using Game.Net;using Game.Prefabs;using Game.Tools;using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        private readonly HashSet<Entity> _recordingOwners=new HashSet<Entity>();
        private readonly Dictionary<Entity,string> _recordedLines=new Dictionary<Entity,string>();
        private readonly Dictionary<Entity,string> _recordingNext=new Dictionary<Entity,string>();
        internal void ResetLineRecording(){_recordingOwners.Clear();_recordedLines.Clear();_recordingNext.Clear();}
        internal void MarkRecordingTarget()
        {
            if(!ModDiagnostics.ActionJournal.Active)return;
            var owner=_diagnosticPaintTarget;
            if(!EntityManager.Exists(owner)){ModDiagnostics.ActionJournal.Record("mark.missing","Hover a road using a Curbside line or Inspect road tool first.");return;}
            ModDiagnostics.ActionJournal.Record("intersection.scopeChanged","Previous watched intersection closed; next rows establish a fresh baseline, not newly created entities.");
            _recordingOwners.Clear();_recordedLines.Clear();_recordingNext.Clear();
            void Add(Entity e){if(_recordingOwners.Count<16&&EntityManager.Exists(e))_recordingOwners.Add(e);}
            Add(owner);
            if(EntityManager.HasBuffer<ConnectedEdge>(owner))foreach(var item in EntityManager.GetBuffer<ConnectedEdge>(owner,true))Add(item.m_Edge);
            if(EntityManager.HasComponent<Edge>(owner)){var edge=EntityManager.GetComponentData<Edge>(owner);Add(edge.m_Start);Add(edge.m_End);}
            ModDiagnostics.ActionJournal.Record("intersection.mark","target="+owner+" watchedOwners="+_recordingOwners.Count+" maximum=16");
            RecordPaintSnapshot();SampleRecordedLines(true);
        }
        internal void SampleRecordedLines(bool mark=false)
        {
            if(!ModDiagnostics.ActionJournal.Active||Mod.Saving)return;
            Dependency.Complete();_recordingNext.Clear();
            void Read(Entity lane)
            {
                if(_recordingNext.Count>=4096||!EntityManager.Exists(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane))return;
                var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(prefab,out var definition)||!NodeLinePolicy.IsLineGeometry(definition))return;
                var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                string row="entity="+lane+" prefab="+prefab+" name="+definition.name+" owner="+(EntityManager.HasComponent<Owner>(lane)?EntityManager.GetComponentData<Owner>(lane).m_Owner:Entity.Null)+" deleted="+EntityManager.HasComponent<Deleted>(lane)+" hidden="+EntityManager.HasComponent<Hidden>(lane)+" replaced="+EntityManager.HasComponent<MGRReplacedPaint>(lane)+" curbOwned="+EntityManager.HasComponent<MGRCurbMarking>(lane)+" replacementOwned="+EntityManager.HasComponent<MGRIntersectionPaint>(lane)+" a="+curve.a+" b="+curve.b+" c="+curve.c+" d="+curve.d;
                _recordingNext[lane]=row;
            }
            foreach(var owner in _recordingOwners){
                if(!EntityManager.Exists(owner))continue;
                if(EntityManager.HasBuffer<Game.Net.SubLane>(owner))foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))Read(entry.m_SubLane);
                if(_marks.TryGetValue(owner,out var marks))foreach(var e in marks.Entities)Read(e);
                if(_joins.TryGetValue(owner,out var joins))foreach(var e in joins.Entities)Read(e);
                World.GetOrCreateSystemManaged<IntersectionPaintSystem>().ReadRecordingReplacements(owner,Read);
            }
            foreach(var pair in _recordingNext)if(mark||!_recordedLines.TryGetValue(pair.Key,out var old)||old!=pair.Value)ModDiagnostics.ActionJournal.Record(mark?"line.markState":_recordedLines.ContainsKey(pair.Key)?"line.observedChanged":"line.firstObserved",pair.Value);
            foreach(var pair in _recordedLines)if(!_recordingNext.ContainsKey(pair.Key))ModDiagnostics.ActionJournal.Record("line.noLongerObserved","entity="+pair.Key+" exists="+EntityManager.Exists(pair.Key)+"; not proof of creator/destructor; previous="+pair.Value);
            _recordedLines.Clear();foreach(var pair in _recordingNext)_recordedLines.Add(pair.Key,pair.Value);
        }
    }
}
