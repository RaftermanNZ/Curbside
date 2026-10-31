# Changelog

0.5.1-test.142 — Surface replacement hotfix

- "Apply road and sidewalk textures to assets and surfaces" is off by default for new/reset settings. Existing explicit preferences are retained.
- Restricted this optional replacement to verified vanilla Pavement Surface 01 (selected road texture) and Pavement Surface 02 (selected sidewalk texture), on building grounds and manually placed surfaces. Other surface types and custom textures are excluded.
- Corrected the lookup for native rendered surface materials; shared prefab material templates remain untouched.
- Removed temporary surface-tracing logs. Normal warnings and user-triggered diagnostics remain available.
- No changes to road-line, parking, traffic-signal or save serialization behavior in this update.

Known limitation: Pavement Surface 02 can still remain unchanged in some cases. Surface tiling is unchanged and remains a follow-up item. The surface option is opt-in.


## 0.5.1-test.132 — October 30, 2026

Changes since 0.5.1-test.122

### Performance and memory
- Bound decorative-powerline queue snapshots to the work consumed in a pass instead of copying the entire pending set. Reuse temporary work collections and buffers.
- Reduce recurring allocations in road attachment transfer, parking, street lighting, paint ownership and cleanup. Reuse change-filter arrays and avoid unnecessary work when the relevant feature is idle.
- Reuse junction eligibility results within a signal-maintenance pass and combine hidden-signal checks, retaining native traffic-phase handling and regeneration checks.
- Avoid formatting signal trace messages and looking up diagnostic prefab names when tracing is disabled or its limit is reached. Skip street-name label planning while that feature is disabled.
- Losslessly compress the four bundled PNGs. Decoded pixels, resolution, color metadata and appearance are unchanged from Test122; this reduces download/file size, not decoded GPU texture memory.
- Add optional elapsed-time spike counters, workload gauges and a user-requested environment/memory snapshot to help investigate large-city reports. These are diagnostic measurements, not an FPS benchmark.

### Road markings and targeting
- Add an opt-in “Preserve vanilla road lines — recolor only” mode covering sections, junctions and transitions. It retains native paths rather than automatic offset geometry, with manual upgrades and visibility settings retaining priority. The option is off by default.
- Improve native median/gore classification using directed traffic-lane boundaries, shared endpoints and elevation checks. Cache copied boundary/anchor data within each reconciliation pass.
- Resolve marking targets from the actual hit node before falling back to an adjoining section. Inspect road can highlight a node directly.
- Remove a redundant synthetic centerline target on one-way highways where the traffic-left edge already represents that boundary.
- Use finite, distinct native highway edge and divider pieces for editing even when the native layout does not form complete paired halves. Preserve their control points instead of falling back to a different curve.
- Stop fabricating pair-based highway end caps across unrelated taper pieces. Improve recognition of native edge pieces when applying or removing explicit markings.
- Compare a merge node and its directly connected sections, or a section and its endpoint nodes, when selecting a line. Preview, apply and removal use the selected path’s actual owner. The existing selection distance remains unchanged.

### Troubleshooting
- Add opt-in local action recording with line creation/replacement/retirement requests, observed tool and settings activity, curve coordinates, pick candidates and missed clicks.
- Allow marking an intersection and comparing its bounded local line state over time. Observed changes do not identify which mod caused them.
- Optional game-window screenshots can be recorded every 30 seconds, up to 40 images. The journal is limited to 32 MiB or 20 minutes and stops on scene changes, diagnostics being disabled, or UI-world disposal. Nothing is uploaded automatically.

### Compatibility and limitations
- Existing saved component layouts, upgrade selections and default marking mode are retained. No save-cleanup redesign or signal-model update is included.
- Highway merges and curved junctions still have unresolved alignment, duplicate/phantom-marking and selection cases. Some section taper pieces remain grouped by road side. This is not a complete freeway-marking fix.
- The opt-in native recolor mode still needs further validation, including median colors and intersections; it should not be treated as a universal correction. Existing automatic-line opt-outs and manual visibility tools remain available as workarounds.
- 21 automated validation suites passed, including 911 line identity, style and preview/apply/remove cases. Those checks do not establish universal save compatibility, full-playset compatibility or measured FPS/TPS gains. Back up saves before updating.


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
