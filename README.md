# Curbside

Source code for [Curbside for Cities: Skylines II](https://mods.paradoxplaza.com/mods/165355/Windows), by RaftermanNZ. Road markings, crosswalks, parking upgrades, roadside decoration and global surface textures.

**Players:** install the published mod through Paradox Mods. This repository is for source review and development, not a replacement download. The optional North American Traffic Signals models are a separate asset pack; Curbside works without them.

## Source versions

`main` and tag `source-test111` contain the released Test111 code baseline. `hotfix/test112` contains unreleased compatibility controls, grass-library fixes and sidecar texture packaging. Compilation and policy tests do not establish in-game compatibility or performance. No hotfix is published automatically.

## Build locally on Windows

1. Install Cities: Skylines II and its official modding toolchain from the game's options. Keep the toolchain-provided `CSII_TOOLPATH`, `CSII_MANAGEDPATH`, `CSII_UNITYMODPROJECTPATH` and `CSII_MODPOSTPROCESSORPATH` user environment variables.
2. Install .NET SDK 10.0.402 (see `global.json`), the .NET Framework 4.8 targeting pack and Node.js 22 or 24 LTS. SDK 10 is a developer tool; the mod still targets the game-shipped net48 runtime and C# 9.
3. In `UI`, run `npm ci --ignore-scripts`, then `npm run build`. Editable TypeScript/SCSS, ambient UI declarations and the lockfile are included. See `UI/TEMPLATE-PROVENANCE.txt` for the shipped-template baseline and deliberate deviations.
4. Run `./Build-Local.ps1` from PowerShell. It compiles and invokes the official game postprocessor/Burst tools. `BuildOutput` is a local staging folder; the script does not install or publish.
5. Bundled texture artwork is excluded here. Obtain the approved images from the maintainer for a complete production package. The hotfix branch deploys them beside the DLL in `BundledTextures`, using the same saved selection IDs and extracted filenames.
6. Before local installation, close the game and disable the subscribed core copy. Never run two Curbside copies. Use disposable copies of saves for development.

GitHub checks build the UI without proprietary game assemblies. Run local policy checks with `./Validation/Run-CurrentChecks.ps1 -Dll ./BuildOutput/MedianGrassReplacement.dll -OutputFolder ./outputs/checks`; set `CURBSIDE_CECIL_PATH` to your local Mono.Cecil assembly for metadata-only checks. Test helpers copy game dependencies into the ignored output folder; never commit those files.

Full native compilation, policy tests and in-game save/reload/performance checks run locally with the licensed game/toolchain. See [release gates](docs/RELEASE-SAFETY.md).

## Reporting issues

Include game/mod versions, the enabled playset, exact road type/width, steps to reproduce and a Curbside diagnostic snapshot. Remove personal information from logs before posting. Native crashes and FPS changes need matching evidence; similar symptoms do not establish the same cause.

## Licensing and credits

Original Curbside source code is MIT licensed; see `LICENSE`. That license does not relicense game SDKs, third-party dependencies, artwork or the separate signal pack. Original project icons retain their existing artwork rights. No proprietary game binaries or decompiled game source are included.

Thanks to WinterQT for troubleshooting help and bug-fix suggestions. RealVision curb-wear decal: StatikNoize, used with permission. Edited sidewalk: Concrete Layers 02 by Rob Tuytel / Poly Haven, CC0. Other supplied road/grass images have separate redistribution permission. See `THIRD-PARTY-NOTICES.txt`.

This spare-time project was developed with AI assistance. Back up saves; compatibility, safe removal, stability and performance cannot be guaranteed for every configuration.
