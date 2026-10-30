# Post-release investigations

These are reports and hypotheses, not confirmed causes or compatibility guarantees.

1. **Priority: highway nodes/junctions.** Extra native/custom stripes. Capture source prefab, curve, owner, visibility and replacement role; reproduce with vanilla highways and with Road Builder separately. Check two-edge transitions and merge/split nodes. Automatic-highway opt-out provides isolation while preserving explicit manual work.
2. **Priority: large-city performance.** Compare Curbside alone, optional signals, textures off and automatic markings off. Profile recurring replacement loops, material discovery, refresh debounce, managed allocations and job waits before changing scheduling.
3. **Traffic Lights Enhancement.** Reports of all signal heads lit and duplicate fixtures during custom phase editing. Inspect current version and phase/fixture ownership; test separately. Do not override another mod's phases or copy its source.
4. **Road Builder parking.** Reported 2 m parking lanes produce no bay paint while 3 m lanes work. Inspect lane classification/width and native parking flags before changing thresholds.
5. **Road Builder narrow curbs.** Paint clipping at 2 m and below. Compare curb geometry, road-space offset, line width and preview geometry.
6. **Grass and detailing.** Nested grass selector path bug corrected locally; live material matching still needs verification. Explore theme-native terrain grass as an isolated experiment. Transparent albedo on an opaque network material does not establish terrain passthrough. Test existing asset/surface replacement option before adding new scope.
7. **Upgrade scope clarity.** One-side highlight sometimes differs from application scope. Audit parking, hatching, line, lighting and decoration selection independently; preserve established whole-junction edge behavior.

Test112 preparation changes defaults only by adding false/off compatibility flags. Saved upgrade components and IDs are unchanged. No rendering, save coordinator or native retirement rewrite is part of this patch.
