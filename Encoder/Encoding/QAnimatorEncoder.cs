using System.IO.Compression;
using QAnimator.Encoder.Format;

namespace QAnimator.Encoder.Encoding;

internal sealed record EncodeSettings(
    int Width,
    int Height,
    int Fps,
    int KeyFrameInterval,
    int BlockSize,
    CompressionLevel CompressionLevel,
    bool HasAlpha);

internal sealed class QAnimatorEncoder
{
    public async Task EncodeAsync(
        Stream rgbaFrameStream,
        string outputPath,
        EncodeSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rgbaFrameStream);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        if (settings.Width <= 0 || settings.Height <= 0 || settings.Width > ushort.MaxValue || settings.Height > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings), $"Width/height must be between 1 and {ushort.MaxValue} for QANM v1.");
        if (settings.Fps <= 0 || settings.Fps > 1000)
            throw new ArgumentOutOfRangeException(nameof(settings), "FPS must be between 1 and 1000.");
        if (settings.KeyFrameInterval <= 0 || settings.KeyFrameInterval > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings), $"Key frame interval must be between 1 and {ushort.MaxValue}.");
        if (settings.BlockSize <= 0 || settings.BlockSize > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings), "Block size is invalid.");

        int frameBytes = checked(settings.Width * settings.Height * 4);
        byte[] current = new byte[frameBytes];
        byte[] previous = new byte[frameBytes];

        string fullOutputPath = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(fullOutputPath)!;
        Directory.CreateDirectory(outputDirectory);
        string token = Guid.NewGuid().ToString("N");
        string frameTempPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{token}.frames.tmp");
        string finalTempPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{token}.output.tmp");

        var entries = new List<QFrameIndexEntry>();
        int nearestKey = 0;
        long tempOffset = 0;

        try
        {
            await using (var temp = new FileStream(frameTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                for (int frameIndex = 0; ; frameIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await ReadFrameAsync(rgbaFrameStream, current, cancellationToken);
                    if (read == 0)
                        break;
                    if (read != frameBytes)
                        throw new InvalidDataException($"Incomplete RGBA frame: {read}/{frameBytes} bytes.");

                    if (!settings.HasAlpha)
                        for (int pixel = 3; pixel < current.Length; pixel += 4) current[pixel] = 255;

                    bool isKey = frameIndex == 0 || frameIndex % settings.KeyFrameInterval == 0;
                    QFrameType type = isKey ? QFrameType.Key : QFrameType.Delta;
                    byte[] rawPayload;

                    if (isKey)
                    {
                        nearestKey = frameIndex;
                        rawPayload = current;
                    }
                    else
                    {
                        rawPayload = BuildDelta(previous, current, settings.Width, settings.Height, settings.BlockSize);
                    }

                    byte[] compressed = Compress(rawPayload, settings.CompressionLevel);
                    await temp.WriteAsync(compressed, cancellationToken);

                    entries.Add(new QFrameIndexEntry(
                        type,
                        nearestKey,
                        tempOffset,
                        compressed.Length,
                        rawPayload.Length,
                        frameIndex / (double)settings.Fps));

                    tempOffset = checked(tempOffset + compressed.Length);
                    Buffer.BlockCopy(current, 0, previous, 0, frameBytes);
                    progress?.Report(frameIndex + 1);
                }
            }

            if (entries.Count == 0)
                throw new InvalidDataException("FFmpeg/source stream produced no complete RGBA frames.");

            var header = new QAnimatorHeader
            {
                Width = settings.Width,
                Height = settings.Height,
                FpsNumerator = settings.Fps,
                FpsDenominator = 1,
                FrameCount = entries.Count,
                Duration = entries.Count / (double)settings.Fps,
                HasAlpha = settings.HasAlpha,
                KeyFrameInterval = checked((ushort)settings.KeyFrameInterval),
                BlockSize = checked((ushort)settings.BlockSize),
                FrameIndexOffset = QAnimatorHeader.SerializedSize,
                FrameDataOffset = checked(QAnimatorHeader.SerializedSize + (long)entries.Count * QFrameIndexEntry.SerializedSize),
            };

            await using (var output = new FileStream(finalTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    header.Write(writer);
                    foreach (var entry in entries)
                        entry.Write(writer);
                }

                await using var input = new FileStream(frameTempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(finalTempPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            TryDelete(frameTempPath);
            TryDelete(finalTempPath);
        }
    }

    private static async Task<int> ReadFrameAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static byte[] BuildDelta(byte[] previous, byte[] current, int width, int height, int blockSize)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(0); // changed block count placeholder
        int changed = 0;

        for (int y = 0; y < height; y += blockSize)
        {
            int bh = Math.Min(blockSize, height - y);
            for (int x = 0; x < width; x += blockSize)
            {
                int bw = Math.Min(blockSize, width - x);
                if (!BlockChanged(previous, current, width, x, y, bw, bh))
                    continue;

                changed = checked(changed + 1);
                writer.Write(checked((ushort)x));
                writer.Write(checked((ushort)y));
                writer.Write(checked((ushort)bw));
                writer.Write(checked((ushort)bh));

                for (int row = 0; row < bh; row++)
                {
                    int offset = checked(((y + row) * width + x) * 4);
                    writer.Write(current, offset, checked(bw * 4));
                }
            }
        }

        ms.Position = 0;
        writer.Write(changed);
        writer.Flush();
        return ms.ToArray();
    }

    private static bool BlockChanged(byte[] a, byte[] b, int width, int x, int y, int bw, int bh)
    {
        int rowBytes = checked(bw * 4);
        for (int row = 0; row < bh; row++)
        {
            int offset = checked(((y + row) * width + x) * 4);
            if (!a.AsSpan(offset, rowBytes).SequenceEqual(b.AsSpan(offset, rowBytes)))
                return true;
        }
        return false;
    }

    private static byte[] Compress(byte[] input, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
            deflate.Write(input, 0, input.Length);
        return output.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of temporary files.
        }
    }
}
