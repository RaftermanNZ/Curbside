using System;
using System.Threading.Tasks;
using Colossal;
using Colossal.Serialization.Entities;
using Colossal.IO.AssetDatabase;
using Game;
using Game.SceneFlow;
using Game.UI;
using Game.UI.Menu;
using Unity.Entities;
namespace MedianGrassReplacement.Systems
{
    // Temporary diagnostic entry point. Uses the complete native save pipeline,
    // with its supported null-preview overload. Never replaces an existing save.
    internal static class DiagnosticSave
    {
        private static bool queued;
        internal static void Request(World world,Action<string> report)
        {
            if(!ModDiagnostics.TraceBuffer.Enabled){report("Enable Diagnostics first.");return;}
            if(queued||Mod.Saving){report("A save is already pending.");return;}
            if(GameManager.instance==null||GameManager.instance.gameMode!=GameMode.Game){report("Load a city before running this save test.");return;}
            queued=true;
            report("Separate save queued without a thumbnail.");
            try { TaskManager.instance.EnqueueTask("SaveLoadGame",()=>Run(world,report),1); }
            catch(Exception error){queued=false;Mod.Log.Error(error,"No-preview save could not be queued.");report("Save test could not start; see the log.");}
        }
        private static async Task Run(World world,Action<string> report)
        {
            string name="Curbside-NoPreview-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
            try
            {
                if(!world.IsCreated||Mod.Saving||GameManager.instance.gameMode!=GameMode.Game){report("Save test canceled because game state changed.");return;}
                var menu=world.GetExistingSystemManaged<MenuUISystem>();
                if(menu==null){report("Native save information is unavailable.");return;}
                var info=menu.GetSaveInfo(false);
                Mod.Log.Info("No-preview save test begin: "+name+"; all Curbside save callbacks retained.");
                bool result=await GameManager.instance.Save(name,info,AssetDatabase.user,(ScreenCaptureHelper.AsyncRequest)null);
                bool exists=AssetDatabase.user.Exists<PackageAsset>(SaveHelpers.GetAssetDataPath<Game.Assets.SaveGameMetadata>(AssetDatabase.user,name),out var package);
                Mod.Log.Info("No-preview save test returned: result="+result+" packageExists="+exists+" name="+name);
                report(exists?"Separate no-thumbnail save created: "+name:"Save test returned without a save package; see the log.");
            }
            catch(Exception error){Mod.Log.Error(error,"No-preview save test failed: "+name);report("Save test failed; see the log.");}
            finally{queued=false;if(Mod.Saving&&world!=null&&world.IsCreated&&world==World.DefaultGameObjectInjectionWorld)SaveRecoverySystem.Recover(world);}
        }
    }
}
