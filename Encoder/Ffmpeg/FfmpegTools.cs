using System.Diagnostics;
using System.Globalization;

namespace QAnimator.Encoder.Ffmpeg;

internal sealed record VideoInfo(int Width, int Height, double Fps, double Duration, string PixelFormat, bool HasAlpha);

internal static class FfmpegTools
{
    public static string ResolveTool(string fileName)
    {
        string local = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(local) ? local : fileName;
    }

    public static async Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken)
    {
        string ffprobe = ResolveTool("ffprobe.exe");
        string args = $"-v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate,pix_fmt:format=duration -of default=noprint_wrappers=1:nokey=0 \"{inputPath}\"";
        string output = await RunCaptureAsync(ffprobe, args, cancellationToken);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = raw.IndexOf('=');
            if (eq > 0)
                values[raw[..eq].Trim()] = raw[(eq + 1)..].Trim();
        }

        int width = int.Parse(values["width"], CultureInfo.InvariantCulture);
        int height = int.Parse(values["height"], CultureInfo.InvariantCulture);
        double fps = ParseRate(values.GetValueOrDefault("avg_frame_rate") ?? "30/1");
        double duration = double.TryParse(values.GetValueOrDefault("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
        string pixelFormat = values.GetValueOrDefault("pix_fmt") ?? string.Empty;
        bool hasAlpha = PixelFormatHasAlpha(pixelFormat);
        return new VideoInfo(width, height, fps, duration, pixelFormat, hasAlpha);
    }

    public static Process StartRgbaDecode(string inputPath, int targetWidth, int targetHeight, int targetFps)
    {
        string ffmpeg = ResolveTool("ffmpeg.exe");
        var filters = new List<string>();
        if (targetWidth > 0 && targetHeight > 0)
            filters.Add($"scale={targetWidth}:{targetHeight}:flags=lanczos");
        if (targetFps > 0)
            filters.Add($"fps={targetFps}");

        string filterArg = filters.Count > 0 ? $"-vf \"{string.Join(',', filters)}\" " : string.Empty;
        string args = $"-hide_banner -loglevel error -i \"{inputPath}\" {filterArg}-an -sn -dn -f rawvideo -pix_fmt rgba pipe:1";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
    }

    public static async Task EnsureSuccessAsync(Process process, CancellationToken cancellationToken)
    {
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}:\n{stderr}");
    }

    private static async Task<string> RunCaptureAsync(string fileName, string args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Unable to start {fileName}.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed: {stderr}");
        return stdout;
    }

    private static double ParseRate(string rate)
    {
        string[] pieces = rate.Split('/');
        if (pieces.Length == 2 && double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) &&
            double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d != 0)
            return n / d;
        return double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out double direct) ? direct : 30;
    }

    private static bool PixelFormatHasAlpha(string pixelFormat)
    {
        string p = pixelFormat.ToLowerInvariant();
        return p.Contains("rgba") || p.Contains("argb") || p.Contains("bgra") || p.Contains("yuva") || p.Contains("gbrap");
    }
}
