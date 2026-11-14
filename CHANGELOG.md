# Curbside 0.6.2

This is an icon-packaging hotfix based directly on 0.6.1.

- Restored 57 original SVG icons, including the launcher, marking tools and placeable surface controls. Existing installations could retain extracted icons and mask the omission.
- Added build-time validation of the required artwork manifest, production references, SVG documents and exact embedded resource names and bytes. The postprocessed package is checked too, even when the UI bundle is reused.
- Verified offline installation into an empty icon folder using the actual bundled-asset installer. Repeated extraction skips unchanged files; corrupt cached artwork is repaired.
- Kept all existing gameplay methods, saved component layouts and serializers unchanged from 0.6.1. Performance prototypes are not included.

This correction addresses missing artwork. Native unlock requirements and optional traffic-signal pack discovery remain separate conditions for control availability. No signal asset pack changes are included.

---

# Curbside 0.6.1

This update brings the tested perf.20 branch to the public release. It focuses on reducing unnecessary marking, parking and texture refresh work and includes the targeted traffic-signal texture-cache correction.

## Traffic signals

- Normalize duplicate configuration tags only for Curbside's known traffic-signal mid-mip cache. The game's lookup requires one matching tag; repeated database refreshes can otherwise hide the valid cache and produce the reported TextureStreamingSystem mid-mip mismatch.
- Coalesce cache-change notifications, retain unfamiliar tags, and skip repeated refreshes for an already processed cache/streaming pair. This is a targeted correction, not a general repair for unrelated asset errors.
- Preserve current light placement, native controllers, wire layouts and working preview behavior.

## Markings, parking and previews

- Narrow road work to marking-relevant changes and reject unchanged-owner notifications before full road planning or junction refreshes.
- Avoid parking restore/reapply cycles when only unrelated network/access state changes; initialize existing saved parking and hidden-decal state after native definitions are ready.
- Reduce repeated junction checks, line preflight work, catalog waits, parking cleanup and hidden-decal scans. Keep required recovery and native-reader synchronization.
- Separate intersection-paint discovery from optional integrity audits, with bounded deferral when producer jobs are busy. The starvation fallback may still wait.
- Exclude private transient visual copies from authored marking catalogs. Restore matching parking definitions with a refresh request for the affected road; ambiguous foreign references keep the broader safe fallback.
- Correct stop-line invalidation across connected endpoints after signal changes, and keep native transverse stop declarations out of longitudinal-line hiding.

## Textures and crosswalks

- Reuse one material snapshot within a texture refresh and avoid scanning every material during cleanup when all cached textures remain selected.
- Tighten experimental asset/surface replacement to the exact native Pavement 01 (road texture) and Pavement 02 (sidewalk texture) targets. Other surface materials remain outside those targets.
- Preserve crosswalk choices through native replacement/road upgrades and reduce unnecessary refresh requests.

## Compatibility and validation

- Public version 0.6.1; serialized assembly identity remains 0.5.1.0 and asset compatibility level remains 243.
- The release-only compiled comparison must match perf.20 executable methods, saved component fields, embedded resources and SDK references. The UI bundle and Burst library are retained from perf.20.
- perf.20 passed 20 regression suites, 37 source contracts, 20 native material-fixture rounds, 20 native catalog/parking-fixture rounds and the targeted mid-mip cache checks. These are bounded offline tests, not universal gameplay or frame-time guarantees.
- No new line-generation mathematics, line output, saved-choice schema, signal placement or preview algorithm is introduced in this release promotion. The separate perf.17 planning/preview experiment and new generation optimizations are excluded.

## Known limits

- Default-marking flashes and crosswalk restoration delays can still occur around saves. The alternative save-retention design remains experimental and is not enabled here.
- Unusual road geometry, Road Builder roads and competing signal/traffic appearance owners still require in-game testing. TTE protected-turn overlap is not declared universally resolved.
- The isolated timing improvements do not establish removal of the recorded 144 ms surface callback or a universal under-10 ms CPU target.

---

# Curbside 0.6.0

This release brings the improvements developed since public 0.5.1-test.204 into a traditionally numbered release. It builds on Test239, with release identity changes and a correction for discovering the updated published signal pack.

## Tool previews and performance

- Retain existing markings during unchanged hover, including crosswalks, arrows and speed decals. Preserve per-road style and visibility choices when native temporary previews inherit from committed roads.
- Carry custom signal appearance through the actual native secondary-object path. Keep native controller and phase ownership rather than creating replacement traffic controllers.
- Use committed network changes and versioned native actor replacement to drive light maintenance. Reuse copied pole, arm and head layouts instead of repeatedly fitting the same junction when native actors change.
- Keep the bounded recovery mechanisms already tested in the candidate. This release adds no new per-frame checks or recurring systems.

## Traffic lights and junction placement

- Support the separate updated five-style signal pack: Garden City, timber wire-span, rigid mast-arm, metal wire-span and Florida horizontal signals.
- Use native curved-curb geometry for Garden City, rigid mast-arm and Florida corner plans. Preserve the 1.5 m wire-pole setback, including supported shoulder-road layouts.
- Preserve successful straight wire fits. When an angled cable cannot use straight lane projection, use its unambiguous crossings of the native curb curves to place the full lane bank inside the served road band.
- Keep protected-phase head membership tied to the full lane-bank mapping. Cache full-bank head positions for native actor replacement.
- Retain source-association, secondary-lane filtering, standing-footprint and assembly-bounds corrections from the intervening candidates.
- Recognize the first published Florida catalog as the R23 clearance layout only after complete matching-owner catalog validation. Earlier original-only packs keep their legacy sign placement.
- Move Far Side above other traffic-light styles, below Two-way Yields.

## Saves, textures and localization

- Preserve creation-time original-definition ancestry, canonical original restoration and parent/child visual lifetime protections during save preparation.
- Retain native reader fences and cancellation-order protections.
- Use baked whole-number sidewalk repeats from 1 to 10, because the native sidewalk shader does not consume the former scale metadata.
- Retain the Russian/Chinese button font correction using the game's `--fontFamily` font stack.

## Compatibility and validation

- Line-generation output and per-road choices remain unchanged from the tested baseline. No saved component schema or assembly identity was changed for this release.
- The release-only comparison protects 207 preview methods, 307 selected marking methods, 90 save-prevention methods and 58 saved component layouts. The one startup discovery predicate change is checked separately from normal update behavior.
- Captured-junction geometry replays, lane-bank and protected-phase checks, native-order adapters, cache binding checks and current regressions pass offline. These checks do not execute native gameplay, collision/rendering, the game's save writer or all third-party mods.
- A static compatibility review covered 565 repositories, seven playsets and 359 cached managed assemblies. See [the compatibility notes](docs/COMPATIBILITY.md) for the limits and overlapping ownership risks.

## Known limits

- The reviewed marking-retention profile does not match the current 1.6.4 native module identities. Existing save cleanup/restoration remains active, so markings can briefly show defaults while saving.
- Physically unsupported extreme junctions may still reject custom placement. The corner planner supports up to six approaches; this is not a promise that every road combination fits.
- TTE protected-turn interactions and other mods changing the same signal appearance or placement are not universally resolved. Avoid competing appearance owners on the same junction when diagnosing issues.
- Offline checks are not a guarantee of native crash safety, FPS gains or compatibility with every city and playset.

## Versioning

Public versions now use `major.minor.patch`: fixes advance the patch number and feature updates advance the minor number while the mod remains below 1.0. Asset compatibility uses a separate monotonic internal build level (240 here), so changing the public version does not invalidate prepared signal packs. The serialized assembly identity remains `0.5.1.0`.

---



# Test202 — local offline secondary-signal preview fix



- Match road secondary objects using their recorded native positions inside the original junction and native-role/mask bucket. Keep exact-slot matching for regular aligned objects.

- Allow a verified custom appearance fallback when a live native original is unregistered, while retaining and restoring that original before apply/save/regeneration.

- Reject foreign owners/roles, incompatible phases, nonfinite or distant positions, ambiguous ties and duplicate fallback claims within one preview owner.

- Retry pending original-owner appearances on actual source-change callbacks. Temporary inherited markers resolve back to committed junctions.

- Native rendering continues to handle positions and interpolation. No attachment removal, signal layout planning, new update system or frame-count/timer polling.

- Permanent line generation, Test201 crosswalk behavior, saved formats, UI behavior, textures and signal loading are unchanged.

- Offline only; Test201 remains installed. Runtime visuals, commit/save/load, compatibility and FPS remain unverified.



# Test201 — local offline native signal preview prototype



- Restore only missing original-light links for a unique exact slot, native role and compatible control mask on the original controlled junction.

- Let native rendering retain the committed pose for unchanged lights. Preserve native create/modify/drag flags; do not detach attachments or request repeated position repairs.

- Run before native subobject visibility, and undo owned link/style/derived pose changes before regeneration/apply/save. Preserve newer tool costs/values/flags and foreign identity writes.

- Cover position-only far-side upgrades using vanilla models as well as custom signal styles. Unidentified/ambiguous/unaligned cases retain native behavior.

- Test199 crosswalk behavior, permanent line generation, signal layout planning, controller data and save formats are unchanged.

- Offline prototype only; not installed or runtime verified. Test200 attachment freezing is held and not included.



# Test199 — local offline prototype



- Keep signal previews at committed positions and orientations.

- Read saved crosswalk appearance by original road/end; retain committed lined-crosswalk paint instead of mapping/cloning it to cursor geometry.

- Restore native crossing definition keys only for affected indexed owners, preserving native original-lane matching. Finish their appearance restoration in the same modification batch.

- Keep permanent line generation, saved component formats, UI behavior, textures and signal loading unchanged.

- Offline validation only. No measured FPS improvement, live flicker result, commit/save/load or other-mod compatibility result is claimed. Not installed or published.



# Changelog



## 0.5.1-test.166 — November 3, 2026



- Keep default textures on one-way and two-way bicycle paths, bicycle arch,

  covered and cable-stayed bridges, and both bicycle quay patterns.

- Preserve existing road bike lanes and pier/quay texture protection.



## 0.5.1-test.161 — November 2, 2026



Changes since 0.5.1-test.154.



### Road markings and individual divider editing

- Added individual selection of recognized native divider lines on road sections

  and supported non-highway junctions. Style, hide or restore an eligible line

  without automatically changing its neighboring divider lines.

- Improved click-time selection, coincident-path detection and reuse of the

  native line geometry. Refined dash/passing previews and section-divider

  clipping near approach stop ranges.

- Added saved individual-divider intent and matching during road-upgrade

  transfers. Existing grouped-divider selections and serialized component layouts

  are retained. Unsupported layouts keep the grouped-divider fallback.



### Pier and quay textures

- Prevent road and sidewalk replacement textures from overriding native pier

  materials, including concrete and timber pavement surfaces.

- Preserve the native bike-path surface on quay compositions without changing

  the shared material used by ordinary roads and paths. The correction requires

  both the affected 8 m bike-path surface and a recognized native quay wall.

- Reuse one protected material copy across matching quay batches and restore

  the original bindings during reset, disabling and unloading.

- Retain the existing parking-road atlas handling, ordinary road/sidewalk texture

  routing and the restricted experimental pavement-surface scope.



### Maintenance and diagnostics

- Focus hidden-item maintenance on changed or revealed objects and avoid

  unnecessary dependency waits and empty visibility-repair playback.

- Reuse managed work collections and unchanged visuals where supported.

- Improve restoration of temporary visual references before tool commit,

  saving and unloading, including cleanup after failed initialization.

- Improve user-triggered placement, persistence and material diagnostics.



### Known limitations

- Vanilla lights or markings can still appear in placement previews, and custom

  signals can still have preview-visibility inconsistencies. These are not fixed

  by this update. Placement-time performance remains under investigation.

- Individual editing depends on recognizable native divider paths. Not every

  junction, merge, roundabout or custom road supports independent line editing.

- Broader save/reload, road-regeneration and mod-removal coverage remains ongoing.



### Validation note

The author confirmed the pier and quay behavior in local testing. Test161 passed

32 inherited offline suites, 19,232 quay/non-quay scope assertions and 9,610

unchanged global material-selection comparisons. The reviewed session initialized

and shut down without Curbside warnings/errors, and logged restoration of all

11 quay bindings. These checks do not establish universal compatibility, measured

FPS gains or crash freedom.



## 0.5.1-test.154 — Release candidate



Changes since published 0.5.1-test.146:



### Road markings and themes

- Fresh settings and resets follow the city's marking theme. Automatic median and center markings use European or North American colors as appropriate; explicit white/yellow tool selections keep their chosen color. Existing saved preferences are retained.

- Consolidated automatic marking controls around the vanilla automatic-markings switch. This disables Curbside's automatic generation, recoloring and adjustments while preserving manual upgrades. Conflicting automatic controls are disabled while vanilla mode is selected; legacy settings remain readable.

- Replaced visible global line-shape selectors with theme choices. Single/double/dashed/passing styles remain available through the line tools.

- Corrected paint-ownership maintenance that could leave duplicate or flickering generated markings. Parking upgrades use dedicated white single paint instead of a shared edge-line asset.

- Improved manual median-line placement beside native paved shoulders, including parking-road medians. Single, double, dashed and passing choices use the same bounded pavement validation without moving the road or changing its lane layout. This final correction still requires in-game confirmation.



### Parking, crossings and textures

- Fixed the shared parking-road atlas replacement so the parking tread uses the road selection and the raised sidewalk uses the sidewalk selection. Native curb/detail strips and normal/mask UVs are preserved. The parking-atlas result has been confirmed in the author's Test153 session; wider road/configuration coverage remains ongoing.

- Reuse one shared 2K parking atlas across matching materials, with mipmaps and no retained CPU pixel copy. Preparation uses GPU copies, respects the existing texture-cache budget and is debounced during tiling-slider changes. The shared result adds about 21 MiB of GPU texture storage, not per-road storage.

- Preserve normal road/sidewalk replacement routing and keep experimental extra-surface replacement limited to the existing two vanilla pavement targets. Custom-surface ownership and original-texture restoration protections remain.

- Explicit parking upgrades are no longer blocked solely by Road Builder's automatic No Markings flag. Existing parking geometry/layout checks remain; this does not add support for every angled or perpendicular custom bay layout.

- Prioritize clicked crossing updates within the existing work budget. Native readiness retries remain; this does not guarantee all reported crossing delays are eliminated.



### Performance, saving and optional signals

- Replace full recurring paint-maintenance walks with affected-owner work and bounded rotating integrity checks. Reuse managed queues/indexes and target cleanup to changed/deleted networks. Initial loading and substantial network edits can still require larger work.

- Use the renderer's material catalog for ordinary network rediscovery where available, with bounded debounce so sustained road editing does not indefinitely postpone texture updates.

- Coalesce preference writes and retry failed writes with bounded backoff while retaining newer pending choices. City upgrade serialization formats are unchanged.

- Optional signal-pack discovery retries at world-load completion after failed boot detection. Partially loaded packs receive bounded progress-aware grace; absent packs do not trigger perpetual scans or runtime model imports.

- Improve manual diagnostic reports for saved upgrade intent, preference-write status, native material mappings and signal-readiness failure reasons. These reports remain user-triggered.



Validation and scope: 29 offline suites plus compiled integration checks passed. Test153 parking textures were accepted in the author's test; the final median-shoulder correction is not yet visually verified. No universal FPS/TPS, crash-free or compatibility guarantee is implied. Individual lane-by-lane editing, including separate bike-lane divider editing, is deferred. Signal models, save schemas and listing artwork/description/metadata are not changed by this candidate.



## 0.5.1-test.146 — Surface restoration update



- Improved restoration of surfaces previously affected by Curbside texture replacement. Identifiable leftover replacements can now recover the surface's own available original texture, including modded surfaces.

- Recovery runs automatically on texture refresh/loading, including when the optional replacement setting starts off. Missing or ambiguous originals are left intact and reported rather than replaced with a guessed texture.

- Prevented cleanup from destroying cached textures still referenced by live materials.

- Marked "Apply road and sidewalk textures to assets and surfaces" as Experimental, with updated labels in all supported languages. The option defaults off, and older settings require a fresh opt-in.

- New optional surface replacement remains limited to the two approved vanilla pavement surfaces. Global road, sidewalk and grass replacement controls remain unchanged.



Validation: 23 offline suites passed. Test sessions loaded saves and exercised texture refreshes without Curbside warnings/errors. Legacy/cross-session recovery is not fully verified; the original surface pack must be available. Pavement Surface 02 may still remain unchanged; surface tiling is unchanged. No line-planner or traffic-signal asset changes are included.



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

