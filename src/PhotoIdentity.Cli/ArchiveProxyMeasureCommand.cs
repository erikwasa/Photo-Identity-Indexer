using System.Globalization;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Cli;

internal sealed record ArchiveProxyMeasureCommandOptions(
    string SourceRoot,
    string OutputRoot,
    IReadOnlyList<ReviewProxyProfile> Profiles,
    bool Recursive)
{
    public static ArchiveProxyMeasureCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "measure", StringComparison.Ordinal))
        {
            throw new ArgumentException("Archive proxy requires the 'measure' action.");
        }

        string? sourceRoot = null;
        string? outputRoot = null;
        List<ReviewProxyProfile> profiles = [];
        bool recursive = true;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--non-recursive")
            {
                if (!recursive)
                {
                    throw new ArgumentException("Option '--non-recursive' may be supplied only once.");
                }

                recursive = false;
                continue;
            }

            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--source":
                    sourceRoot = Single(sourceRoot, value, option);
                    break;
                case "--output":
                case "--output-root":
                    outputRoot = Single(outputRoot, value, option);
                    break;
                case "--profile":
                    profiles.Add(ParseProfile(value));
                    break;
                default:
                    throw new ArgumentException($"Unknown archive proxy measure option '{option}'.");
            }
        }

        if (sourceRoot is null)
        {
            throw new ArgumentException("Option '--source' is required for archive proxy measure.");
        }

        if (outputRoot is null)
        {
            throw new ArgumentException("Option '--output' is required for archive proxy measure.");
        }

        if (profiles.Count == 0)
        {
            throw new ArgumentException(
                "Archive proxy measure requires at least one '--profile ID:MAX_LONG_EDGE:JPEG_QUALITY'.");
        }

        string[] duplicateIds = profiles
            .GroupBy(profile => profile.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new ArgumentException(
                $"Proxy profile ids must be unique. Duplicate: {duplicateIds[0]}.");
        }

        return new ArchiveProxyMeasureCommandOptions(
            sourceRoot,
            outputRoot,
            profiles,
            recursive);
    }

    private static ReviewProxyProfile ParseProfile(string value)
    {
        string[] parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int maximumLongEdge) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int jpegQuality))
        {
            throw new ArgumentException(
                "Option '--profile' requires ID:MAX_LONG_EDGE:JPEG_QUALITY, for example jpeg-1600-q82:1600:82.");
        }

        return new ReviewProxyProfile(parts[0], maximumLongEdge, jpegQuality);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"Option '{option}' requires a non-empty value.");
        }

        return trimmed;
    }
}

internal static class ArchiveProxyMeasureCommandRunner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".heic",
        ".heif",
    };

    public static async Task<int> RunAsync(
        ArchiveProxyMeasureCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetFullPath(options.SourceRoot);
        string outputRoot = Path.GetFullPath(options.OutputRoot);
        if (!Directory.Exists(sourceRoot))
        {
            throw new ArgumentException("Archive proxy measurement source directory does not exist.");
        }

        EnsureOutputOutsideSource(sourceRoot, outputRoot);

        EnumerationOptions enumeration = new()
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        string[] sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*", enumeration)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            throw new ArgumentException("Archive proxy measurement source contains no supported JPEG, PNG, HEIC or HEIF images.");
        }

        Directory.CreateDirectory(outputRoot);
        long totalSourceBytes = sourceFiles.Sum(path => new FileInfo(path).Length);
        OpenCvReviewProxyRenderer renderer = new();
        Dictionary<string, List<long>> encodedSizes = options.Profiles.ToDictionary(
            profile => profile.Id,
            _ => new List<long>(sourceFiles.Length),
            StringComparer.Ordinal);

        foreach (string sourcePath in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativeSourcePath = Path.GetRelativePath(sourceRoot, sourcePath);
            foreach (ReviewProxyProfile profile in options.Profiles)
            {
                EncodedReviewProxy encoded = await renderer.RenderAsync(
                    sourcePath,
                    profile,
                    cancellationToken);
                string relativeOutputPath = relativeSourcePath + ".proxy.jpg";
                string destination = Path.Combine(outputRoot, profile.Id, relativeOutputPath);
                await WriteAtomicallyAsync(destination, encoded.Content, cancellationToken);
                encodedSizes[profile.Id].Add(encoded.Content.LongLength);
            }
        }

        output.WriteLine("archive-proxy-measurement: complete");
        output.WriteLine($"source-images: {sourceFiles.Length.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"source-bytes: {totalSourceBytes.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"profiles: {options.Profiles.Count.ToString(CultureInfo.InvariantCulture)}");

        foreach (ReviewProxyProfile profile in options.Profiles)
        {
            IReadOnlyList<long> sizes = encodedSizes[profile.Id];
            long totalProxyBytes = sizes.Sum();
            double mean = sizes.Average(value => (double)value);
            double median = Median(sizes);
            long p95 = PercentileNearestRank(sizes, 0.95d);
            double compressionRatio = totalProxyBytes == 0
                ? 0d
                : (double)totalSourceBytes / totalProxyBytes;

            output.WriteLine($"profile: {profile.Id}");
            output.WriteLine($"settings: {profile.ToDisplayText()}");
            output.WriteLine($"proxy-bytes: {totalProxyBytes.ToString(CultureInfo.InvariantCulture)}");
            output.WriteLine($"mean-proxy-bytes: {mean.ToString("F1", CultureInfo.InvariantCulture)}");
            output.WriteLine($"median-proxy-bytes: {median.ToString("F1", CultureInfo.InvariantCulture)}");
            output.WriteLine($"p95-proxy-bytes: {p95.ToString(CultureInfo.InvariantCulture)}");
            output.WriteLine($"compression-ratio-source-to-proxy: {compressionRatio.ToString("F3", CultureInfo.InvariantCulture)}x");
        }

        return 0;
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static double Median(IReadOnlyList<long> values)
    {
        long[] ordered = values.Order().ToArray();
        int midpoint = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[midpoint]
            : ((double)ordered[midpoint - 1] + ordered[midpoint]) / 2d;
    }

    private static long PercentileNearestRank(IReadOnlyList<long> values, double percentile)
    {
        long[] ordered = values.Order().ToArray();
        int rank = Math.Max(1, (int)Math.Ceiling(percentile * ordered.Length));
        return ordered[rank - 1];
    }

    private static void EnsureOutputOutsideSource(string sourceRoot, string outputRoot)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string sourcePrefix = EnsureTrailingSeparator(sourceRoot);
        if (outputRoot.Equals(sourceRoot, comparison) || outputRoot.StartsWith(sourcePrefix, comparison))
        {
            throw new ArgumentException(
                "Archive proxy output must be outside the source root so derivatives can never become source assets.");
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
