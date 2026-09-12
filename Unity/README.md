# QAnimator Unity Runtime

Target: **Unity 6000.0+**.

This package only reads QAnimator `.bytes` files. It contains no video decoder dependency, no FFmpeg, and no Unity `VideoPlayer`.

## Install from Git

Package Manager -> Add package from git URL:

```text
https://github.com/quanquanhhh/QAnimator-For-Unity.git?path=/Unity
```

## UGUI

Create one object below a Canvas and add:

- `QEmoGraphic`

Assign a QAnimator `.bytes` `TextAsset` to **Animation Bytes**. The first frame appears in Edit Mode, and **Auto Play** starts it in Play Mode. The component owns decoding, timing, its reusable texture, and UGUI rendering; no helper object or `RawImage` is needed.

Or load from code:

```csharp
qEmoGraphic.Play(animationBytes);
```

QANM v1 contains one animation per file. Optional files can be assigned under **Additional Animations**, and `Play("happy")` switches to the assigned asset named `happy`.

## SpriteRenderer

Add:

- `SpriteRenderer`
- `QAnimatorPlayer`
- `QAnimatorSpriteRendererOutput`

This legacy adapter creates one Sprite around the player's reusable Texture2D. It does not create a new Sprite every animation frame.

## Runtime-loaded bytes

QAnimator is intentionally agnostic about asset delivery:

```csharp
byte[] bytes = await DownloadOrLoadSomehow();
qEmoGraphic.Play(bytes);
```

The bytes can therefore come from YooAsset, AssetBundle, StreamingAssets, Resources, a CDN, or any other system.
