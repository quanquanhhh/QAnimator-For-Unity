using System.IO.Compression;
using QAnimator.Encoder.Encoding;
using QAnimator.Unity;

if (args.Length > 0)
{
    await ValidateEncodedFile(args[0]);
    return;
}

await RunRoundTripCase(width: 32, height: 32, fps: 10, frameCount: 6, keyInterval: 3, blockSize: 8, alpha: true);
await RunRoundTripCase(width: 37, height: 29, fps: 12, frameCount: 9, keyInterval: 4, blockSize: 16, alpha: true);
await RunUnchangedFrameCase();

Console.WriteLine("QAnimator codec smoke tests passed.");

static async Task RunRoundTripCase(int width, int height, int fps, int frameCount, int keyInterval, int blockSize, bool alpha)
{
    int frameBytes = checked(width * height * 4);
    byte[][] expected = new byte[frameCount][];
    using var rgbaStream = new MemoryStream();

    for (int frame = 0; frame < frameCount; frame++)
    {
        byte[] pixels = new byte[frameBytes];

        // Transparent background with a moving opaque rectangle and changing alpha edge.
        int squareX = (frame * 3) % Math.Max(1, width - 1);
        int squareY = (frame * 2) % Math.Max(1, height - 1);
        for (int y = squareY; y < Math.Min(squareY + 8, height); y++)
        {
            for (int x = squareX; x < Math.Min(squareX + 8, width); x++)
            {
                int p = (y * width + x) * 4;
                pixels[p + 0] = (byte)(20 + frame * 17);
                pixels[p + 1] = (byte)(180 - frame * 9);
                pixels[p + 2] = 90;
                pixels[p + 3] = (byte)(x == squareX || y == squareY ? 128 : 255);
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
            keyInterval,
            blockSize,
            CompressionLevel.Optimal,
            alpha);

        await encoder.EncodeAsync(rgbaStream, outputPath, settings, progress: null, CancellationToken.None);

        byte[] encoded = await File.ReadAllBytesAsync(outputPath);
        var decoder = new QAnimatorDecoder(encoded);

        Assert(decoder.Width == width, "Width mismatch");
        Assert(decoder.Height == height, "Height mismatch");
        Assert(decoder.FrameCount == frameCount, "Frame count mismatch");
        Assert(Math.Abs(decoder.Fps - fps) < 0.001f, "FPS mismatch");
        Assert(decoder.HasAlpha == alpha, "Alpha flag mismatch");

        for (int frame = 0; frame < frameCount; frame++)
        {
            decoder.DecodeFrame(frame);
            Assert(decoder.RgbaBuffer.AsSpan().SequenceEqual(expected[frame]), $"Sequential frame {frame} mismatch");
        }

        int[] seeks = [frameCount - 2, 1, frameCount - 1, 0, frameCount / 2, 2];
        foreach (int frame in seeks.Where(v => v >= 0 && v < frameCount))
        {
            decoder.DecodeFrame(frame);
            Assert(decoder.RgbaBuffer.AsSpan().SequenceEqual(expected[frame]), $"Seek frame {frame} mismatch");
        }
    }
    finally
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);
    }
}

static async Task RunUnchangedFrameCase()
{
    const int width = 19;
    const int height = 17;
    const int fps = 15;
    const int frameCount = 5;
    int frameBytes = width * height * 4;
    byte[] frame = new byte[frameBytes];
    for (int i = 0; i < frame.Length; i += 4)
    {
        frame[i + 0] = 12;
        frame[i + 1] = 34;
        frame[i + 2] = 56;
        frame[i + 3] = 200;
    }

    using var stream = new MemoryStream();
    for (int i = 0; i < frameCount; i++)
        stream.Write(frame, 0, frame.Length);
    stream.Position = 0;

    string outputPath = Path.Combine(Path.GetTempPath(), $"qanim-static-{Guid.NewGuid():N}.bytes");
    try
    {
        var encoder = new QAnimatorEncoder();
        await encoder.EncodeAsync(
            stream,
            outputPath,
            new EncodeSettings(width, height, fps, 4, 16, CompressionLevel.Fastest, true),
            null,
            CancellationToken.None);

        var decoder = new QAnimatorDecoder(await File.ReadAllBytesAsync(outputPath));
        for (int i = 0; i < frameCount; i++)
        {
            decoder.DecodeFrame(i);
            Assert(decoder.RgbaBuffer.AsSpan().SequenceEqual(frame), $"Static frame {i} mismatch");
        }
    }
    finally
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);
    }
}

static async Task ValidateEncodedFile(string path)
{
    byte[] encoded = await File.ReadAllBytesAsync(path);
    var decoder = new QAnimatorDecoder(encoded);
    Assert(decoder.Width > 0 && decoder.Height > 0, "Invalid dimensions");
    Assert(decoder.FrameCount > 0, "Encoded file contains no frames");
    Assert(decoder.Fps > 0, "Invalid FPS");

    for (int i = 0; i < decoder.FrameCount; i++)
        decoder.DecodeFrame(i);

    if (decoder.FrameCount > 2)
    {
        decoder.DecodeFrame(decoder.FrameCount - 1);
        decoder.DecodeFrame(0);
        decoder.DecodeFrame(decoder.FrameCount / 2);
    }

    Console.WriteLine($"Validated {Path.GetFileName(path)}: {decoder.Width}x{decoder.Height}, {decoder.FrameCount} frames @ {decoder.Fps:0.###}fps, alpha={decoder.HasAlpha}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
