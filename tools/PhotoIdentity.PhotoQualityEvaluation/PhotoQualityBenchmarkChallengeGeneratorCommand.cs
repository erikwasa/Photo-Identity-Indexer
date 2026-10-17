using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityBenchmarkChallengeGeneratorCommand
{
    private const string ConnectionStringEnvironmentVariable =
        "PhotoIdentity__Postgres__ConnectionString";
    private const string ProxyRootEnvironmentVariable =
        "PhotoIdentity__ReviewProxyRoot";
    private const string ProxyProfileEnvironmentVariable =
        "PhotoIdentity__ReviewProxyProfileId";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            Options options = Parse(args);
            PhotoQualityBenchmarkExport benchmark =
                await PhotoQualityBenchmarkFile.LoadAsync(options.InputPath);
            int available = benchmark.ChallengeReserve - benchmark.ChallengeCount;
            if (available <= 0)
            {
                throw new InvalidOperationException(
                    "The benchmark has no remaining challenge capacity.");
            }

            if (options.PoolOnly && options.Count > 500)
            {
                throw new ArgumentException(
                    "Disagreement candidate pool is capped at 500 photos to bound model scoring.");
            }
            int requestedCount = options.PoolOnly
                ? options.Count
                : Math.Min(options.Count, available);
            string connectionString = RequiredSetting(
                options.ConnectionString,
                ConnectionStringEnvironmentVariable,
                "PostgreSQL catalogue connection string");
            string proxyRoot = Path.GetFullPath(RequiredSetting(
                options.ProxyRoot,
                ProxyRootEnvironmentVariable,
                "review-proxy derivative root"));
            string profileId = RequiredSetting(
                options.ProfileId,
                ProxyProfileEnvironmentVariable,
                "review-proxy profile id");
            if (!Directory.Exists(proxyRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Review-proxy root does not exist: {proxyRoot}");
            }

            await using PostgresCatalogueDatabase database =
                new(connectionString);
            PostgresArchiveReviewProxyRepository proxyRepository = new(database);
            ReviewProxyProfile profile =
                await proxyRepository.GetProfileAsync(profileId)
                ?? throw new InvalidOperationException(
                    $"Review proxy profile '{profileId}' is not registered in the catalogue.");
            if (!string.Equals(
                    benchmark.ProxyProfile,
                    profile.ToCanonicalText(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The configured review-proxy profile does not match the frozen benchmark profile.");
            }

            PostgresPhotoQualityEvaluationRepository repository = new(database);
            IReadOnlyList<PhotoQualityBenchmarkPopulationRow> population =
                await repository.ReadCurrentProxyPopulationAsync(profile.Id);
            HashSet<string> excludedRevisionIds = benchmark.Photos
                .Select(photo => photo.RevisionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> excludedContentHashes = benchmark.Photos
                .Select(photo => photo.ContentSha256)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            PhotoQualityBenchmarkPopulationRow[] eligible = population
                .Where(row =>
                    !excludedRevisionIds.Contains(row.AssetRevisionId.ToString()) &&
                    !excludedContentHashes.Contains(row.ContentHash.ToString()))
                .ToArray();
            if (eligible.Length == 0)
            {
                throw new InvalidOperationException(
                    "No non-benchmark current photos remain eligible for challenge generation.");
            }

            OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();
            List<AnalyzedRow> analyzed = new(eligible.Length);
            for (int index = 0; index < eligible.Length; index++)
            {
                PhotoQualityBenchmarkPopulationRow row = eligible[index];
                string proxyPath = PhotoQualityBenchmarkFile.ResolveProxyPath(
                    proxyRoot,
                    row.Proxy.RelativePath);
                byte[] content = await File.ReadAllBytesAsync(proxyPath);
                VerifyProxy(row, content);
                TechnicalPhotoQualityEvidence evidence =
                    analyzer.Analyze(content, profile);
                PhotoQualityBenchmarkStratum stratum =
                    PhotoQualityBenchmarkSelector.DescribeStratum(
                        ToDescriptor(row));
                analyzed.Add(new AnalyzedRow(row, stratum, evidence));

                if ((index + 1) % 500 == 0 || index + 1 == eligible.Length)
                {
                    Console.WriteLine(
                        $"Analyzed {index + 1}/{eligible.Length} non-core proxies...");
                }
            }

            PhotoQualityChallengeThresholds thresholds =
                PhotoQualityBenchmarkChallengeSelector.CalculateThresholds(
                    analyzed.Select(item => item.Evidence));
            PhotoQualityChallengeRarity rarity =
                PhotoQualityBenchmarkChallengeSelector.CalculateRarity(
                    analyzed.Select(item => item.Stratum));

            PhotoQualityChallengeAnalyzedCandidate[] candidates = analyzed
                .Select(item =>
                {
                    IReadOnlyList<string> reasons =
                        PhotoQualityBenchmarkChallengeSelector.BuildReasons(
                            item.Evidence,
                            item.Stratum,
                            thresholds,
                            rarity);
                    return new PhotoQualityChallengeAnalyzedCandidate(
                        ToChallengePhoto(item.Row, item.Stratum, reasons),
                        item.Evidence,
                        reasons,
                        PhotoQualityBenchmarkChallengeSelector.StableOrderKey(
                            benchmark.BenchmarkId,
                            item.Row.AssetRevisionId.ToString()));
                })
                .Where(candidate => candidate.Reasons.Count > 0)
                .ToArray();

            // A screening pool must not contain the same original content twice.
            // Keep the deterministically earliest member of each content group.
            IEnumerable<PhotoQualityChallengeAnalyzedCandidate> selectionInput =
                options.PoolOnly
                    ? candidates
                        .GroupBy(candidate => candidate.Photo.ContentSha256,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.OrderBy(candidate => candidate.StableOrderKey,
                            StringComparer.Ordinal).First())
                    : candidates;
            IReadOnlyList<PhotoQualityChallengeAnalyzedCandidate> selected =
                PhotoQualityBenchmarkChallengeSelector.Select(
                    selectionInput,
                    requestedCount);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException(
                    "Challenge heuristics did not produce any candidates.");
            }

            // Pool-only artifacts are intentionally too large for the remaining
            // challenge reserve. They must be scored and filtered first; never
            // pass them directly to benchmark add-challenges.
            PhotoQualityBenchmarkChallengeFile output =
                new(
                    SchemaVersion: 1,
                    BenchmarkId: benchmark.BenchmarkId,
                    SelectionPolicy: options.PoolOnly
                        ? "m37-disagreement-screening-pool-v1"
                        : PhotoQualityBenchmarkChallengeSelector.SelectionPolicyVersion,
                    Photos: selected
                        .Select(candidate => candidate.Photo)
                        .ToArray());

            string outputPath = ValidatePrivateOutputPath(options.OutputPath);
            if (File.Exists(outputPath) && !options.Force)
            {
                throw new IOException(
                    $"Output already exists: {outputPath}. Pass --force to replace it.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(
                    output,
                    PhotoQualityBenchmarkFile.JsonOptions));

            Console.WriteLine();
            Console.WriteLine(
                $"Selected {selected.Count} challenge candidates from {eligible.Length} non-core current photos.");
            if (options.PoolOnly)
            {
                Console.WriteLine(
                    $"Disagreement screening pool only: selected {selected.Count} non-benchmark photos. Do not import this unfiltered pool.");
                Console.WriteLine(
                    $"Reserved challenge slots remain {available}; use score-disagreement-pool and select-disagreements first.");
            }
            else
            {
                Console.WriteLine(
                    $"Requested {requestedCount}; remaining benchmark challenge capacity after import would be {available - selected.Count}.");
            }
            Console.WriteLine("Challenge reason counts:");
            foreach (IGrouping<string, PhotoQualityChallengeAnalyzedCandidate> group in
                     selected
                         .SelectMany(candidate =>
                             candidate.Reasons.Select(reason => (reason, candidate)))
                         .GroupBy(pair => pair.reason, pair => pair.candidate)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {group.Key}: {group.Count()}");
            }

            Console.WriteLine($"Private challenge export: {outputPath}");
            Console.WriteLine(
                "Do not commit it: it contains private archive source keys and proxy paths.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }
    }

    private static PhotoQualityBenchmarkPhoto ToChallengePhoto(
        PhotoQualityBenchmarkPopulationRow row,
        PhotoQualityBenchmarkStratum stratum,
        IReadOnlyList<string> reasons) =>
        new(
            row.AssetRevisionId.ToString(),
            row.SourceKey,
            row.ContentHash.ToString(),
            row.Proxy.RelativePath,
            row.Proxy.ContentHash.ToString(),
            row.MediaType,
            row.Width,
            row.Height,
            row.TakenAtLocal,
            row.CameraMake,
            row.CameraModel,
            row.FaceCount,
            stratum,
            "challenge",
            "challenge",
            reasons,
            new PhotoQualityBenchmarkHumanReview(null, null, null));

    private static PhotoQualityBenchmarkDescriptor ToDescriptor(
        PhotoQualityBenchmarkPopulationRow row) =>
        new(
            row.AssetRevisionId,
            row.ContentHash.ToString(),
            row.SourceKey,
            row.MediaType,
            row.Width,
            row.Height,
            row.TakenAtLocal,
            row.CameraMake,
            row.CameraModel,
            row.FaceCount);

    private static void VerifyProxy(
        PhotoQualityBenchmarkPopulationRow row,
        byte[] content)
    {
        if (content.LongLength != row.Proxy.EncodedByteLength)
        {
            throw new InvalidDataException(
                $"Stored proxy length does not match catalogue metadata for revision {row.AssetRevisionId}.");
        }

        string hash = Convert.ToHexString(
            SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(
                hash,
                row.Proxy.ContentHash.ToString(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Stored proxy hash does not match catalogue metadata for revision {row.AssetRevisionId}.");
        }
    }

    private static string ValidatePrivateOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("--output is required.");
        }

        if (!Path.IsPathRooted(outputPath))
        {
            string normalized = outputPath
                .Replace('\\', '/')
                .TrimStart('.', '/');
            bool allowed =
                normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("staging/", StringComparison.OrdinalIgnoreCase);
            if (!allowed)
            {
                throw new ArgumentException(
                    "Relative challenge exports must be written under private/, data/, or staging/.");
            }
        }

        return Path.GetFullPath(outputPath);
    }

    private static string RequiredSetting(
        string? optionValue,
        string environmentVariable,
        string description)
    {
        string? value = string.IsNullOrWhiteSpace(optionValue)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : optionValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Provide the {description} with the matching command option or set {environmentVariable}.");
        }

        return value.Trim();
    }

    private static Options Parse(string[] args)
    {
        string input = string.Empty;
        string output = string.Empty;
        string? proxyRoot = null;
        string? profile = null;
        string? connectionString = null;
        int count = 180;
        bool force = false;
        bool poolOnly = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    input = NextValue(args, ref index, "--input");
                    break;
                case "--output":
                    output = NextValue(args, ref index, "--output");
                    break;
                case "--proxy-root":
                    proxyRoot = NextValue(args, ref index, "--proxy-root");
                    break;
                case "--profile":
                    profile = NextValue(args, ref index, "--profile");
                    break;
                case "--connection-string":
                    connectionString =
                        NextValue(args, ref index, "--connection-string");
                    break;
                case "--count":
                    if (!int.TryParse(
                            NextValue(args, ref index, "--count"),
                            out count) ||
                        count is <= 0 or > 5000)
                    {
                        throw new ArgumentException(
                            "--count must be an integer between 1 and 5000.");
                    }
                    break;
                case "--pool-only":
                    poolOnly = true;
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown generate-challenges argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("--input is required.");
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("--output is required.");
        }

        return new Options(
            Path.GetFullPath(input),
            output,
            proxyRoot,
            profile,
            connectionString,
            count,
            force,
            poolOnly);
    }

    private static string NextValue(
        string[] args,
        ref int index,
        string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark generate-challenges --input private/photo-quality-benchmark.json --output private/photo-quality-challenges.json [--count 180] [--pool-only] [--profile <id>] [--proxy-root <path>] [--connection-string <value>] [--force]");
    }

    private sealed record Options(
        string InputPath,
        string OutputPath,
        string? ProxyRoot,
        string? ProfileId,
        string? ConnectionString,
        int Count,
        bool Force,
        bool PoolOnly);

    private sealed record AnalyzedRow(
        PhotoQualityBenchmarkPopulationRow Row,
        PhotoQualityBenchmarkStratum Stratum,
        TechnicalPhotoQualityEvidence Evidence);
}
