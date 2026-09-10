using System.IO.Compression;
using QAnimator.Encoder.Encoding;
using QAnimator.Unity;

const int width = 32;
const int height = 32;
const int fps = 10;
const int frameCount = 6;
const int frameBytes = width * height * 4;

byte[][] expected = new byte[frameCount][];
using var rgbaStream = new MemoryStream();

for (int frame = 0; frame < frameCount; frame++)
{
    byte[] pixels = new byte[frameBytes];

    // Transparent background with a moving opaque square.
    int squareX = frame * 3;
    int squareY = frame * 2;
    for (int y = squareY; y < Math.Min(squareY + 8, height); y++)
    {
        for (int x = squareX; x < Math.Min(squareX + 8, width); x++)
        {
            int p = (y * width + x) * 4;
            pixels[p + 0] = (byte)(20 + frame * 20);
            pixels[p + 1] = (byte)(180 - frame * 10);
            pixels[p + 2] = 90;
            pixels[p + 3] = 255;
        }
    }

    expected[frame] = pixels;
    rgbaStream.Write(pixels, 0, pixels.Length);
}

rgbaStream.Position = 0;
string outputPath = Path.Combine(Path.GetTempPath(), $"qanim-smoke-{Guid.NewGuid():N}.bytes");

try
{
    var encoder = new QAnimatorEncoder();
    var settings = new EncodeSettings(
        width,
        height,
        fps,
        KeyFrameInterval: 3,
        BlockSize: 8,
        CompressionLevel.Optimal,
        HasAlpha: true);

    await encoder.EncodeAsync(rgbaStream, outputPath, settings, progress: null, CancellationToken.None);

    byte[] encoded = await File.ReadAllBytesAsync(outputPath);
    var decoder = new QAnimatorDecoder(encoded);

    Assert(decoder.Width == width, "Width mismatch");
    Assert(decoder.Height == height, "Height mismatch");
    Assert(decoder.FrameCount == frameCount, "Frame count mismatch");
    Assert(Math.Abs(decoder.Fps - fps) < 0.001f, "FPS mismatch");
    Assert(decoder.HasAlpha, "Alpha flag mismatch");

    // Forward sequential decode.
    for (int frame = 0; frame < frameCount; frame++)
    {
        decoder.DecodeFrame(frame);
        Assert(decoder.RgbaBuffer.AsSpan().SequenceEqual(expected[frame]), $"Sequential frame {frame} mismatch");
    }

    // Backward/random access must rebuild from nearest key frame correctly.
    int[] seeks = [4, 1, 5, 0, 3, 2];
    foreach (int frame in seeks)
    {
        decoder.DecodeFrame(frame);
        Assert(decoder.RgbaBuffer.AsSpan().SequenceEqual(expected[frame]), $"Seek frame {frame} mismatch");
    }

    Console.WriteLine($"QAnimator codec smoke test passed. {frameCount} RGBA frames round-tripped exactly.");
}
finally
{
    if (File.Exists(outputPath))
        File.Delete(outputPath);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
