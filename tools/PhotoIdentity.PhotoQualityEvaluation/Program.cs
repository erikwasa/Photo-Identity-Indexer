using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class Program
{
    private const string ConnectionStringEnvironmentVariable = "PhotoIdentity__Postgres__ConnectionString";
    private const string ProxyRootEnvironmentVariable = "PhotoIdentity__ReviewProxyRoot";
    private const string ProxyProfileEnvironmentVariable = "PhotoIdentity__ReviewProxyProfileId";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Options options = Parse(args);
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
                throw new DirectoryNotFoundException($"Review-proxy root does not exist: {proxyRoot}");
            }

            string outputPath = ValidateOutputPath(options.OutputPath);
            if (File.Exists(outputPath) && !options.Force)
            {
                throw new IOException($"Output already exists: {outputPath}. Pass --force to replace it.");
            }

            await using PostgresCatalogueDatabase database = new(connectionString);
            PostgresArchiveReviewProxyRepository proxyRepository = new(database);
            ReviewProxyProfile profile = await proxyRepository.GetProfileAsync(profileId)
                ?? throw new InvalidOperationException($"Review proxy profile '{profileId}' is not registered in the catalogue.");
            PostgresPhotoQualityEvaluationRepository evaluationRepository = new(database);
            IReadOnlyList<PhotoQualityEvaluationCandidate> candidates =
                await evaluationRepository.ReadCurrentProxySampleAsync(profile.Id, options.Limit);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No current archive revisions have durable review proxies for profile '{profile.Id}'.");
            }

            OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();
            List<PhotoQualityEvaluationPhoto> photos = [];
            long evidenceJsonBytes = 0;
            Stopwatch total = Stopwatch.StartNew();
            foreach (PhotoQualityEvaluationCandidate candidate in candidates)
            {
                string proxyPath = ResolveProxyPath(proxyRoot, candidate.Proxy.RelativePath);
                byte[] content = await File.ReadAllBytesAsync(proxyPath);
                VerifyProxy(candidate, content);

                Stopwatch analysis = Stopwatch.StartNew();
                TechnicalPhotoQualityEvidence evidence = analyzer.Analyze(content, profile);
                analysis.Stop();
                evidenceJsonBytes += JsonSerializer.SerializeToUtf8Bytes(evidence, JsonOptions).LongLength;

                photos.Add(new PhotoQualityEvaluationPhoto(
                    candidate.AssetRevisionId.ToString(),
                    candidate.SourceKey,
                    candidate.Proxy.RelativePath,
                    candidate.Proxy.ContentHash.ToString(),
                    analysis.Elapsed.TotalMilliseconds,
                    evidence,
                    new HumanReviewDraft(null, null, null, null)));
            }
            total.Stop();

            double[] analysisTimes = photos
                .Select(photo => photo.AnalysisMilliseconds)
                .OrderBy(value => value)
                .ToArray();
            PhotoQualityEvaluationExport export = new(
                SchemaVersion: 1,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                AlgorithmVersion: TechnicalPhotoQualityProtocol.AlgorithmVersion,
                CalibrationVersion: photos[0].Evidence.CalibrationVersion,
                ProxyProfile: profile.ToCanonicalText(),
                SelectionPolicy: "current-archive-revisions-with-durable-proxy; stable-md5-revision-order",
                RequestedLimit: options.Limit,
                PhotoCount: photos.Count,
                Runtime: new PhotoQualityRuntimeSummary(
                    total.Elapsed.TotalMilliseconds,
                    Percentile(analysisTimes, 0.50d),
                    Percentile(analysisTimes, 0.95d),
                    evidenceJsonBytes / (double)photos.Count),
                ReviewInstructions:
                    "Review representative good photos, blur/focus failures, low-light/noisy photos, exposure failures, scans and intentionally imperfect but personally important photos. Fill review.category, review.verdict (acceptable|technical-failure|uncertain), review.personallyImportant and review.notes. Candidate reasons are experimental evidence, not deletion/hiding rules.",
                Photos: photos);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(export, JsonOptions));

            Console.WriteLine($"Evaluated {photos.Count} durable review proxies using {export.AlgorithmVersion}.");
            Console.WriteLine($"Profile: {profile.ToDisplayText()}");
            Console.WriteLine($"Median analyzer time: {export.Runtime.MedianAnalysisMilliseconds:F2} ms; p95: {export.Runtime.P95AnalysisMilliseconds:F2} ms.");
            Console.WriteLine($"Average serialized evidence payload: {export.Runtime.AverageEvidenceJsonBytes:F0} bytes/photo.");
            Console.WriteLine($"Private review export: {outputPath}");
            Console.WriteLine("Do not commit the export: it contains private archive paths and human review notes.");
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

    private static void VerifyProxy(PhotoQualityEvaluationCandidate candidate, byte[] content)
    {
        if (content.LongLength != candidate.Proxy.EncodedByteLength)
        {
            throw new InvalidDataException(
                $"Stored proxy length does not match catalogue metadata for revision {candidate.AssetRevisionId}.");
        }

        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(hash, candidate.Proxy.ContentHash.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Stored proxy hash does not match catalogue metadata for revision {candidate.AssetRevisionId}.");
        }
    }

    private static string ResolveProxyPath(string proxyRoot, string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(proxyRoot, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = proxyRoot.EndsWith(Path.DirectorySeparatorChar)
            ? proxyRoot
            : proxyRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException("A stored review-proxy path escapes the configured derivative root.");
        }

        return path;
    }

    private static string ValidateOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An output path is required. Pass --output <path>.", nameof(outputPath));
        }

        if (!Path.IsPathRooted(outputPath))
        {
            string normalized = outputPath.Replace('\\', '/').TrimStart('.', '/');
            bool protectedByRepositoryIgnore =
                normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("staging/", StringComparison.OrdinalIgnoreCase);
            if (!protectedByRepositoryIgnore)
            {
                throw new ArgumentException(
                    "Relative evaluation exports must be written under private/, data/, or staging/ because they contain private archive paths.",
                    nameof(outputPath));
            }
        }

        return Path.GetFullPath(outputPath);
    }

    private static string RequiredSetting(string? optionValue, string environmentVariable, string description)
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

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0d;
        }

        int index = Math.Clamp(
            (int)Math.Ceiling(sortedValues.Length * percentile) - 1,
            0,
            sortedValues.Length - 1);
        return sortedValues[index];
    }

    private static Options Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
        {
            PrintUsage();
            Environment.Exit(0);
        }

        string? output = null;
        string? proxyRoot = null;
        string? profile = null;
        string? connectionString = null;
        int limit = 250;
        bool force = false;
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--output":
                    output = NextValue(args, ref index, arg);
                    break;
                case "--proxy-root":
                    proxyRoot = NextValue(args, ref index, arg);
                    break;
                case "--profile":
                    profile = NextValue(args, ref index, arg);
                    break;
                case "--connection-string":
                    connectionString = NextValue(args, ref index, arg);
                    break;
                case "--limit":
                    string limitText = NextValue(args, ref index, arg);
                    if (!int.TryParse(limitText, out limit) || limit is <= 0 or > 5000)
                    {
                        throw new ArgumentException("--limit must be an integer between 1 and 5000.");
                    }
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new Options(output ?? string.Empty, proxyRoot, profile, connectionString, limit, force);
    }

    private static string NextValue(string[] args, ref int index, string option)
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
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- --output private/photo-quality-evaluation.json [--limit 250] [--profile <id>] [--proxy-root <path>] [--connection-string <value>] [--force]");
        Console.WriteLine();
        Console.WriteLine($"Defaults can come from {ConnectionStringEnvironmentVariable}, {ProxyRootEnvironmentVariable}, and {ProxyProfileEnvironmentVariable}.");
    }

    private sealed record Options(
        string OutputPath,
        string? ProxyRoot,
        string? ProfileId,
        string? ConnectionString,
        int Limit,
        bool Force);
}

internal sealed record PhotoQualityEvaluationExport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string AlgorithmVersion,
    string CalibrationVersion,
    string ProxyProfile,
    string SelectionPolicy,
    int RequestedLimit,
    int PhotoCount,
    PhotoQualityRuntimeSummary Runtime,
    string ReviewInstructions,
    IReadOnlyList<PhotoQualityEvaluationPhoto> Photos);

internal sealed record PhotoQualityRuntimeSummary(
    double TotalElapsedMilliseconds,
    double MedianAnalysisMilliseconds,
    double P95AnalysisMilliseconds,
    double AverageEvidenceJsonBytes);

internal sealed record PhotoQualityEvaluationPhoto(
    string RevisionId,
    string SourceKey,
    string ProxyRelativePath,
    string ProxySha256,
    double AnalysisMilliseconds,
    TechnicalPhotoQualityEvidence Evidence,
    HumanReviewDraft Review);

internal sealed record HumanReviewDraft(
    string? Category,
    string? Verdict,
    bool? PersonallyImportant,
    string? Notes);
