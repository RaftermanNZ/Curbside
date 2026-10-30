using System;

using System.Diagnostics;

using Colossal.IO.AssetDatabase;

using Game;

namespace MedianGrassReplacement.Systems

{

    // Live settings still apply immediately. Only persistence waits for a quiet

    // second; no worker thread, file watcher or custom settings file is used.

    public partial class SettingsSaveSystem : GameSystemBase

    {

        private static Setting _pending;

        private static long _due;

        private static bool _saving;

        private static bool _flushAfterSave;

        private bool _registerOptions = true;

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); Mod.ResetSaveGuard(); }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            _registerOptions = true;

        }

        internal static void Request(Setting settings) { _pending = settings; _due = Stopwatch.GetTimestamp() + Stopwatch.Frequency; }

        protected override void OnUpdate()

        {

            if (_registerOptions && Mod.Settings != null)

            {

                Mod.Settings.RegisterInOptionsUI();

                _registerOptions = false;

            }

            if (_pending != null && !_saving && Stopwatch.GetTimestamp() >= _due) Flush();

        }

        internal static async void Flush()

        {

            if (_pending == null) return;

            if (_saving) { _flushAfterSave = true; return; }

            _pending = null; _saving = true;

            try { await AssetDatabase.global.SaveSpecificSetting(nameof(Setting)); }

            catch (Exception error) { Mod.Log.Warn("Settings could not be saved: " + error.Message); }

            finally

            {

                _saving = false;

                if (_flushAfterSave) { _flushAfterSave = false; Flush(); }

            }

        }

    }

}

