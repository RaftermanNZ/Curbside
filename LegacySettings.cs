using Game.Settings;

namespace MedianGrassReplacement

{

    // Accepted only to load older settings files. These keys have no active behavior.

    public sealed partial class Setting

    {

        [SettingsUIHidden] public bool VerboseLogging { get => false; set { } }

        [SettingsUIHidden] public bool ParkingBoundaryLines { get; set; }

        [SettingsUIHidden] public bool ParkingTMarkers { get; set; }

        [SettingsUIHidden] public bool ContinuousParkingTMarkers { get; set; }

        [SettingsUIHidden] public float VisualParkingSpacing { get; set; }

        [SettingsUIHidden] public bool FollowParkedCars { get; set; }

        [SettingsUIHidden] public float ParkedCarMarkerClearance { get; set; }

        [SettingsUIHidden] public bool FixedParkingBays { get; set; }

        [SettingsUIHidden] public bool TwoLineCrosswalks { get; set; }

        [SettingsUIHidden] public float CurbMarkingWidth { get; set; }

        [SettingsUIHidden] public float ParkingMarkingWidth { get; set; }

        [SettingsUIHidden] public float CrosswalkMarkingWidth { get; set; }

        [SettingsUIHidden] public bool ActivateZebraCrosswalk { set { } }

    }

}
