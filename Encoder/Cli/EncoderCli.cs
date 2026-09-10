using System.IO.Compression;
using QAnimator.Encoder.Encoding;
using QAnimator.Encoder.Ffmpeg;

namespace QAnimator.Encoder.Cli;

internal static class EncoderCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Input) || string.IsNullOrWhiteSpace(options.Output))
            throw new ArgumentException("--input and --output are required. Use --help for usage.");

        string input = Path.GetFullPath(options.Input);
        string output = Path.GetFullPath(options.Output);
        if (!File.Exists(input))
            throw new FileNotFoundException("Input video not found.", input);

        VideoInfo source = await FfmpegTools.ProbeAsync(input, CancellationToken.None);
        int width = options.Width > 0 ? options.Width : source.Width;
        int height = options.Height > 0 ? options.Height : source.Height;
        int fps = options.Fps > 0 ? options.Fps : Math.Max(1, (int)Math.Round(source.Fps));
        bool hasAlpha = options.PreserveAlpha && source.HasAlpha;

        Console.WriteLine($"QAnimator: {Path.GetFileName(input)} {source.Width}x{source.Height} {source.Fps:0.###}fps -> {width}x{height} {fps}fps alpha={hasAlpha}");

        using var ffmpeg = FfmpegTools.StartRgbaDecode(input, width, height, fps, source.CodecName, hasAlpha);
        try
        {
            var settings = new EncodeSettings(
                width,
                height,
                fps,
                options.KeyFrameInterval,
                options.BlockSize,
                options.CompressionLevel,
                hasAlpha);

            var encoder = new QAnimatorEncoder();
            await encoder.EncodeAsync(ffmpeg.StandardOutput.BaseStream, output, settings, progress: null, CancellationToken.None);
            await FfmpegTools.EnsureSuccessAsync(ffmpeg, CancellationToken.None);
        }
        catch
        {
            FfmpegTools.TryKill(ffmpeg);
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            throw;
        }

        Console.WriteLine($"QAnimator: wrote {output} ({new FileInfo(output).Length:N0} bytes)");
        return 0;
    }

    private static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a is "--help" or "-h" or "/?")
            {
                o.ShowHelp = true;
                continue;
            }

            string Next()
            {
                if (++i >= args.Length)
                    throw new ArgumentException($"Missing value for {a}.");
                return args[i];
            }

            switch (a.ToLowerInvariant())
            {
                case "--input": case "-i": o.Input = Next(); break;
                case "--output": case "-o": o.Output = Next(); break;
                case "--fps": o.Fps = int.Parse(Next()); break;
                case "--width": o.Width = int.Parse(Next()); break;
                case "--height": o.Height = int.Parse(Next()); break;
                case "--key-interval": o.KeyFrameInterval = int.Parse(Next()); break;
                case "--block-size": o.BlockSize = int.Parse(Next()); break;
                case "--no-alpha": o.PreserveAlpha = false; break;
                case "--compression":
                    o.CompressionLevel = Next().ToLowerInvariant() switch
                    {
                        "fastest" => CompressionLevel.Fastest,
                        "optimal" => CompressionLevel.Optimal,
                        "smallest" or "smallestsize" => CompressionLevel.SmallestSize,
                        var v => throw new ArgumentException($"Unknown compression level '{v}'.")
                    };
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{a}'. Use --help for usage.");
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("QAnimator Encoder CLI");
        Console.WriteLine("  QAnimatorEncoder.exe --input in.webm --output out.bytes [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --fps N             Output FPS; default = rounded source FPS");
        Console.WriteLine("  --width N           Output width; default = source width");
        Console.WriteLine("  --height N          Output height; default = source height");
        Console.WriteLine("  --key-interval N    Key-frame interval; default = 15");
        Console.WriteLine("  --block-size N      Delta block size; default = 16");
        Console.WriteLine("  --compression NAME  fastest|optimal|smallest; default = optimal");
        Console.WriteLine("  --no-alpha          Do not mark/preserve source alpha");
    }

    private sealed class Options
    {
        public string? Input;
        public string? Output;
        public int Fps;
        public int Width;
        public int Height;
        public int KeyFrameInterval = 15;
        public int BlockSize = 16;
        public CompressionLevel CompressionLevel = CompressionLevel.Optimal;
        public bool PreserveAlpha = true;
        public bool ShowHelp;
    }
}
