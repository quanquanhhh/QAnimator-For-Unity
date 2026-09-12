# Unity 6 integration validation

Executed in the user's open Unity Editor **6000.0.61f1**, project `E:/VideoEncode/My project`.

The local package is referenced through `Packages/manifest.json`; changes to `Unity/Runtime` and `Unity/Editor` therefore reach the project directly. The UGUI assembly reference must be `UnityEngine.UI` (the package name `com.unity.ugui` is not an assembly name).

## Installed layout

```text
Assets/QAnimatorValidation/
  QAnimatorValidation.cs
  QAnimatorValidation.unity
  Editor/QAnimatorValidationSetup.cs
  Data/treasure_chest_open.bytes
  Data/treasure_chest_open_alpha.bytes
  Data/Checker.asset
QAnimatorTestResults/
  alpha-sha256.txt
  results.json
  alpha-closed.png
  alpha-open.png
  mp4-open.png
```

The runtime test source and editor setup source in this directory are copied to the locations above. The reference hashes are SHA-256 hashes of each of the 120 RGBA frames independently decoded from the alpha WebM using the offline FFmpeg command:

```text
ffmpeg -c:v libvpx-vp9 -i treasure_chest_open_alpha.webm -vf scale=512:512:flags=lanczos,fps=30,vflip -f rawvideo -pix_fmt rgba pipe:1
```

Each frame is 1,048,576 bytes. Hashes are stored one per line, lowercase hex. The Unity test compares these against each uploaded texture's raw RGBA data; Game-view screenshots separately verify visible orientation and alpha compositing.

## Repeat in the installed project

Use **Tools → QAnimator → Run Unity validation**. A dedicated additive scene preserves the existing scene, including unsaved edits. The test enters Play mode, executes 25 checks and writes results/screenshots, then returns to Edit mode. Enter Play mode normally to use the looping interactive demo without running the tests.

Editor automation also accepts project-root `QAnimatorValidation.run` and `QAnimatorValidation.demo` marker files. They are consumed once; no server, network endpoint, or permanent automatic playback is installed.

## Coverage

- Package/runtime/editor compilation on Unity 6 with UGUI 2.0.0.
- All 120 actual texture upload buffers match the independent RGBA reference; one texture instance is reused.
- Transparency includes zero, full and partial alpha; opaque MP4 input remains opaque.
- RawImage binding, asset reload, disable/enable, SpriteRenderer binding and resource release.
- Actual Unity Update-based play, pause, resume, zero/2x speed, stop and restart.
- Scaled/unscaled time while `Time.timeScale = 0`.
- A 10 FPS game loop advances to the correct 30 FPS animation frame.
- Non-loop completion fires exactly once; looping wraps without completion.
- Unload and reload reconstruct the expected frame.

Windows standalone, Android, iOS, IL2CPP and device profiling are not covered by this Editor run.
