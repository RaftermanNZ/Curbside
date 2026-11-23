using Game.Settings;
using Game.Tools;
using Game.UI.Widgets;
using MedianGrassReplacement.Systems;
using Unity.Entities;

namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        private bool _curbsidePreviews;
        [SettingsUIHidden]
        public bool CurbsidePreviews
        {
            get => _curbsidePreviews;
            set
            {
                if (_curbsidePreviews == value) return;
                _curbsidePreviews = value;
                if (!_runtimeEventsEnabled) return;
                ApplyAndSave();
                NotifyPreviewMode();
            }
        }

        [Colossal.Json.Exclude]
        [SettingsUISection(kMainTab, kCompatibilityGroup)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetPreviewModes))]
        public string PreviewMode
        {
            get => CurbsidePreviews ? "Curbside" : "Vanilla";
            set { if (value == "Vanilla" || value == "Curbside") CurbsidePreviews = value == "Curbside"; }
        }
        public DropdownItem<string>[] GetPreviewModes() => new[]
        {
            new DropdownItem<string> { value = "Vanilla", displayName = Localization.LocalizationService.Localized("Vanilla previews (performance)") },
            new DropdownItem<string> { value = "Curbside", displayName = Localization.LocalizationService.Localized("Curbside previews (experimental)") }
        };
        private void NotifyPreviewMode()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            world.GetExistingSystemManaged<NativeApproachStopPreviewSystem>()?.Restore();
            world.GetExistingSystemManaged<NativeSignalProjectionSystem>()?.ModeChanged();
            world.GetExistingSystemManaged<NativePaintProjectionSystem>()?.ModeChanged();
            world.GetExistingSystemManaged<NativeCrosswalkDragSystem>()?.ModeChanged();
            // Native stop signs and stop lines can outlive the temporary
            // priority flags that generated them. Rebuild the current stock
            // tool preview after releasing Curbside's cosmetic claims.
            var tools = world.GetExistingSystemManaged<ToolSystem>();
            if (!Mod.Saving && NativeSignalProjectionSystem.Supported
                && NativeSignalProjectionPolicy.AllowsTool(tools?.activeTool?.toolID))
                tools.RequireFullUpdate();
            world.GetExistingSystemManaged<CurbsidePanelSystem>()?.UpdatePreviewMode(CurbsidePreviews);
        }
    }
}
