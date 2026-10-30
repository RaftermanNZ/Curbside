# Release and compatibility gates

Released player cities take priority over new features. Preserve serialized component names, field layouts, enum values, prefab identifiers and saved upgrade intent. New compatibility switches default off and must not remove manual upgrades. Do not downgrade a player save to test compatibility.

## Required review

- Identify ownership of native containers, imported assets, geometry, lights and rendering allocations. Never free game-owned allocations or patch private culling/allocator records.
- Complete the relevant scheduled readers and writers before structural changes; disabling a system does not finish its jobs. Avoid global synchronization every frame.
- Do not retain borrowed buffers, arrays, lookups or pointers over structural mutations, asynchronous boundaries or disposed worlds.
- Check shutdown, failed import, save/load, autosave, cancellation, duplicate callbacks and road regeneration. Restore temporary native state to its previous value.
- Test explicit upgrades and hide/restore on sections, junctions, transitions, highways, roundabouts and road ends. Check one-side previews against actual placement.
- Measure frame time and allocations on comparable small/large cities, with identical graphics and playsets. Report mean and worst frame costs, memory and texture cache behavior. Do not infer FPS gains from file size alone.
- Run repeated manual saves and autosaves, reload, absent optional pack, core disabled and asset-pack disabled tests on disposable copies. Offline tests and one successful save are not proof.
- Inspect actual installed versions before assessing overlapping mods. Read other implementations only for interoperability and diagnostics; do not copy their code.

## Publishing boundary

No automated Paradox Mods upload. Future authorized updates may change versions and changelogs only. Preserve description, screenshots, thumbnail, dependencies and other listing metadata unless explicitly instructed otherwise. Signal models remain a separate download.

## Evidence and documentation

Use the shipped game mod/UI toolchain and current official guidance. SDK 10 is a developer tool; players use the runtime shipped with the game. Curbside retains net48/C#9 and source-generated/postprocessed game assemblies.

References: [official code-modding workflow](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-3-code-modding), [Unity ECS structural changes](https://docs.unity3d.com/Packages/com.unity.entities@1.0/manual/concepts-structural-changes.html), [Unity query filters](https://docs.unity3d.com/Packages/com.unity.entities@1.0/manual/systems-entityquery-filters.html), [GitHub workflow security](https://docs.github.com/en/actions/reference/security/secure-use).

