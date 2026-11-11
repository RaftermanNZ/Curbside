# Release safety and evidence

Build with the installed game's official toolchain and licensed game references. Use an absolute Build-Local.ps1 output path. Do not publish proprietary game assemblies, authentication files, generated caches or unlicensed artwork.

The current release protects line generation, previews, saved component layouts and original-definition lifetime code against the tested candidate. Captured geometry and policy adapters cover bounded cases; they do not replace native save/load, rendering, physics, timing and playset testing.

Keep the public semantic version independent of the internal asset compatibility build. Preserve serialized assembly/type identity unless a separately validated migration is intended. Optional native signal models are published separately at existing listing 165366; the code listing is 165355.

Publication should preserve existing listing metadata and artwork, then verify the downloaded package against the reviewed file hashes. Create the GitHub release as a draft, attach verified files, and publish only after the source tag matches the release source.

References: [GitHub release guidance](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository), [semantic versioning](https://semver.org/), and the installed Paradox ModPublisher/PDX SDK for the actual upload behavior.
