# Development changelog

## Test112 — unreleased
- Correct nested grass texture selections and adjacent normal/mask map resolution.
- Add opt-out controls for automatic markings, automatic highway markings, automatic native-line adjustments and surface texture replacements; all default off.
- Add a Highway markings settings tab. Preserve explicit upgrade intent and existing saved component layouts.
- Ship the same four texture PNGs beside the DLL; retain selection IDs, extracted filenames and image bytes.
- Restore owned grass material references before releasing replaced custom texture objects.
- Keep the UI layout and game-language behavior unchanged. Update the isolated UI test harness for React useMemo.

Highway root-cause fixes, FPS improvement, large-playset compatibility and runtime save/removal checks are not established by this build. Public PDX listings are unchanged.

## Source baseline
Released Test111 source is retained on main and tag source-test111. This repository omits third-party raster artwork and proprietary game assemblies.
