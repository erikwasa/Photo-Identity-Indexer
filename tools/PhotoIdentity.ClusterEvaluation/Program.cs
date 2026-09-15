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
            IReadOnlyList<ProvisionalClusterEvaluationFace> references =
                await repository.ReadConfirmedReferenceSampleAsync(
                    options.ModelId,
                    options.ModelHash,
                    options.MaximumReferences);

            if (sample.Count == 0)
            {
                throw new InvalidOperationException("No reviewed faces with an exact-model embedding matched the requested sample.");
            }
            if (references.Count == 0)
            {
                throw new InvalidOperationException("No confirmed production references matched the requested exact model revision.");
            }

            ReviewIdentitySuggestionPolicy policy = await new PostgresIdentitySuggestionPolicyRepository(database)
                .GetAsync(options.ModelId, options.ModelHash);

            string outputPath = ValidateOutputPath(options.OutputPath);
            if (File.Exists(outputPath) && !options.Force)
            {
                throw new IOException($"Output already exists: {outputPath}. Pass --force to replace it.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            PrivateEvaluationExport export = BuildExport(sample, references, options, policy);
            JsonSerializerOptions jsonOptions = new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(export, jsonOptions));

            Console.WriteLine($"Exported {sample.Count} reviewed targets and {references.Count} confirmed production references to {outputPath}");
            Console.WriteLine($"Target assigned labels: {export.AssignedLabelCount}; Unknown targets: {export.UnknownFaceCount}");
            Console.WriteLine($"Reference labels: {export.ReferenceLabelCount}");
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
        IReadOnlyList<ProvisionalClusterEvaluationFace> references,
        ExportOptions options,
        ReviewIdentitySuggestionPolicy policy)
    {
        ProvisionalClusterEvaluationFace[] allRows = sample.Concat(references).ToArray();
        string[] personIds = allRows
            .Where(face => face.PersonId is not null)
            .Select(face => face.PersonId!.Value.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> labels = personIds
            .Select((personId, index) => (personId, label: $"person-{index + 1:D4}"))
            .ToDictionary(pair => pair.personId, pair => pair.label, StringComparer.Ordinal);

        string[] faceIds = allRows
            .Select(face => face.FaceOccurrenceId.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> faceLabels = faceIds
            .Select((faceId, index) => (faceId, label: $"face-{index + 1:D6}"))
            .ToDictionary(pair => pair.faceId, pair => pair.label, StringComparer.Ordinal);

        string[] photoIds = allRows
            .Select(face => face.AssetRevisionId.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> photoGroups = photoIds
            .Select((assetRevisionId, index) => (assetRevisionId, label: $"photo-{index + 1:D6}"))
            .ToDictionary(pair => pair.assetRevisionId, pair => pair.label, StringComparer.Ordinal);

        string[] contentHashes = allRows
            .Select(face => face.ContentHash.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> contentGroups = contentHashes
            .Select((contentHash, index) => (contentHash, label: $"content-{index + 1:D6}"))
            .ToDictionary(pair => pair.contentHash, pair => pair.label, StringComparer.Ordinal);

        IReadOnlyList<PrivateEvaluationFace> faces = ProjectRows(
            sample, labels, faceLabels, photoGroups, contentGroups);
        IReadOnlyList<PrivateEvaluationFace> referenceFaces = ProjectRows(
            references, labels, faceLabels, photoGroups, contentGroups);
        int unknownCount = sample.Count(face => face.PersonId is null);
        int assignedLabelCount = sample
            .Where(face => face.PersonId is not null)
            .Select(face => face.PersonId!.Value.ToString())
            .Distinct(StringComparer.Ordinal)
            .Count();
        int referenceLabelCount = references
            .Select(face => face.PersonId?.ToString())
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .Count();

        // Schema 1 is intentionally retained because all WI-0081 additions are optional/additive;
        // the accepted WI-0113/WI-0116 evaluators continue to consume only the existing Faces field.
        return new PrivateEvaluationExport(
            1,
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
                policy.UpdatedAtUtc),
            new PrivateSampleSelection(
                options.MaximumFaces,
                options.MaximumReferences,
                options.IncludeUnknown,
                "face-occurrence-id-ascending"),
            faces.Count,
            assignedLabelCount,
            unknownCount,
            referenceFaces.Count,
            referenceLabelCount,
            faces,
            referenceFaces);
    }

    private static IReadOnlyList<PrivateEvaluationFace> ProjectRows(
        IReadOnlyList<ProvisionalClusterEvaluationFace> rows,
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, string> faceLabels,
        IReadOnlyDictionary<string, string> photoGroups,
        IReadOnlyDictionary<string, string> contentGroups)
    {
        return rows.Select(face => new PrivateEvaluationFace(
            faceLabels[face.FaceOccurrenceId.ToString()],
            photoGroups[face.AssetRevisionId.ToString()],
            contentGroups[face.ContentHash.ToString()],
            face.ReviewState,
            face.PersonId is null ? null : labels[face.PersonId.Value.ToString()],
            face.Embedding.ToArray(),
            face.DetectorConfidence,
            face.FaceAreaFraction,
            face.ReviewedAtUtc,
            face.ReviewedPersonHasMergeHistory)).ToArray();
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
        int maximumFaces = ParseBound(values, "--max-faces", 5000);
        int maximumReferences = ParseBound(values, "--max-references", 20000);

        return new ExportOptions(
            new ModelId(modelId.Trim()),
            new Sha256Digest(modelHash.Trim().ToLowerInvariant()),
            maximumFaces,
            maximumReferences,
            includeUnknown,
            output,
            force);
    }

    private static int ParseBound(
        IReadOnlyDictionary<string, string?> values,
        string key,
        int defaultValue) =>
        values.TryGetValue(key, out string? text)
            ? int.Parse(text!, System.Globalization.CultureInfo.InvariantCulture)
            : defaultValue;

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
        Console.WriteLine("    --model-id <id> --model-hash <sha256> [--max-faces 5000] [--max-references 20000]");
        Console.WriteLine("    [--output private/cluster-evaluation/sample.json] [--exclude-unknown] [--force]");
        Console.WriteLine();
        Console.WriteLine("Faces remains the backward-compatible reviewed target sample. referenceFaces separately carries");
        Console.WriteLine("the exact production confirmed-reference population, including legacy confirmed labels.");
        Console.WriteLine("The export also includes suggestion policy, detector confidence, normalized face area, review time");
        Console.WriteLine("and merge-history audit metadata. No names, actors or source paths are exported.");
        Console.WriteLine($"Connection string is read only from {ConnectionStringEnvironmentVariable}.");
    }

    private sealed record ExportOptions(
        ModelId ModelId,
        Sha256Digest ModelHash,
        int MaximumFaces,
        int MaximumReferences,
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
        int ReferenceFaceCount,
        int ReferenceLabelCount,
        IReadOnlyList<PrivateEvaluationFace> Faces,
        IReadOnlyList<PrivateEvaluationFace> ReferenceFaces);

    private sealed record PrivateSuggestionPolicy(
        int Version,
        bool AutoAssignEnabled,
        double HighScoreThreshold,
        double HighMarginThreshold,
        double MediumScoreThreshold,
        DateTimeOffset UpdatedAtUtc);

    private sealed record PrivateSampleSelection(
        int MaximumFaces,
        int MaximumReferences,
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
        bool ReviewedPersonHasMergeHistory);
}
