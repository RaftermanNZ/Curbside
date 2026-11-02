using System;
using System.Diagnostics;
using Colossal.IO.AssetDatabase;
using Game;
namespace MedianGrassReplacement.Systems
{
    public partial class SettingsSaveSystem:GameSystemBase
    {
        private static readonly DeferredSettingsWrite<Setting> writes=new DeferredSettingsWrite<Setting>();
        private bool _registerOptions=true;
        private static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        internal static string PersistenceStatus=>"pending="+(writes.Pending!=null)+" writing="+writes.Saving+" failures="+writes.Failures;
        internal static void Request(Setting settings)=>writes.Request(settings,Now);
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){base.OnGamePreload(purpose,mode);Mod.ResetSaveGuard();}
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);_registerOptions=true;}
        protected override void OnUpdate()
        {
            if(_registerOptions&&Mod.Settings!=null){Mod.Settings.RegisterInOptionsUI();_registerOptions=false;}
            if(writes.Pending!=null&&!writes.Saving)WritePending();
        }
        // Explicit save/dispose may bypass debounce, but never wait synchronously.
        internal static void Flush(){writes.Force(Now);WritePending();}
        private static async void WritePending()
        {
            var settings=writes.Begin(Now);if(settings==null)return;
            bool success=false;
            try{await AssetDatabase.global.SaveSpecificSetting(nameof(Setting));success=true;}
            catch(Exception error){Mod.Log.Warn("Settings could not be saved; pending preferences retained for bounded retry: "+error.Message);}
            finally{writes.Complete(success,Now);}
        }
    }
}
