# QAnimator Unity Runtime

Target: **Unity 6000.0+**.

This package only reads QAnimator `.bytes` files. It contains no video decoder dependency, no FFmpeg, and no Unity `VideoPlayer`.

## Install from Git

Package Manager -> Add package from git URL:

```text
https://github.com/quanquanhhh/QAnimator-For-Unity.git?path=/Unity
```

## UGUI

Add these components to the same GameObject:

- `RawImage`
- `QAnimatorPlayer`
- `QAnimatorRawImageOutput`

Assign a QAnimator `.bytes` `TextAsset` to the player's Source field and optionally enable Play On Awake.

Or load from code:

```csharp
player.Play(animationBytes);
```

## SpriteRenderer

Add:

- `SpriteRenderer`
- `QAnimatorPlayer`
- `QAnimatorSpriteRendererOutput`

The adapter creates one Sprite around the player's reusable Texture2D. It does not create a new Sprite every animation frame.

## Runtime-loaded bytes

QAnimator is intentionally agnostic about asset delivery:

```csharp
byte[] bytes = await DownloadOrLoadSomehow();
player.Play(bytes);
```

The bytes can therefore come from YooAsset, AssetBundle, StreamingAssets, Resources, a CDN, or any other system.
