using System.Text.Json;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.ClusterEvaluation;

internal static class Program
{
    private const string ConnectionStringEnvironmentVariable = "PhotoIdentity__Postgres__ConnectionString";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            ExportOptions options = Parse(args);
            string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {ConnectionStringEnvironmentVariable} to the local PostgreSQL catalogue connection string before exporting.");
            }

            await using PostgresCatalogueDatabase database = new(connectionString);
            PostgresProvisionalClusterEvaluationRepository repository = new(database);
            IReadOnlyList<ProvisionalClusterEvaluationFace> sample =
                await repository.ReadReviewedSampleAsync(
                    options.ModelId,
                    options.ModelHash,
                    options.MaximumFaces,
                    options.IncludeUnknown);

            if (sample.Count == 0)
            {
                throw new InvalidOperationException("No reviewed faces with an exact-model embedding matched the requested sample.");
            }

            ReviewIdentitySuggestionPolicy policy = await new PostgresIdentitySuggestionPolicyRepository(database)
                .GetAsync(options.ModelId, options.ModelHash);

            string outputPath = ValidateOutputPath(options.OutputPath);
            if (File.Exists(outputPath) && !options.Force)
            {
                throw new IOException($"Output already exists: {outputPath}. Pass --force to replace it.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            PrivateEvaluationExport export = BuildExport(sample, options, policy);
            JsonSerializerOptions jsonOptions = new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(export, jsonOptions));

            Console.WriteLine($"Exported {sample.Count} reviewed faces to {outputPath}");
            Console.WriteLine($"Assigned labels: {export.AssignedLabelCount}; Unknown faces: {export.UnknownFaceCount}");
            Console.WriteLine(
                $"Suggestion policy v{export.SuggestionPolicy.Version}: High={export.SuggestionPolicy.HighScoreThreshold:F2} " +
                $"margin={export.SuggestionPolicy.HighMarginThreshold:F2}; Medium={export.SuggestionPolicy.MediumScoreThreshold:F2}");
            Console.WriteLine("The export contains biometric embeddings. Keep it private and do not commit it.");
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

    private static PrivateEvaluationExport BuildExport(
        IReadOnlyList<ProvisionalClusterEvaluationFace> sample,
        ExportOptions options,
        ReviewIdentitySuggestionPolicy policy)
    {
        string[] personIds = sample
            .Where(face => face.PersonId is not null)
            .Select(face => face.PersonId!.Value.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> labels = personIds
            .Select((personId, index) => (personId, label: $"person-{index + 1:D4}"))
            .ToDictionary(pair => pair.personId, pair => pair.label, StringComparer.Ordinal);

        string[] photoIds = sample
            .Select(face => face.AssetRevisionId.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> photoGroups = photoIds
            .Select((assetRevisionId, index) => (assetRevisionId, label: $"photo-{index + 1:D6}"))
            .ToDictionary(pair => pair.assetRevisionId, pair => pair.label, StringComparer.Ordinal);

        string[] contentHashes = sample
            .Select(face => face.ContentHash.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> contentGroups = contentHashes
            .Select((contentHash, index) => (contentHash, label: $"content-{index + 1:D6}"))
            .ToDictionary(pair => pair.contentHash, pair => pair.label, StringComparer.Ordinal);

        List<PrivateEvaluationFace> faces = new(sample.Count);
        int unknownCount = 0;
        for (int index = 0; index < sample.Count; index++)
        {
            ProvisionalClusterEvaluationFace face = sample[index];
            string? label = null;
            if (face.PersonId is not null)
            {
                label = labels[face.PersonId.Value.ToString()];
            }
            else
            {
                unknownCount++;
            }

            faces.Add(new PrivateEvaluationFace(
                $"face-{index + 1:D6}",
                photoGroups[face.AssetRevisionId.ToString()],
                contentGroups[face.ContentHash.ToString()],
                face.ReviewState,
                label,
                face.Embedding.ToArray(),
                face.DetectorConfidence,
                face.FaceAreaFraction,
                face.ReviewedAtUtc,
                face.ReviewedPersonWasMerged));
        }

        return new PrivateEvaluationExport(
            2,
            options.ModelId.ToString(),
            options.ModelHash.ToString(),
            DateTimeOffset.UtcNow,
            ProvisionalFaceClusterSemantics.CanonicalDefinition,
            new PrivateSuggestionPolicy(
                policy.Version,
                policy.AutoAssignEnabled,
                policy.HighScoreThreshold,
                policy.HighMarginThreshold,
                policy.MediumScoreThreshold,
                policy.UpdatedBy,
                policy.UpdatedAtUtc),
            new PrivateSampleSelection(
                options.MaximumFaces,
                options.IncludeUnknown,
                "face-occurrence-id-ascending"),
            faces.Count,
            labels.Count,
            unknownCount,
            faces);
    }

    private static string ValidateOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An output path is required.", nameof(outputPath));
        }

        if (!Path.IsPathRooted(outputPath))
        {
            string normalized = outputPath.Replace('\\', '/').TrimStart('.', '/');
            bool underIgnoredPrivateRoot =
                normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("staging/", StringComparison.OrdinalIgnoreCase);
            if (!underIgnoredPrivateRoot)
            {
                throw new ArgumentException(
                    "Relative evaluation exports must be written under private/, data/, or staging/ so repository ignore rules protect biometric data.",
                    nameof(outputPath));
            }
        }

        return Path.GetFullPath(outputPath);
    }

    private static ExportOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
        {
            PrintUsage();
            Environment.Exit(0);
        }

        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
        bool force = false;
        bool includeUnknown = true;
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg == "--force")
            {
                force = true;
                continue;
            }
            if (arg == "--exclude-unknown")
            {
                includeUnknown = false;
                continue;
            }
            if (!arg.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"Unknown or incomplete argument '{arg}'.");
            }

            values[arg] = args[++index];
        }

        string modelId = Required(values, "--model-id");
        string modelHash = Required(values, "--model-hash");
        string output = values.GetValueOrDefault("--output") ?? "private/cluster-evaluation/sample.json";
        int maximumFaces = values.TryGetValue("--max-faces", out string? maximumFacesText)
            ? int.Parse(maximumFacesText!, System.Globalization.CultureInfo.InvariantCulture)
            : 5000;

        return new ExportOptions(
            new ModelId(modelId.Trim()),
            new Sha256Digest(modelHash.Trim().ToLowerInvariant()),
            maximumFaces,
            includeUnknown,
            output,
            force);
    }

    private static string Required(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument {key}.");

    private static void PrintUsage()
    {
        Console.WriteLine("PhotoIdentity.ClusterEvaluation - private reviewed-sample exporter");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/PhotoIdentity.ClusterEvaluation --");
        Console.WriteLine("    --model-id <id> --model-hash <sha256> [--max-faces 5000]");
        Console.WriteLine("    [--output private/cluster-evaluation/sample.json] [--exclude-unknown] [--force]");
        Console.WriteLine();
        Console.WriteLine("Schema v2 includes the exact suggestion policy plus detector confidence, normalized face area,");
        Console.WriteLine("review time and merged-person audit metadata for WI-0081. No names or source paths are exported.");
        Console.WriteLine($"Connection string is read only from {ConnectionStringEnvironmentVariable}.");
    }

    private sealed record ExportOptions(
        ModelId ModelId,
        Sha256Digest ModelHash,
        int MaximumFaces,
        bool IncludeUnknown,
        string OutputPath,
        bool Force);

    private sealed record PrivateEvaluationExport(
        int SchemaVersion,
        string ModelId,
        string ModelHash,
        DateTimeOffset GeneratedAtUtc,
        string ClusterContract,
        PrivateSuggestionPolicy SuggestionPolicy,
        PrivateSampleSelection SampleSelection,
        int FaceCount,
        int AssignedLabelCount,
        int UnknownFaceCount,
        IReadOnlyList<PrivateEvaluationFace> Faces);

    private sealed record PrivateSuggestionPolicy(
        int Version,
        bool AutoAssignEnabled,
        double HighScoreThreshold,
        double HighMarginThreshold,
        double MediumScoreThreshold,
        string UpdatedBy,
        DateTimeOffset UpdatedAtUtc);

    private sealed record PrivateSampleSelection(
        int MaximumFaces,
        bool IncludeUnknown,
        string Ordering);

    private sealed record PrivateEvaluationFace(
        string Id,
        string PhotoGroup,
        string ContentGroup,
        string ReviewState,
        string? GroundTruthLabel,
        float[] Embedding,
        double? DetectorConfidence,
        double? FaceAreaFraction,
        DateTimeOffset? ReviewedAtUtc,
        bool ReviewedPersonWasMerged);
}
