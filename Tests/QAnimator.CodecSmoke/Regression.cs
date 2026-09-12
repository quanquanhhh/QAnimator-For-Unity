using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using QAnimator.Encoder.Encoding;
using QAnimator.Encoder.Ffmpeg;
using QAnimator.Unity;

internal static class Regression
{
    public static async Task Core()
    {
        string directory = Path.Combine(Path.GetTempPath(), "qanim-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "test.bytes");
        try
        {
            byte[] pixels = [10, 20, 30, 0, 50, 60, 70, 128];
            var settings = new EncodeSettings(2, 1, 30, 15, 16, CompressionLevel.Optimal, false);
            await new QAnimatorEncoder().EncodeAsync(new MemoryStream(pixels), output, settings, null, default);
            byte[] valid = await File.ReadAllBytesAsync(output);
            var decoder = new QAnimatorDecoder(valid);
            decoder.DecodeFrame(0);
            Check(!decoder.HasAlpha && decoder.RgbaBuffer[3] == 255 && decoder.RgbaBuffer[7] == 255, "No-alpha must force opaque pixels");
            Check(decoder.RgbaBuffer[0] == 10, "No-alpha must preserve RGB");
            try
            {
                await new QAnimatorEncoder().EncodeAsync(new MemoryStream([1, 2, 3]), output, settings, null, default);
                throw new Exception("Incomplete frame accepted");
            }
            catch (InvalidDataException) { }
            Check(valid.SequenceEqual(await File.ReadAllBytesAsync(output)), "Failure replaced existing output");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await new QAnimatorEncoder().EncodeAsync(new MemoryStream(pixels), output, settings, null, cancelled.Token);
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) { }
            Check(valid.SequenceEqual(await File.ReadAllBytesAsync(output)), "Cancellation replaced existing output");
            Check(Directory.GetFiles(directory).Length == 1, "Temporary files leaked");
            Check(VideoConversion.ResolveSize(1920, 1080, 640, 0) == (640, 360), "Width-only aspect ratio");
            Check(VideoConversion.ResolveSize(1920, 1080, 0, 360) == (640, 360), "Height-only aspect ratio");
            foreach (int length in new[] { 0, 63, 64, valid.Length - 4 })
            {
                try
                {
                    var bad = new QAnimatorDecoder(valid.AsSpan(0, length).ToArray());
                    bad.DecodeFrame(0);
                    throw new Exception("Truncated data accepted");
                }
                catch (InvalidDataException) { }
            }
            byte[] hugeIndex = (byte[])valid.Clone();
            BitConverter.GetBytes(int.MaxValue).CopyTo(hugeIndex, 24);
            try { _ = new QAnimatorDecoder(hugeIndex); throw new Exception("Invalid frame count accepted"); }
            catch (InvalidDataException) { }
            Console.WriteLine("Regression passed: opaque alpha, aspect ratio, atomic failure/cancellation, malformed data.");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    public static async Task Integration(string input, string output)
    {
        var source = await FfmpegTools.ProbeAsync(input, default);
        int fps = Math.Max(1, (int)Math.Round(source.Fps));
        var settings = new EncodeSettings(source.Width, source.Height, fps, 15, 16, CompressionLevel.Optimal, source.HasAlpha);
        await VideoConversion.ConvertAsync(input, output, source, settings, null, default);
        var decoder = new QAnimatorDecoder(await File.ReadAllBytesAsync(output));
        using var reference = FfmpegTools.StartRgbaDecode(input, source.Width, source.Height, fps, source.CodecName, source.HasAlpha);
        Task completion = FfmpegTools.EnsureSuccessAsync(reference, default);
        var hashes = new List<byte[]>();
        byte[] expected = new byte[source.Width * source.Height * 4];
        try
        {
            for (int frame = 0; frame < decoder.FrameCount; frame++)
            {
                await reference.StandardOutput.BaseStream.ReadExactlyAsync(expected);
                decoder.DecodeFrame(frame);
                Check(decoder.RgbaBuffer.AsSpan().SequenceEqual(expected), $"RGBA mismatch at frame {frame}");
                hashes.Add(SHA256.HashData(expected));
            }
            Check(reference.StandardOutput.BaseStream.ReadByte() == -1, "Frame count mismatch");
            await completion;
        }
        finally { FfmpegTools.TryKill(reference); }
        var rng = new Random(17);
        for (int i = 0; i < 30; i++)
        {
            int frame = rng.Next(decoder.FrameCount);
            decoder.DecodeFrame(frame);
            Check(SHA256.HashData(decoder.RgbaBuffer).AsSpan().SequenceEqual(hashes[frame]), $"Seek mismatch {frame}");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < decoder.FrameCount; i++) decoder.DecodeFrame(i);
        stopwatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine($"PASS {Path.GetFileName(input)}: {decoder.Width}x{decoder.Height}, {decoder.FrameCount} frames, alpha={decoder.HasAlpha}; all RGBA bytes + 30 random seeks match.");
        Console.WriteLine($"Decode: {stopwatch.Elapsed.TotalMilliseconds / decoder.FrameCount:0.000} ms/frame; managed allocations: {allocated / decoder.FrameCount} bytes/frame (.NET host, not Unity). Output {new FileInfo(output).Length:N0} bytes.");

        // A missing source after probing must not remove the last good output.
        byte[] before = await File.ReadAllBytesAsync(output);
        try
        {
            await VideoConversion.ConvertAsync(input + ".missing", output, source, settings, null, default);
            throw new Exception("Missing source accepted");
        }
        catch (InvalidDataException) { }
        catch (InvalidOperationException) { }
        Check(before.SequenceEqual(await File.ReadAllBytesAsync(output)), "FFmpeg failure destroyed valid output");
        Console.WriteLine("FFmpeg failure preserves existing output: PASS");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
