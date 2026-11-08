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

### Signal-support changes (TTE duplicate issue remains)

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

**Known TTE issue:** the maintainer reports that protected left turns still
produce duplicate lights with Test204. The offline shared-support fixtures do
not cover that actual TTE path completely. This release does not claim to fix
those duplicates or certify TTE compatibility. Further tracing is deferred to a
separate build; the tested Test204 code is unchanged.

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
