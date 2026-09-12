# QAnimator for Unity

**本地试用：** 双击 `启动编码器.cmd`。中文操作步骤、Unity 6 接入和宝箱素材说明见 [使用说明.md](使用说明.md)，实际验证结果见 [TEST_REPORT.md](TEST_REPORT.md)。

v0.3 adds the self-contained `QEmoGraphic` UGUI workflow and Edit Mode inspector preview. The transactional encoder and custom QANM runtime remain unchanged.


QAnimator is a lightweight custom 2D animation codec and runtime for **Unity 6 / Unity 6000**.

It converts `MP4/WebM -> QAnimator .bytes` offline, then Unity decodes that custom binary directly into a reusable `Texture2D`.

**QAnimator Runtime does not use Unity `VideoPlayer`, FFmpeg, temporary MP4/WebM files, or the OS video decoder.**

> Full product and technical requirements: [SPEC.md](SPEC.md)

## Pipeline

```text
Authoring / PC only
MP4 or WebM
    -> FFmpeg decode to RGBA
    -> QAnimator Encoder
    -> QANM .bytes

Unity runtime
QANM .bytes
    -> QAnimatorDecoder
    -> RGBA frame buffer
    -> reusable Texture2D
    -> QEmoGraphic (UGUI)
```

## Repository layout

```text
Encoder/                 Windows encoder (WinForms, .NET 8)
Tests/QAnimator.CodecSmoke/
                         codec round-trip / validator tests
Unity/Runtime/           QEmoGraphic + codec runtime
Unity/Editor/            QEmoGraphic inspector and preview
Unity/package.json       Unity 6000 UPM package manifest
.github/workflows/       build + end-to-end conversion validation
```

## Encoder

### Requirements

For source video conversion, the encoder needs `ffmpeg.exe` and `ffprobe.exe` either:

- next to `QAnimator.Encoder.exe`, or
- available on `PATH`.

FFmpeg is **only an authoring dependency**. It is never needed by the Unity runtime/player.

### Build

```powershell
dotnet build Encoder/QAnimator.Encoder.csproj -c Release
```

### GUI

Launch the encoder with no command-line arguments. The WinForms UI supports:

- drag/drop and multi-file MP4/WebM queue;
- output folder selection;
- source or overridden FPS;
- source or overridden width/height;
- key-frame interval;
- block size;
- compression level;
- alpha preservation;
- start/stop/clear controls;
- per-job and overall progress;
- conversion logs.

### CLI

The same executable also exposes a headless path used by CI:

```powershell
dotnet run --project Encoder/QAnimator.Encoder.csproj -c Release -- `
  --input input.webm `
  --output output.bytes `
  --fps 30 `
  --key-interval 15 `
  --block-size 16
```

Optional arguments:

```text
--fps N
--width N
--height N
--key-interval N
--block-size N
--compression fastest|optimal|smallest
--no-alpha
```

## QANM v1 codec

The current MVP uses:

- `RGBA32` pixels;
- full RGBA key frames;
- changed-block delta frames;
- configurable block size (default `16x16`);
- independent per-frame Deflate compression;
- a frame index with byte offsets and nearest key frame;
- seek by rebuilding from the nearest key frame.

The format is versioned so a future codec/compression implementation can be added without pretending an MP4/WebM is a QAnimator file.

## Unity 6000 integration

### Option A — copy Runtime

Copy `Unity/Runtime` into your Unity project's `Assets` folder.

### Option B — Git UPM package

In Package Manager, add the repository as a Git package with the `Unity` subfolder:

```text
https://github.com/quanquanhhh/QAnimator-For-Unity.git?path=/Unity
```

The package targets Unity `6000.0` and depends on UGUI for `QEmoGraphic`.

### Basic usage

```csharp
using QAnimator.Unity;
using UnityEngine;

public sealed class Demo : MonoBehaviour
{
    [SerializeField] private QEmoGraphic animation;

    private void Start()
    {
        animation.Loop = true;
        animation.Play();
    }
}
```

Add one `QEmoGraphic` under a Canvas and assign its **Animation Bytes** field. It displays the first frame in Edit Mode and can auto-play on entering Play Mode. No `RawImage`, `QAnimatorPlayer`, or output adapter is required.

`QEmoGraphic.Texture` is the reusable playback texture. The component renders it directly through `CanvasRenderer`, including UGUI tint and mask support.

QANM v1 stores one animation per `.bytes` file. Its animation name is the Unity `TextAsset` name, so `animation.Play("happy")` selects an assigned `happy.bytes` asset. Assign extra clips under **Additional Animations**; a multi-animation container format is intentionally deferred.

The older `QAnimatorPlayer` output adapters remain available for compatibility and non-UGUI use, but they are no longer the primary workflow.

### Runtime controls

```csharp
animation.Play();
animation.Play("happy");
animation.Pause();
animation.Resume();
animation.Stop();
animation.Restart();
animation.Seek(0.5f);
animation.Speed = 1.0f;
animation.Loop = true;
```

Completion callback:

```csharp
animation.OnComplete += HandleComplete;
```

## Automated verification

GitHub Actions validates more than compilation. CI currently performs:

1. Encoder build.
2. Pure RGBA codec round-trip tests, including odd dimensions and unchanged frames.
3. Forward and backward seek reconstruction tests.
4. Synthetic MP4 generation with FFmpeg.
5. Real MP4 -> QAnimator `.bytes` conversion through the encoder CLI.
6. Full decode validation of the generated `.bytes`.
7. Transparent VP9 WebM conversion and alpha/pixel preservation checks.
8. Self-contained Windows x64 publish and artifact upload.

## Current limitations

This is the v0.2 preview, not a general movie codec. In particular:

- source fractional FPS is currently rounded to an integer output FPS unless explicitly overridden;
- frame decompression currently uses managed `DeflateStream` and therefore is not yet a zero-allocation decoder;
- Unity 6000.0.61f1 Editor validation now passes 25 checks; standalone/device/IL2CPP validation remains pending;
- FFmpeg binaries are not committed to this repository.

The next optimization target after functional Unity validation is runtime decode allocation/CPU profiling on real short 2D animations.
