# Changelog

## 0.5.1-test.122 — October 30, 2026

- Add controls to disable automatic road lines, automatic highway lines, automatic line adjustments, or surface texture replacements while retaining saved selections and manual upgrades. Highway controls have their own settings tab.
- Improve individual highway-node marking selection and use native highway paint geometry where supported for automatic and manual section markings. Reduce overlapping center/edge targets on one-way highways and improve diagnostic details for manual selections.
- Fix custom grass-file selection and bundled road-wear texture loading. Package texture PNGs alongside the mod DLL and improve shared material ownership and restoration.
- Replace the included grass with the new terrain-tone texture, optimized to 1024 x 1024. Its estimated pixel memory is 75% lower than the previous 2K grass; no measured FPS/TPS improvement is claimed.
- Improve one-side road-wear placement and remove the city-wide road-wear decal limit and its setting. Per-road and per-update safety bounds remain.
- Improve signal coverage and native signal-group reconciliation for traffic-controller compatibility; add bounded optional-pack discovery and clearer diagnostics. The separate signal asset pack is unchanged.
- Reduce repeated allocations and bound refresh work; correct About/version reporting, settings reset, and input validation.
- Preserve existing saved component layouts and upgrade selections.

Known issue: freeway merge, node and junction markings still have significant alignment/duplicate-line problems in some layouts. This update does not resolve every freeway case; further freeway-line work is the next priority. Automatic-line opt-out controls and manual visibility/marking upgrades are available as workarounds.

Validation: all 16 current automated check programs passed, including 415 line-policy cases and 42 unchanged existing persisted layouts. Large-city FPS/TPS gains, full-playset compatibility, and every road layout have not been established by these checks. Back up saves before updating.
