using System;
using System.Collections.Generic;
using Colossal.Rendering;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;
using UnityTime=UnityEngine.Time;

namespace MedianGrassReplacement.Systems
{
    internal static class QuayBikeMaterialPolicy
    {
        internal static bool QuayPiece(string name)=>name=="Quay01Side100cm Mesh"
            ||name=="Quay02Side50cm Mesh"||name=="Quay03Side80cm Mesh";
        internal static bool BikeSurface(string name)=>name=="BikePathPavement800cm"
            ||name=="BikePathPavement800cm_9abfd1a7cc766d58dea7d215fcfb149c";
        internal static bool Protect(bool quayPiece,string surface)=>quayPiece&&BikeSurface(surface);
    }

    // A quay and an ordinary pedestrian/bike path share this surface. Keep the
    // global material unchanged; use the engine's material registration API only
    // on a composition containing an independently verified native quay wall.
    public partial class QuayBikeMaterialSystem:GameSystemBase
    {
        private sealed class Binding {internal Material Original,Applied;}
        private readonly Dictionary<ManagedBatch,Binding> bindings=new Dictionary<ManagedBatch,Binding>();
        private readonly Dictionary<Material,Material> copies=new Dictionary<Material,Material>();
        private readonly HashSet<ManagedBatch> seen=new HashSet<ManagedBatch>();
        private readonly List<ManagedBatch> stale=new List<ManagedBatch>();
        private bool active,failed,pending=true;
        private int groups=-1,batches=-1;
        private float quietUntil,deadline;
        internal string Status{get;private set;}="Quay bike isolation: awaiting render data.";
        internal void RequestRefresh(){if(!pending)deadline=UnityTime.realtimeSinceStartup+1f;pending=true;quietUntil=UnityTime.realtimeSinceStartup+.15f;}
        protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
        {base.OnGameLoadingComplete(purpose,mode);active=(mode&GameMode.GameOrEditor)!=GameMode.None;pending=true;deadline=0;}
        protected override void OnUpdate()
        {
            if(!active||failed||Mod.Saving)return;
            var surfaces=World.GetExistingSystemManaged<GlobalSurfaceMaterialSystem>();
            if(bindings.Count==0&&(surfaces==null||!surfaces.HasQuayBikePatch()))return;
            var renderer=World.GetExistingSystemManaged<ManagedBatchSystem>();
            if(renderer==null)return;
            // Counters only while idle: no native batch accessor or job wait.
            if(groups!=renderer.groupCount||batches!=renderer.batchCount)
            {groups=renderer.groupCount;batches=renderer.batchCount;RequestRefresh();}
            if(!pending)foreach(var pair in bindings)if(pair.Key.material==pair.Value.Original)
            {RequestRefresh();break;}
            if(!pending||UnityTime.realtimeSinceStartup<quietUntil&&UnityTime.realtimeSinceStartup<deadline)return;
            pending=false;
            try{Reconcile(false);}
            catch(Exception e){failed=true;Mod.Log.Error(e,"Quay bike material isolation paused; native batches/materials are retained for safe cleanup.");}
        }
        private bool HasQuayWall(Entity mesh,PrefabSystem prefabs)
        {
            if(!EntityManager.HasComponent<NetCompositionMeshData>(mesh)||!EntityManager.HasBuffer<NetCompositionPiece>(mesh))return false;
            var pieces=EntityManager.GetBuffer<NetCompositionPiece>(mesh,true);
            for(int i=0;i<pieces.Length;i++)
                if(prefabs.TryGetPrefab<RenderPrefab>(pieces[i].m_Piece,out var piece)
                    &&piece!=null&&QuayBikeMaterialPolicy.QuayPiece(piece.name))return true;
            return false;
        }
        private void Reconcile(bool restoreOnly)
        {
            var manager=World.GetExistingSystemManaged<BatchManagerSystem>();
            var prefabs=World.GetExistingSystemManaged<PrefabSystem>();
            if(manager==null||prefabs==null)
            {if(bindings.Count>0)Mod.Log.Warn("Quay material registrations unavailable; retaining owned material references.");return;}
            var surfaces=World.GetExistingSystemManaged<GlobalSurfaceMaterialSystem>();
            Dependency.Complete();
            // Borrowed accessors stay local. Complete exactly the dependencies
            // returned for this public writer API before reading or rebinding.
            var native=manager.GetNativeBatchGroups(false,out var dependencies);
            dependencies.Complete();
            var managed=manager.GetManagedBatches();
            seen.Clear();int applied=0,restored=0,quayGroups=0,bikeBatches=0;
            for(int group=0;group<native.GetGroupCount();group++)
            {
                if(!native.IsValidGroup(group))continue;
                var data=native.GetGroupData(group);
                bool quay=!restoreOnly&&HasQuayWall(data.m_Mesh,prefabs);
                if(quay)quayGroups++;
                for(int part=0;part<native.GetBatchCount(group);part++)
                {
                    int index=native.GetManagedBatchIndex(group,part);
                    if(index<0)continue;
                    var batch=managed.GetBatch(index) as CustomBatch;
                    if(batch==null)continue;
                    bool tracked=bindings.TryGetValue(batch,out var binding);
                    if(tracked)seen.Add(batch);
                    // Unknown/third-party surfaces and all non-quay groups fail
                    // closed. Never change the ordinary source material itself.
                    bool protect=!restoreOnly&&quay&&batch.sourceSurface!=null
                        &&QuayBikeMaterialPolicy.Protect(quay,batch.sourceSurface.name);
                    if(protect)bikeBatches++;
                    var original=tracked?binding.Original:batch.material;
                    protect=protect&&surfaces!=null&&surfaces.HasOwnedSidewalkPatch(original);
                    if(!protect)
                    {
                        if(tracked)
                        {
                            if(batch.material==binding.Applied&&binding.Original!=null)
                            {managed.SetMaterial(index,binding.Original,native);restored++;}
                            bindings.Remove(batch);
                        }
                        continue;
                    }
                    if(tracked)
                    {
                        // The engine may restore its loaded material when shader
                        // availability changes. Preserve other third-party binds.
                        if(batch.material==binding.Original)
                        {managed.SetMaterial(index,binding.Applied,native);applied++;}
                        continue;
                    }
                    if(original==null)continue;
                    if(!copies.TryGetValue(original,out var copy)||copy==null)
                    {
                        if(copies.Count>=64)continue; // Bound shader/material variants.
                        copy=surfaces.CreateQuayNativeCopy(original);
                        if(copy==null)continue;
                        copies[original]=copy;
                    }
                    // Record before the engine call, so partial failures retain
                    // ownership for restoration. No entity/prefab is cloned.
                    binding=new Binding{Original=original,Applied=copy};bindings[batch]=binding;seen.Add(batch);
                    managed.SetMaterial(index,copy,native);
                    if(batch.material==copy)applied++;else bindings.Remove(batch);
                }
            }
            stale.Clear();foreach(var pair in bindings)if(!seen.Contains(pair.Key))stale.Add(pair.Key);
            foreach(var batch in stale)bindings.Remove(batch);
            // Scan current managed registrations before releasing owned copies.
            // This also covers batches reused/merged by the native renderer.
            var retained=new HashSet<Material>();
            for(int group=0;group<native.GetGroupCount();group++)if(native.IsValidGroup(group))
                for(int part=0;part<native.GetBatchCount(group);part++)
                {int index=native.GetManagedBatchIndex(group,part);if(index>=0){var batch=managed.GetBatch(index);if(batch!=null&&batch.material!=null)retained.Add(batch.material);}}
            var released=new List<Material>();
            foreach(var pair in copies)if(!retained.Contains(pair.Value))
            {if(pair.Value!=null)UnityEngine.Object.Destroy(pair.Value);released.Add(pair.Key);}
            foreach(var original in released)copies.Remove(original);
            string status="Quay bike isolation: quayGroups="+quayGroups+" bikeBatches="+bikeBatches+" active="+bindings.Count+" copies="+copies.Count;
            if(status!=Status||applied>0||restored>0)Mod.Log.Info(status+" applied="+applied+" restored="+restored+". Non-quay batches unchanged.");
            Status=status;
        }
        internal void Restore(){Reconcile(true);pending=true;groups=batches=-1;}
        protected override void OnGamePreload(Purpose purpose,GameMode mode)
        {Restore();active=false;failed=false;base.OnGamePreload(purpose,mode);}
        protected override void OnDestroy()
        {
            try{Restore();}catch(Exception e){Mod.Log.Warn("Quay material cleanup retained owned materials because native deregistration could not be confirmed: "+e.Message);}
            base.OnDestroy();
        }
    }
}
