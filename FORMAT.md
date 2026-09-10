# QANM v1 Binary Format

This document is the canonical byte-level description of the QAnimator v1 file format.

All integer values are little-endian. All offsets are byte offsets. Pixel data is RGBA32, 8 bits per channel.

## File layout

```text
+---------------------------+
| Header (64 bytes)         |
+---------------------------+
| Frame index (32 * N)      |
+---------------------------+
| Compressed frame payloads |
+---------------------------+
```

## Header — 64 bytes

| Offset | Size | Type | Field | v1 value / meaning |
|---:|---:|---|---|---|
| 0 | 4 | uint32 | magic | `0x4D4E4151` (`QANM` bytes) |
| 4 | 2 | uint16 | version | `1` |
| 6 | 2 | uint16 | headerSize | `64` |
| 8 | 4 | int32 | width | pixels |
| 12 | 4 | int32 | height | pixels |
| 16 | 4 | int32 | fpsNumerator | output FPS numerator |
| 20 | 4 | int32 | fpsDenominator | output FPS denominator; currently `1` |
| 24 | 4 | int32 | frameCount | number of frames |
| 28 | 8 | float64 | duration | seconds |
| 36 | 1 | uint8 | pixelFormat | `1 = RGBA32` |
| 37 | 1 | bool/u8 | hasAlpha | `0/1` |
| 38 | 1 | uint8 | codec | `1 = BlockDelta` |
| 39 | 1 | uint8 | compression | `1 = Deflate` |
| 40 | 2 | uint16 | keyFrameInterval | encoder setting |
| 42 | 2 | uint16 | blockSize | encoder setting |
| 44 | 8 | int64 | frameIndexOffset | normally `64` |
| 52 | 8 | int64 | frameDataOffset | header + frame index |
| 60 | 4 | bytes | reserved | zero in v1 |

## Frame index entry — 32 bytes

There are exactly `frameCount` entries.

| Entry offset | Size | Type | Field |
|---:|---:|---|---|
| 0 | 1 | uint8 | frameType (`0=Key`, `1=Delta`) |
| 1 | 3 | bytes | reserved |
| 4 | 4 | int32 | nearestKeyFrame |
| 8 | 8 | int64 | dataOffset, relative to `frameDataOffset` |
| 16 | 4 | int32 | compressedLength |
| 20 | 4 | int32 | uncompressedLength |
| 24 | 8 | float64 | timestamp seconds |

`nearestKeyFrame` points to the closest key frame at or before this frame. A key frame references itself.

## Key frame payload

After per-frame Deflate decompression, a key payload is exactly:

```text
width * height * 4 bytes
```

The bytes are full RGBA32 pixels.

The offline FFmpeg stage vertically flips source frames before QAnimator encoding so the stored scanline order can be uploaded directly to the Unity `Texture2D` path used by QAnimator without doing a per-frame vertical flip at runtime.

## Delta frame payload

After Deflate decompression:

```text
int32 changedBlockCount
repeat changedBlockCount times:
    uint16 x
    uint16 y
    uint16 blockWidth
    uint16 blockHeight
    byte[blockWidth * blockHeight * 4] RGBA rows
```

Pixels for a changed block are stored row-by-row. Blocks that are identical to the previous frame are omitted.

The decoder starts with the preceding decoded frame and overwrites the listed changed blocks.

## Seeking

To seek to frame `T`:

1. Read frame `T`'s `nearestKeyFrame` index `K`.
2. Decode key frame `K`.
3. Apply delta/key frames `K+1 ... T` in order.

Normal forward playback can continue from the currently decoded frame without rebuilding from a key frame.

## Compression

Each frame payload is compressed independently with Deflate. The complete file is deliberately not compressed as one stream because random access requires independently addressable frame payloads.

## Alpha

`hasAlpha` indicates the source/conversion path contains meaningful alpha. Regardless of this flag, v1 pixel storage is RGBA32. Transparent WebM inputs are decoded to RGBA offline and the alpha byte is preserved in QANM payloads.

## Compatibility rules

A v1 reader should reject a file when:

- magic/version/header size are unsupported;
- dimensions/FPS/frame count are invalid;
- the first frame is not a key frame;
- frame offsets or lengths point outside the file;
- a key frame payload is not exactly `width*height*4` after decompression;
- a delta block lies outside the image or declared block grid;
- a frame references an invalid nearest key frame.

Future codecs/compression algorithms must use new enum values and, when byte layout compatibility requires it, a new format version.
