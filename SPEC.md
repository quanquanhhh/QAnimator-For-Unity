# QAnimator for Unity — Product & Technical Specification

## 1. Goal

QAnimator is a lightweight 2D animation codec and Unity runtime player inspired by the workflow of tools such as Leviathan Video Encoder.

The system has exactly two main parts:

1. **Windows Encoder EXE** — converts `.webm` / `.mp4` into QAnimator's own binary animation file (`.bytes`).
2. **Unity Runtime Player** — reads the custom binary file directly, decodes frames, updates a reusable `Texture2D`, and displays the animation.

The project is **not** a wrapper around Unity `VideoPlayer`.

## 2. Non-negotiable constraints

The Unity runtime must **NOT**:

- use `UnityEngine.Video.VideoPlayer`;
- reconstruct a temporary `.mp4` / `.webm` and play it;
- call FFmpeg at runtime;
- depend on the operating system's video decoder;
- treat `.bytes` as a renamed video file;
- allocate a new `Texture2D` every frame;
- decode all frames into separate textures at load time.

The correct runtime pipeline is:

```text
QAnimator .bytes
    -> custom binary reader
    -> custom frame decoder
    -> RGBA frame buffer
    -> reusable Texture2D
    -> RawImage / Material / optional SpriteRenderer wrapper
```

The correct authoring pipeline is:

```text
MP4 / WebM
    -> Windows Encoder EXE
    -> FFmpeg used OFFLINE only to decode source video
    -> QAnimator custom encoder
    -> .bytes
```

After conversion, the original MP4/WebM may be deleted. Unity only needs the `.bytes` file.

## 3. Primary use cases

QAnimator targets short 2D game animations rather than movie playback:

- UI animation;
- character animation;
- loading animation;
- win / fail animation;
- reward / event animation;
- cartoon FX;
- transparent WebM character animation.

Typical duration: roughly 1–10 seconds.

## 4. Alpha channel

Alpha transparency is a first-class requirement.

If the source contains an alpha channel (for example an alpha-enabled WebM), the encoder must preserve it through the full pipeline:

```text
WebM RGBA -> QAnimator .bytes -> Unity RGBA Texture2D
```

No black or green background should be introduced by the QAnimator format.

## 5. QAnimator binary format — v1

The v1 format is intentionally simple, seekable, versioned, and suitable for later optimization.

### Header

Required fields:

- magic (`QANM`);
- format version;
- width;
- height;
- FPS numerator / denominator;
- frame count;
- duration;
- pixel format (`RGBA32` initially);
- alpha flag;
- codec type;
- compression type;
- key-frame interval;
- frame-index offset;
- frame-data offset.

### Frame index

Every frame must have an index entry so the runtime can jump to its payload without scanning from the start of the file.

Each frame entry contains at least:

- frame type (`KeyFrame` or `DeltaFrame`);
- payload offset;
- compressed payload length;
- uncompressed payload length;
- timestamp / frame time as required;
- nearest key-frame index (or enough information to derive it).

### Frame payload

Version 1 codec:

- key frame: full RGBA frame;
- delta frame: changed pixel blocks relative to the previous decoded frame;
- each payload compressed independently.

Suggested initial block size: `16x16` pixels.

A delta frame stores only changed blocks. Unchanged blocks are skipped.

This is **not** an attempt to reimplement H.264 / VP9 / AV1. It is a custom lightweight lossless animation codec optimized for short 2D game animation and alpha transparency.

## 6. Compression

The first implementation may use a broadly portable built-in compressor (for example Deflate) to validate the full pipeline.

The file format must reserve a compression enum so later versions can add faster codecs such as LZ4/Zstd without breaking format compatibility.

Compression must happen per frame payload, not as one monolithic stream, so frame seeking remains possible.

## 7. Encoder EXE

### Platform

- Windows desktop application;
- initial implementation: C# / .NET WinForms is acceptable;
- FFmpeg/ffprobe may be used by the encoder only.

### UI reference

The visual layout should follow the provided Leviathan Video Encoder screenshot as a structural reference, but only Unity-related functionality is needed.

Main UI:

- dark theme;
- task list at the top;
- add files;
- remove selected tasks;
- clear finished;
- clear all;
- start all tasks;
- stop all tasks;
- progress per task and overall progress;
- output folder;
- output extension `.bytes`;
- video/animation settings;
- advanced settings;
- log viewer.

No Creator / Unreal / Laya presets are required. Only **Unity** output is supported.

### Input

- `.webm`;
- `.mp4`.

### Encoder settings — MVP

- output folder;
- output extension (`.bytes`);
- output FPS (optional override);
- optional resize width/height or scale;
- key-frame interval;
- block size (initially 16 is fine);
- compression level;
- preserve alpha;
- max bitrate / CRF are source-video concepts and are not required for the custom QAnimator codec unless used only while preprocessing through FFmpeg.

### Offline conversion pipeline

```text
Input MP4/WebM
    -> ffprobe metadata
    -> FFmpeg decode to RGBA frames
    -> frame-by-frame QAnimator encoder
    -> keyframe / delta-frame generation
    -> per-frame compression
    -> frame index
    -> .bytes
```

The encoder should stream/process frames where possible instead of loading an entire long video into memory.

## 8. Unity runtime API

The basic usage should be simple:

```csharp
[SerializeField] private TextAsset animationBytes;
[SerializeField] private QAnimatorPlayer player;

void Start()
{
    player.Load(animationBytes.bytes);
    player.Play();
}
```

Required runtime controls:

```csharp
Play();
Pause();
Resume();
Stop();
Restart();
Seek(float timeSeconds); // may be added after MVP, but format must support it
```

Required properties:

```csharp
bool Loop;
float Speed;
bool IsPlaying;
float CurrentTime;
float Duration;
int CurrentFrame;
int FrameCount;
Texture Texture;
```

Required callback/event:

```csharp
OnComplete
```

## 9. Runtime timing

Playback must be time-based, not `one Unity Update = one animation frame`.

The player computes the target frame from playback time and animation FPS.

If the game drops frames, QAnimator should advance to the correct animation frame instead of slowing the whole animation down.

## 10. Runtime memory/performance

Target platforms:

- Unity Editor;
- Windows;
- Android;
- iOS.

WebGL may be evaluated later.

Runtime goals:

- one reusable playback `Texture2D`;
- reusable pixel/decompression buffers;
- low or ideally zero steady-state GC allocations during playback;
- no LINQ or repeated temporary allocations in the hot path;
- do not create one texture per animation frame;
- do not fully decode all frames at load time;
- decode only what is needed for playback;
- architecture should allow future worker-thread decode, while Unity texture upload stays on the main thread.

For delta decoding, keeping a previous/current RGBA frame buffer is acceptable.

## 11. Seeking

Seeking to a delta frame must work by finding the closest prior key frame and applying subsequent delta frames until the requested frame is reconstructed.

The format must support this even if polished seek UI/API is deferred.

## 12. Resource loading

The core runtime must accept raw bytes and must not care how the game obtained them.

Supported sources may therefore include:

- `TextAsset`;
- Resources;
- YooAsset;
- AssetBundle;
- StreamingAssets;
- downloaded CDN bytes.

Example core API:

```csharp
player.Load(byte[] data);
```

## 13. Unity display targets

Primary output: reusable `Texture2D`.

First-class integration target: `UnityEngine.UI.RawImage`.

Optional wrappers may support:

- `Renderer` / `Material`;
- `SpriteRenderer`.

The runtime player itself should not be tightly coupled to one UI component.

## 14. Repository structure

Preferred structure:

```text
QAnimator-For-Unity/
├─ SPEC.md
├─ README.md
├─ Encoder/
│  ├─ QAnimator.Encoder.csproj
│  ├─ Program.cs
│  ├─ UI/
│  ├─ Encoding/
│  ├─ Ffmpeg/
│  └─ Format/
├─ Unity/
│  └─ Runtime/
│     ├─ QAnimatorPlayer.cs
│     ├─ QAnimatorReader.cs
│     ├─ QAnimatorDecoder.cs
│     ├─ QAnimatorFormat.cs
│     └─ QAnimatorRawImageOutput.cs
└─ Tests/
```

## 15. Development phases

### Phase 1 — working format + encoder core

- define v1 format;
- decode source video through FFmpeg;
- produce `.bytes` using keyframes + block delta + per-frame compression;
- create CLI/core conversion path first.

### Phase 2 — Unity decoder/player

- parse header and frame index;
- decode key/delta frames;
- upload into one reusable Texture2D;
- Play/Pause/Stop/Loop/Speed/Complete;
- no `VideoPlayer` anywhere.

### Phase 3 — Windows GUI

- dark WinForms-style UI inspired by the reference screenshot;
- queue and task progress;
- drag/drop input;
- settings and logs.

### Phase 4 — optimization

Benchmark 256x256 and 512x512 animations around 30 FPS / 3 seconds:

- output file size;
- startup/load time;
- average frame decode time;
- CPU time;
- texture upload time;
- GC allocation;
- runtime memory.

Then optimize codec/compression only where measurements justify it.

## 16. Acceptance criteria

The MVP is accepted when all of the following are true:

1. `frog.webm` or `frog.mp4` can be converted into `frog.bytes` by the Windows encoder.
2. The source video can then be removed from the Unity project.
3. Unity can load only `frog.bytes` and play the animation correctly.
4. Transparency is preserved when the source has alpha.
5. Playback timing remains correct independent of game frame rate.
6. The player reuses its texture/buffers instead of creating a texture every frame.
7. Unity runtime contains **no `VideoPlayer`**.
8. Unity runtime contains **no FFmpeg dependency**.
9. `.bytes` is a real QAnimator binary format, not a renamed or embedded MP4/WebM handed to a system decoder.

## 17. One-sentence definition

**QAnimator is an offline MP4/WebM-to-custom-binary animation encoder plus a Unity-native custom decoder that renders decoded RGBA frames to a reusable texture, with no Unity VideoPlayer in the runtime.**
