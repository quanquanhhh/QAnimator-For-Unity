# Local verification — 2026-09-11

Environment: Windows x64, .NET SDK 9.0.308; projects target .NET 8. Test host runs on the installed newer runtime via major roll-forward. Portable application bundles .NET 8. FFmpeg 9.0.1 essentials.

## Results

- Release build: passed, no warnings or errors.
- Portable win-x64 publish: passed. The bundled executable also converted the alpha WebM to 256×256 with `--no-alpha`; a decoded frame was checked for all-255 alpha.
- Codec round trips: passed, including odd dimensions, unchanged frames and forward/backward seeks.
- Regression checks: passed for opaque-alpha conversion, single-dimension aspect ratio, incomplete-frame failure, cancellation preserving existing output, temp-file cleanup, truncated input and forged huge frame count.
- MP4: all 120 decoded RGBA frames exactly match FFmpeg reference pixels; 30 seeded random seeks match reference hashes.
- Alpha WebM: all 120 decoded RGBA frames exactly match the alpha-capable FFmpeg reference; 30 random seeks match.
- FFmpeg failure after a valid output exists: preserves that output for both fixtures.
- Desktop smoke: hidden WinForms event loop exercises actual queue conversion at 256-pixel width, attempted addition during processing, decoder preview and seek. Captured UI images inspected for layout and animation orientation.

| Fixture | Dimensions | FPS / frames | QANM bytes | Decode ms/frame | Managed bytes/frame |
|---|---|---|---|---|---|
| Treasure chest MP4 | 512×512 | 30 / 120 | 7,045,015 | 0.301 | 312 |
| Treasure chest alpha WebM | 512×512 | 30 / 120 | 4,216,767 | 0.181 | 312 |

Timing is one warmed sequential pass through the shared decoder on the .NET host. It excludes Unity texture upload, has normal run-to-run variability, and is **not a Unity/device benchmark**.

## Unity 6 Editor validation — completed

Unity **6000.0.61f1**, DX11, project `E:/VideoEncode/My project`. The local UPM package compiled successfully after correcting both assembly definition references from `Unity.ugui` to `UnityEngine.UI`.

**25 Play-mode checks passed**, including all 120 RGBA texture upload buffers against independent offline hashes, transparency and partial alpha, MP4 loading, actual Update-based playback, pause/resume, 0x/2x speed, stop/restart, scaled/unscaled time, 10 FPS frame skipping, loop/one-shot completion, RawImage and SpriteRenderer resource lifecycle, unload and reload. The same Texture2D instance was reused during the 120-frame check.

Game-view screenshots were inspected: chest orientation, checkerboard-visible transparency and opaque MP4 background are correct. A dedicated additive scene preserves the existing unsaved SampleScene.

Raw results and screenshots: `E:/VideoEncode/My project/QAnimatorTestResults/`. Reproducible test source and instructions: `Tests/UnityIntegration/`.

Still pending: standalone/IL2CPP/device builds, Android/iOS playback and Unity Profiler performance measurements. Editor validation does not establish zero GC or mobile performance.
