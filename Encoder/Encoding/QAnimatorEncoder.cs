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
        if (settings.Width <= 0 || settings.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "Width/height must be positive.");
        if (settings.Fps <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "FPS must be positive.");
        if (settings.KeyFrameInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "Key frame interval must be positive.");
        if (settings.BlockSize <= 0 || settings.BlockSize > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings), "Block size is invalid.");

        int frameBytes = checked(settings.Width * settings.Height * 4);
        byte[] current = new byte[frameBytes];
        byte[] previous = new byte[frameBytes];

        string tempPath = outputPath + ".frames.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var entries = new List<QFrameIndexEntry>();
        int nearestKey = 0;
        long tempOffset = 0;

        try
        {
            await using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                for (int frameIndex = 0; ; frameIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await ReadFrameAsync(rgbaFrameStream, current, cancellationToken);
                    if (read == 0)
                        break;
                    if (read != frameBytes)
                        throw new InvalidDataException($"Incomplete RGBA frame: {read}/{frameBytes} bytes.");

                    bool isKey = frameIndex == 0 || frameIndex % settings.KeyFrameInterval == 0;
                    QFrameType type = isKey ? QFrameType.Key : QFrameType.Delta;
                    byte[] rawPayload;

                    if (isKey)
                    {
                        nearestKey = frameIndex;
                        rawPayload = current.ToArray();
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

                    tempOffset += compressed.Length;
                    Buffer.BlockCopy(current, 0, previous, 0, frameBytes);
                    progress?.Report(frameIndex + 1);
                }
            }

            if (entries.Count == 0)
                throw new InvalidDataException("FFmpeg produced no frames.");

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
                FrameDataOffset = QAnimatorHeader.SerializedSize + (long)entries.Count * QFrameIndexEntry.SerializedSize,
            };

            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
            using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                header.Write(writer);
                foreach (var entry in entries)
                    entry.Write(writer);
            }

            await using var input = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
            await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
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

                changed++;
                writer.Write((ushort)x);
                writer.Write((ushort)y);
                writer.Write((ushort)bw);
                writer.Write((ushort)bh);

                for (int row = 0; row < bh; row++)
                {
                    int offset = ((y + row) * width + x) * 4;
                    writer.Write(current, offset, bw * 4);
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
        int rowBytes = bw * 4;
        for (int row = 0; row < bh; row++)
        {
            int offset = ((y + row) * width + x) * 4;
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
}
