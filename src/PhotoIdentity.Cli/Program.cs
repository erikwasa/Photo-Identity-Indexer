using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args, Console.Out, Console.Error);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("error: operation cancelled");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintUsage(output);
            return args.Length == 0 ? 2 : 0;
        }

        if (!string.Equals(args[0], "decode", StringComparison.Ordinal))
        {
            error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage(error);
            return 2;
        }

        DecodeCommandOptions options;
        try
        {
            options = DecodeCommandOptions.Parse(args.Skip(1).ToArray());
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"error: {exception.Message}");
            PrintUsage(error);
            return 2;
        }

        return await DecodeAsync(options, output, error, cancellationToken);
    }

    private static async Task<int> DecodeAsync(
        DecodeCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string inputPath = Path.GetFullPath(options.InputPath);
        string outputPath = Path.GetFullPath(options.OutputPath);
        string? reportPath = options.ReportPath is null
            ? null
            : Path.GetFullPath(options.ReportPath);

        if (!File.Exists(inputPath))
        {
            error.WriteLine("error: input file does not exist");
            return 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (reportPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        }

        byte[] hashBefore = await ComputeHashAsync(inputPath, cancellationToken);
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await using FileStream input = new(
                inputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);

            ImageSize? maximumSize = options.MaximumWidth is null
                ? null
                : new ImageSize(options.MaximumWidth.Value, options.MaximumHeight!.Value);

            OpenCvImageDecoder decoder = new();
            ImageFrame frame = await decoder.DecodeAsync(
                input,
                new DecodeOptions(maximumSize),
                cancellationToken);

            await using FileStream destination = new(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);

            OpenCvPngEncoder encoder = new();
            await encoder.EncodeAsync(frame, destination, cancellationToken);
            stopwatch.Stop();

            byte[] hashAfter = await ComputeHashAsync(inputPath, cancellationToken);
            bool inputUnchanged = hashBefore.AsSpan().SequenceEqual(hashAfter);

            DecodeVerificationReport report = new(
                Result: inputUnchanged ? "passed" : "failed",
                SourceType: SourceType(inputPath),
                Width: frame.Size.Width,
                Height: frame.Size.Height,
                PixelFormat: frame.Format.ToString(),
                Stride: frame.Stride,
                ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                InputUnchanged: inputUnchanged,
                OutputFileName: Path.GetFileName(outputPath));

            if (reportPath is not null)
            {
                await WriteReportAsync(reportPath, report, cancellationToken);
            }

            output.WriteLine($"decoded: {frame.Size.Width}x{frame.Size.Height} {frame.Format}");
            output.WriteLine($"output: {outputPath}");
            output.WriteLine($"elapsed-ms: {stopwatch.ElapsedMilliseconds}");
            output.WriteLine($"input-unchanged: {inputUnchanged.ToString().ToLowerInvariant()}");

            if (options.Verbose)
            {
                output.WriteLine($"input: {inputPath}");
            }

            return inputUnchanged ? 0 : 1;
        }
        catch (ImageDecodingException exception)
            when (exception.Failure == ImageDecodingFailure.UnsupportedFormat)
        {
            error.WriteLine($"unsupported-format: {exception.Message}");
            return 3;
        }
        catch (ImageDecodingException exception)
            when (exception.Failure == ImageDecodingFailure.CorruptMedia)
        {
            error.WriteLine($"corrupt-media: {exception.Message}");
            return 4;
        }
    }

    private static async Task<byte[]> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using SHA256 sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(stream, cancellationToken);
    }

    private static async Task WriteReportAsync(
        string path,
        DecodeVerificationReport report,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true);

        await JsonSerializer.SerializeAsync(
            stream,
            report,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            },
            cancellationToken);
    }

    private static string SourceType(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(extension) ? "unknown" : extension;
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
            Photo Identity Indexer CLI

              decode --input PATH --output PATH [--report PATH]
                     [--max-width PIXELS --max-height PIXELS] [--verbose]

            The decode command reads JPEG or PNG content, applies EXIF orientation,
            optionally downsizes it, and writes a normalised PNG without modifying the input.
            """);
    }

    private sealed record DecodeCommandOptions(
        string InputPath,
        string OutputPath,
        string? ReportPath,
        int? MaximumWidth,
        int? MaximumHeight,
        bool Verbose)
    {
        public static DecodeCommandOptions Parse(string[] args)
        {
            string? input = null;
            string? output = null;
            string? report = null;
            int? maximumWidth = null;
            int? maximumHeight = null;
            bool verbose = false;

            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--verbose")
                {
                    verbose = true;
                    continue;
                }

                string value = index + 1 < args.Length
                    ? args[++index]
                    : throw new ArgumentException($"Option '{option}' requires a value.");

                switch (option)
                {
                    case "--input":
                        input = Single(input, value, option);
                        break;
                    case "--output":
                        output = Single(output, value, option);
                        break;
                    case "--report":
                        report = Single(report, value, option);
                        break;
                    case "--max-width":
                        maximumWidth = PositiveInteger(maximumWidth, value, option);
                        break;
                    case "--max-height":
                        maximumHeight = PositiveInteger(maximumHeight, value, option);
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{option}'.");
                }
            }

            if (input is null || output is null)
            {
                throw new ArgumentException("Both --input and --output are required.");
            }

            if ((maximumWidth is null) != (maximumHeight is null))
            {
                throw new ArgumentException("--max-width and --max-height must be supplied together.");
            }

            return new DecodeCommandOptions(
                input,
                output,
                report,
                maximumWidth,
                maximumHeight,
                verbose);
        }

        private static string Single(string? current, string value, string option)
        {
            if (current is not null)
            {
                throw new ArgumentException($"Option '{option}' may be supplied only once.");
            }

            return value;
        }

        private static int PositiveInteger(int? current, string value, string option)
        {
            if (current is not null)
            {
                throw new ArgumentException($"Option '{option}' may be supplied only once.");
            }

            return int.TryParse(value, out int parsed) && parsed > 0
                ? parsed
                : throw new ArgumentException($"Option '{option}' requires a positive integer.");
        }
    }

    private sealed record DecodeVerificationReport(
        string Result,
        string SourceType,
        int Width,
        int Height,
        string PixelFormat,
        int Stride,
        long ElapsedMilliseconds,
        bool InputUnchanged,
        string OutputFileName);
}
