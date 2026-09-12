# QEmoGraphic Design Specification

## Goal

QEmoGraphic is the final user-facing Unity UGUI component of QAnimator.

The goal is to provide a workflow similar to Spine-Unity SkeletonGraphic.

The user should not need to understand decoder, texture update, playback management, or internal runtime classes.

Final usage:

```
Canvas
 └── QEmoGraphic
```

Bind an animation bytes asset in Inspector and the animation can play.

---

## Important Scope Decision

Do NOT implement yet:

- .qemo asset format
- custom AssetImporter
- complex resource pipeline
- skeleton/bone animation
- mesh deformation
- IK
- Spine data structures

Current priority:

```
bytes animation data
        ↓
QEmoGraphic
        ↓
UGUI rendering
```

Keep the current `.bytes` format.

Resource format optimization can happen later.

---

# Component

Create:

```
QEmoGraphic.cs
```

It should inherit:

```csharp
UnityEngine.UI.MaskableGraphic
```

Reason:

- Native UGUI support
- CanvasRenderer support
- Mask support
- Similar architecture to Spine SkeletonGraphic

---

# Hierarchy

There should be no helper objects.

Wrong:

```
Canvas
 └── QAnimatorPlayer
      └── RawImage
           └── Decoder
```

Correct:

```
Canvas
 └── QEmoGraphic
```

All internal systems are hidden inside the component.

---

# Inspector Design

Target experience:

```
QEmoGraphic

Animation Bytes
[ frog.bytes ]

Animation
[ idle ▼ ]

Auto Play       ☑
Loop            ☑
Speed           1.0
Color           White

```

Internal fields should not expose:

- decoder
- texture buffer
- playback object
- frame management

---

# Runtime API

Example:

```csharp
qEmoGraphic.Play();
qEmoGraphic.Play("happy");
qEmoGraphic.Pause();
qEmoGraphic.Resume();
qEmoGraphic.Stop();
qEmoGraphic.Seek(1.5f);
```

Required features:

- Auto Play
- Loop
- Speed control
- Animation switching
- Complete callback

---

# Internal Architecture

QEmoGraphic owns:

```
QEmoGraphic
    |
    ├── QAnimatorDecoder
    ├── Texture2D playback texture
    └── playback state
```

Users should only interact with QEmoGraphic.

---

# Editor Experience

Reference: Spine-Unity SkeletonGraphic.

## Non Play Mode

When selecting the component in Unity Editor:

```
Canvas
 └── QEmoGraphic
```

The first frame should be visible.

No need to enter Play Mode.

---

## Inspector Preview

Custom Inspector should provide a preview area:

```
Preview

   Animation Image

[Play] [Pause] [Stop]

Frame: 20 / 120
Time: 0.66s
```

Preview is for editor workflow only.

---

# Rendering

Do not require a separate RawImage object.

QEmoGraphic itself should render through UGUI.

Preferred:

- MaskableGraphic
- CanvasRenderer
- reusable Texture2D

Support:

- RectTransform
- CanvasGroup
- Color tint
- Mask
- RectMask2D

---

# Development Strategy

Phase 1:

Replace current demo workflow:

Remove dependency on:

- QAnimatorPlayer
- QAnimatorRawImageOutput

Use:

```
Canvas
 └── QEmoGraphic
```

Phase 2:

Add Editor preview:

- first frame display
- inspector playback preview

Phase 3:

Only after the workflow is stable consider:

- .qemo asset
- importer
- advanced resource management

---

# Spine Reference Rule

Spine-Unity may be used as UX/reference inspiration.

Study:

- SkeletonGraphic implementation style
- MaskableGraphic usage
- Custom Inspector
- Editor Preview workflow

Do NOT copy Spine animation architecture or APIs.

Goal:

Learn the user experience, not the implementation.

---

# Acceptance Criteria

The final user experience should be:

1. Create Canvas.
2. Add QEmoGraphic.
3. Assign frog.bytes.
4. See first frame in Editor.
5. Enter Play Mode and animation plays.
6. Call Play("happy") from code.

No extra objects required.

QEmoGraphic should feel like the QAnimator equivalent of Spine SkeletonGraphic.
