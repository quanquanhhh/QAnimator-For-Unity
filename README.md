# QAnimator for Unity

QAnimator is a lightweight custom 2D animation codec for Unity.

It converts `MP4/WebM -> .bytes` offline and plays the resulting binary directly in Unity without `VideoPlayer` or runtime FFmpeg.

> Full requirements: see [SPEC.md](SPEC.md).

## Current repository layout

```text
Encoder/                 Windows encoder (WinForms, .NET 8)
Unity/Runtime/           Unity runtime decoder/player source
.github/workflows/       Encoder build verification
```

## Encoder

Requirements for local use:

- Windows
- .NET 8 SDK
- `ffmpeg.exe` and `ffprobe.exe` available on PATH, or placed next to the encoder executable

Build:

```powershell
dotnet build Encoder/QAnimator.Encoder.csproj -c Release
```

The encoder UI supports MP4/WebM input, `.bytes` output, queue processing, FPS override, resizing, key-frame interval, block size, compression level, and alpha-preserving RGBA decode.

## Unity runtime

Copy `Unity/Runtime` into a Unity project, or later package it as a UPM package.

Typical usage:

```csharp
[SerializeField] private TextAsset animationData;
[SerializeField] private QAnimatorPlayer player;

private void Start()
{
    player.Load(animationData.bytes);
    player.Play();
}
```

The runtime intentionally does not reference `UnityEngine.Video.VideoPlayer`.

## Status

This repository starts with a v1 MVP codec:

- RGBA32 key frames
- 16x16-style block delta frames (configurable)
- per-frame Deflate compression
- indexed frame access
- reusable Unity texture and decode buffers

The format is versioned so compression/codecs can be upgraded later.
