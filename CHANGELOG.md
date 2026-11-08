# Test204 update — November 7, 2026

## Curbside 0.5.1-test.204

This update promotes the tested Test204 build. It retains the existing line
generation and per-road customization behavior. It does not include the proposed
preview-refresh optimizations, a save-appearance redesign or an additional crash fix.

### Previews and recurring work

- Uses the supplied-build approach to cached/batched visual mutation and narrower
  preview processing, ported onto the published feature set in Test197.
- Carries customized secondary road-light appearance through native temporary
  ownership/original matching, including the missing alignment-slot path.
- Restores owned native identity/definition inputs before refresh, apply and save.
  Unchanged targets avoid unnecessary visual writes within the existing batches.
- Retains committed crossing appearance and marking visibility through the updated
  preview-inheritance paths. Native interpolation handles custom light placement.
- No new per-frame polling or timing-mod assembly dependency is added by the
  shared-support change. No numerical FPS improvement is claimed.

### Separate traffic phases, shared hardware

- Retains independent native phase controllers and their overhead heads.
- Where native lane masks prove distinct, mapped lanes on the same approach,
  those controllers share a physical support and a single approach-width arm.
- A stable hardware owner creates the pole, arm, sign and associated crossing
  attachment. Other phase actors avoid duplicate hardware and remain visible.
- Hardware owners apply first; head-only actors retain native appearance until
  the support is ready through the existing prefab-ready/node event path.
- Unknown, overlapping or shared-lane phase mappings are not guessed and retain
  the existing independent-support path. Broad traffic-mod compatibility remains
  configuration-dependent.

### Interface

Panel buttons explicitly use the game's locale-aware `var(--fontFamily)`. The
installed game defines the lowercase variable; the suggested uppercase spelling
would not resolve there. Offline character-map checks found coverage for all
characters in the Russian, Simplified Chinese and Traditional Chinese dictionaries.
The small pre-existing damaged loading-text entry is a separate text-data issue.

### Validation

- 35 existing offline suites and compiled integration checks passed on Test204.
- 288 split-phase geometry cases, 576 support-readiness exercises and 288 ordinary
  single-phase apply cases; 7,783 assertions. Ordinary apply results matched
  Test203 in the recorded fixtures.
- 150 preview cases / 520 assertions passed with the production preview bridge.
- 1,166 compiled checks retained 43 saved layouts, 58 serializers and 165
  line-emission methods, plus unchanged working preview methods.
- The maintainer accepted Test204 after live testing and reported inability to
  reproduce the reported crash. A captured session without other traffic
  controllers completed four autosaves, restoring 140 controlled props per save,
  without a native-crash banner or managed exception in that snapshot. Local
  helper modules also loaded; this was not a strict one-module benchmark.

Offline replays use explicit managed adapters and do not reproduce a native
Unity crash, rendering, GPU behavior or every playset. They are not crash-safety
or broad-compatibility certification. In-game font selection in every supported
language and a full controlled TTE/crash matrix remain incomplete.

### Known limitations and follow-up

Native crash reports remain under investigation. The supplied report labels its
Curbside build Test166 and provides no usable native stack/dump; this update is
not advertised as a confirmed fix for those crashes. A conditional fallback
prefab-restoration path still bypasses the common refresh/reset helpers; both
supplied save cycles reported zero objects using it. Hardening that path is a
separate follow-up, not a cause established for the reported crash.

Saving can briefly remove/rebuild custom visuals. Smoother save presentation,
additional preview-refresh reductions and NetLane-tool lag are unimplemented
follow-ups. Existing unusual-geometry, shared-lane, freeway-line and overlapping
visual-mod limitations are not declared resolved by this update.

The optional North American Traffic Signals asset pack is unchanged and remains
a separate download. Line generation, textures and saved upgrade layouts are retained.


---

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
