using QAnimator.Encoder.Ffmpeg;

namespace QAnimator.Encoder.Encoding;

// Both the GUI and CLI publish only after the decoder process has succeeded.
internal static class VideoConversion
{
    public static (int Width, int Height) ResolveSize(int width, int height, int requestedWidth, int requestedHeight)
    {
        if (requestedWidth < 0 || requestedHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedWidth), "Dimensions cannot be negative.");
        if (requestedWidth == 0 && requestedHeight == 0) return (width, height);
        if (requestedWidth == 0) requestedWidth = Math.Max(1, (int)Math.Round(width * (requestedHeight / (double)height)));
        if (requestedHeight == 0) requestedHeight = Math.Max(1, (int)Math.Round(height * (requestedWidth / (double)width)));
        return (requestedWidth, requestedHeight);
    }

    public static async Task ConvertAsync(string input, string output, VideoInfo source,
        EncodeSettings settings, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input and output must be different files.");
        if (!string.Equals(Path.GetExtension(output), ".bytes", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must use the .bytes extension.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string staged = output + "." + Guid.NewGuid().ToString("N") + ".output.tmp";
        try
        {
            using var ffmpeg = FfmpegTools.StartRgbaDecode(input, settings.Width, settings.Height,
                settings.Fps, source.CodecName, settings.HasAlpha);
            Task completion = FfmpegTools.EnsureSuccessAsync(ffmpeg, cancellationToken);
            try
            {
                await new QAnimatorEncoder().EncodeAsync(ffmpeg.StandardOutput.BaseStream, staged,
                    settings, progress, cancellationToken).ConfigureAwait(false);
                await completion.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(staged, output, overwrite: true);
            }
            catch
            {
                FfmpegTools.TryKill(ffmpeg);
                try { await completion.ConfigureAwait(false); } catch { }
                throw;
            }
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }
}
