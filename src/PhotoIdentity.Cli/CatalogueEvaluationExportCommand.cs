using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal static class CatalogueEvaluationExportCommandRunner
{
    private const string SplitPolicy = "source-revision-grouped-seeded-sha256-v1";
    private const string TimingPolicy = "processing-job-duration-per-exported-face-with-1ms-fallback-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(
        CatalogueEvaluationExportCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string databasePath = Path.GetFullPath(options.DatabasePath);
        if (!File.Exists(databasePath))
        {
            error.WriteLine("error: catalogue database does not exist");
            return 2;
        }

        string outputPath = Path.GetFullPath(options.OutputPath);
        if (string.Equals(databasePath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine("error: evaluation export cannot overwrite the catalogue database");
            return 2;
        }

        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync(cancellationToken);
            CatalogueEvaluationScope scope = options.CreateScope();
            CatalogueEvaluationExportInput input = await new SqliteCatalogueEvaluationExportRepository(database)
                .LoadAsync(
                    scope,
                    new ModelId(options.DetectorModelId),
                    new Sha256Digest(options.DetectorModelHash),
                    new ModelId(options.EmbedderModelId),
                    new Sha256Digest(options.EmbedderModelHash),
                    cancellationToken);
            CatalogueEvaluationDatasetManifest manifest = BuildManifest(input, options);
            string json = JsonSerializer.Serialize(manifest, JsonOptions)
                .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
            await WriteAtomicAsync(outputPath, json, cancellationToken);

            output.WriteLine($"dataset: {manifest.DatasetId}");
            output.WriteLine($"scope: {manifest.CatalogueExport.Scope.Kind}");
            output.WriteLine($"source-revisions: {manifest.CatalogueExport.SourceRevisions.Count}");
            output.WriteLine($"gallery: {manifest.Gallery.Count}");
            output.WriteLine($"validation: {manifest.Validation.Count}");
            output.WriteLine($"test: {manifest.Test.Count}");
            output.WriteLine($"catalogue-input-sha256: {manifest.CatalogueExport.CatalogueInputSha256}");
            if (manifest.CatalogueExport.FallbackTimingSampleCount > 0)
            {
                output.WriteLine(
                    $"warning: {manifest.CatalogueExport.FallbackTimingSampleCount} exported sample(s) use the deterministic 1 ms timing fallback because processing-job timing was unavailable");
            }
            output.WriteLine($"manifest: {outputPath}");
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or KeyNotFoundException or InvalidDataException)
        {
            error.WriteLine($"error: catalogue evaluation export failed: {exception.Message}");
            return 2;
        }
    }

    private static CatalogueEvaluationDatasetManifest BuildManifest(
        CatalogueEvaluationExportInput input,
        CatalogueEvaluationExportCommandOptions options)
    {
        CatalogueEvaluationSplitPlan plan = CatalogueEvaluationSplitPlanner.Create(
            input,
            new CatalogueEvaluationSplitOptions(
                options.Seed,
                options.GalleryPerPerson,
                options.ValidationKnownPerPerson,
                options.TestKnownPerPerson,
                options.ValidationUnknownCount,
                options.TestUnknownCount));
        CatalogueEvaluationGalleryItem[] gallery = plan.Gallery
            .Select(face => new CatalogueEvaluationGalleryItem(
                face.Face.Id.ToString(),
                face.Face.AssetRevisionId.ToString(),
                face.Face.PersonId.ToString(),
                face.Face.Embedding))
            .OrderBy(item => item.PersonId, StringComparer.Ordinal)
            .ThenBy(item => item.FaceId, StringComparer.Ordinal)
            .ToArray();
        CatalogueEvaluationSample[] validation = plan.ValidationKnown
            .Select(CreateKnownSample)
            .Concat(plan.ValidationUnknown.Select(CreateUnknownSample))
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .ToArray();
        CatalogueEvaluationSample[] test = plan.TestKnown
            .Select(CreateKnownSample)
            .Concat(plan.TestUnknown.Select(CreateUnknownSample))
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .ToArray();

        return new CatalogueEvaluationDatasetManifest
        {
            SchemaVersion = 1,
            DatasetId = options.DatasetId,
            PipelineVersion = options.PipelineVersion,
            Detector = new CatalogueEvaluationModelDescriptor(
                options.DetectorModelId,
                options.DetectorModelHash.ToLowerInvariant()),
            Embedder = new CatalogueEvaluationEmbeddingModelDescriptor(
                options.EmbedderModelId,
                options.EmbedderModelHash.ToLowerInvariant(),
                plan.Dimensions),
            Thresholds = options.Thresholds.OrderBy(value => value).ToArray(),
            Gallery = gallery,
            Validation = validation,
            Test = test,
            CatalogueExport = new CatalogueEvaluationExportMetadata
            {
                SchemaVersion = 1,
                Scope = new CatalogueEvaluationScopeMetadata
                {
                    Kind = input.Scope.Kind,
                    ProcessingRunId = input.Scope.ProcessingRunId?.ToString(),
                    AssetRevisionIds = input.SourceRevisions
                        .Select(revision => revision.Id.ToString())
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                },
                Seed = options.Seed,
                SplitPolicy = SplitPolicy,
                TimingPolicy = TimingPolicy,
                CatalogueInputSha256 = ComputeInputDigest(input, options),
                SourceRevisions = input.SourceRevisions
                    .OrderBy(revision => revision.Id.ToString(), StringComparer.Ordinal)
                    .Select(revision => new CatalogueEvaluationSourceRevisionMetadata(
                        revision.Id.ToString(),
                        revision.ContentHash.ToString()))
                    .ToArray(),
                GalleryPerPerson = options.GalleryPerPerson,
                ValidationKnownPerPerson = options.ValidationKnownPerPerson,
                TestKnownPerPerson = options.TestKnownPerPerson,
                ValidationUnknownCount = options.ValidationUnknownCount,
                TestUnknownCount = options.TestUnknownCount,
                KnownPersonCount = plan.KnownPersonCount,
                FallbackTimingSampleCount = plan.FallbackTimingSampleCount,
            },
        };
    }

    private static CatalogueEvaluationSample CreateKnownSample(PlannedFace face) =>
        new(
            face.Face.Id.ToString(),
            face.Face.Id.ToString(),
            face.Face.AssetRevisionId.ToString(),
            face.Face.PersonId.ToString(),
            FaceExpected: true,
            FaceDetected: true,
            face.Face.Embedding,
            face.ElapsedMilliseconds);

    private static CatalogueEvaluationSample CreateUnknownSample(PlannedFace face) =>
        new(
            face.Face.Id.ToString(),
            face.Face.Id.ToString(),
            face.Face.AssetRevisionId.ToString(),
            ExpectedPersonId: null,
            FaceExpected: true,
            FaceDetected: true,
            face.Face.Embedding,
            face.ElapsedMilliseconds);

    private static string ComputeInputDigest(
        CatalogueEvaluationExportInput input,
        CatalogueEvaluationExportCommandOptions options)
    {
        StringBuilder canonical = new();
        Append(canonical, "dataset", options.DatasetId);
        Append(canonical, "pipeline", options.PipelineVersion);
        Append(canonical, "seed", options.Seed);
        Append(canonical, "scope", input.Scope.Kind);
        Append(canonical, "run", input.Scope.ProcessingRunId?.ToString() ?? string.Empty);
        Append(canonical, "detector", $"{options.DetectorModelId}:{options.DetectorModelHash.ToLowerInvariant()}");
        Append(canonical, "embedder", $"{options.EmbedderModelId}:{options.EmbedderModelHash.ToLowerInvariant()}");
        Append(canonical, "counts", string.Join(",", new[]
        {
            options.GalleryPerPerson,
            options.ValidationKnownPerPerson,
            options.TestKnownPerPerson,
            options.ValidationUnknownCount,
            options.TestUnknownCount,
        }.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        Append(canonical, "thresholds", string.Join(",", options.Thresholds
            .OrderBy(value => value)
            .Select(value => value.ToString("R", CultureInfo.InvariantCulture))));
        foreach (CatalogueEvaluationSourceRevision revision in input.SourceRevisions
                     .OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            Append(canonical, "revision", $"{revision.Id}:{revision.ContentHash}");
        }
        foreach (CatalogueEvaluationFace face in input.Faces
                     .OrderBy(value => value.AssetRevisionId.ToString(), StringComparer.Ordinal)
                     .ThenBy(value => value.Ordinal)
                     .ThenBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            string embedding = string.Join(",", face.Embedding.Select(value =>
                BitConverter.SingleToInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture)));
            Append(canonical, "face", string.Join(":", new[]
            {
                face.AssetRevisionId.ToString(),
                face.Id.ToString(),
                face.Ordinal.ToString(CultureInfo.InvariantCulture),
                face.PersonId.ToString(),
                face.Dimensions.ToString(CultureInfo.InvariantCulture),
                embedding,
            }));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('=').Append(value).Append('\n');

    private static async Task WriteAtomicAsync(
        string outputPath,
        string content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = outputPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

internal sealed record CatalogueEvaluationExportCommandOptions(
    string DatabasePath,
    string OutputPath,
    string DatasetId,
    string PipelineVersion,
    string DetectorModelId,
    string DetectorModelHash,
    string EmbedderModelId,
    string EmbedderModelHash,
    string Seed,
    string? RunId,
    IReadOnlyList<string> RevisionIds,
    int GalleryPerPerson,
    int ValidationKnownPerPerson,
    int TestKnownPerPerson,
    int ValidationUnknownCount,
    int TestUnknownCount,
    IReadOnlyList<double> Thresholds)
{
    private static readonly double[] DefaultThresholds = [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    public CatalogueEvaluationScope CreateScope() => RunId is not null
        ? CatalogueEvaluationScope.ForRun(ProcessingRunId.From(Guid.Parse(RunId)))
        : CatalogueEvaluationScope.ForRevisions(
            RevisionIds.Select(value => AssetRevisionId.From(Guid.Parse(value))).ToArray());

    public static CatalogueEvaluationExportCommandOptions Parse(string[] args)
    {
        Dictionary<string, string> singles = new(StringComparer.Ordinal);
        List<string> revisions = [];
        List<double> thresholds = [];
        int galleryPerPerson = 1;
        int validationKnown = 1;
        int testKnown = 1;
        int validationUnknown = 1;
        int testUnknown = 1;

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");
            switch (option)
            {
                case "--revision":
                    revisions.Add(GuidValue(value, option));
                    break;
                case "--threshold":
                    thresholds.Add(Threshold(value, option));
                    break;
                case "--gallery-per-person":
                    galleryPerPerson = Count(value, option);
                    break;
                case "--validation-known-per-person":
                    validationKnown = Count(value, option);
                    break;
                case "--test-known-per-person":
                    testKnown = Count(value, option);
                    break;
                case "--validation-unknown":
                    validationUnknown = Count(value, option);
                    break;
                case "--test-unknown":
                    testUnknown = Count(value, option);
                    break;
                case "--database" or "--output" or "--dataset-id" or "--pipeline-version" or
                    "--detector-id" or "--detector-hash" or "--embedder-id" or "--embedder-hash" or
                    "--seed" or "--run":
                    if (!singles.TryAdd(option, NormalizeSingle(option, value)))
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        string[] required =
        [
            "--database", "--output", "--dataset-id", "--pipeline-version", "--detector-id",
            "--detector-hash", "--embedder-id", "--embedder-hash", "--seed",
        ];
        string[] missing = required.Where(option => !singles.ContainsKey(option)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Required option(s) missing: {string.Join(", ", missing)}.");
        }

        bool hasRun = singles.ContainsKey("--run");
        if (hasRun == (revisions.Count > 0))
        {
            throw new ArgumentException("Select exactly one scope: --run or one or more --revision values.");
        }
        if (revisions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != revisions.Count)
        {
            throw new ArgumentException("Each --revision value must be unique.");
        }
        IReadOnlyList<double> selectedThresholds = thresholds.Count == 0 ? DefaultThresholds : thresholds;
        if (selectedThresholds.Distinct().Count() != selectedThresholds.Count)
        {
            throw new ArgumentException("Each --threshold value must be unique.");
        }

        return new CatalogueEvaluationExportCommandOptions(
            singles["--database"],
            singles["--output"],
            singles["--dataset-id"],
            singles["--pipeline-version"],
            singles["--detector-id"],
            singles["--detector-hash"],
            singles["--embedder-id"],
            singles["--embedder-hash"],
            singles["--seed"],
            singles.GetValueOrDefault("--run"),
            revisions,
            galleryPerPerson,
            validationKnown,
            testKnown,
            validationUnknown,
            testUnknown,
            selectedThresholds);
    }

    private static string NormalizeSingle(string option, string value) => option switch
    {
        "--detector-hash" or "--embedder-hash" => Hash(value, option),
        "--run" => GuidValue(value, option),
        _ => RequiredText(value, option),
    };

    private static string RequiredText(string value, string option)
    {
        string normalized = value.Trim();
        if (normalized.Length is < 1 or > 200)
        {
            throw new ArgumentException($"{option} must contain between 1 and 200 characters.");
        }
        return normalized;
    }

    private static string Hash(string value, string option)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{option} must be a 64-character SHA-256 value.");
        }
        return normalized;
    }

    private static string GuidValue(string value, string option) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? parsed.ToString("D")
            : throw new ArgumentException($"{option} must be a non-empty GUID.");

    private static int Count(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed is >= 1 and <= 100
            ? parsed
            : throw new ArgumentException($"{option} must be between 1 and 100.");

    private static double Threshold(string value, string option) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
        double.IsFinite(parsed) && parsed is >= -1 and <= 1
            ? parsed
            : throw new ArgumentException($"{option} must be a finite cosine score between -1 and 1.");
}

internal sealed record CatalogueEvaluationDatasetManifest
{
    public int SchemaVersion { get; init; }
    public string DatasetId { get; init; } = string.Empty;
    public string PipelineVersion { get; init; } = string.Empty;
    public CatalogueEvaluationModelDescriptor Detector { get; init; } = new(string.Empty, string.Empty);
    public CatalogueEvaluationEmbeddingModelDescriptor Embedder { get; init; } = new(string.Empty, string.Empty, 0);
    public IReadOnlyList<double> Thresholds { get; init; } = [];
    public IReadOnlyList<CatalogueEvaluationGalleryItem> Gallery { get; init; } = [];
    public IReadOnlyList<CatalogueEvaluationSample> Validation { get; init; } = [];
    public IReadOnlyList<CatalogueEvaluationSample> Test { get; init; } = [];
    public CatalogueEvaluationExportMetadata CatalogueExport { get; init; } = new();
}

internal sealed record CatalogueEvaluationModelDescriptor(string ModelId, string ModelHash);
internal sealed record CatalogueEvaluationEmbeddingModelDescriptor(string ModelId, string ModelHash, int Dimensions);
internal sealed record CatalogueEvaluationGalleryItem(
    string FaceId,
    string SourceRevisionId,
    string PersonId,
    float[] Embedding);
internal sealed record CatalogueEvaluationSample(
    string SampleId,
    string FaceId,
    string SourceRevisionId,
    string? ExpectedPersonId,
    bool FaceExpected,
    bool FaceDetected,
    float[] Embedding,
    double ElapsedMilliseconds);

internal sealed record CatalogueEvaluationExportMetadata
{
    public int SchemaVersion { get; init; }
    public CatalogueEvaluationScopeMetadata Scope { get; init; } = new();
    public string Seed { get; init; } = string.Empty;
    public string SplitPolicy { get; init; } = string.Empty;
    public string TimingPolicy { get; init; } = string.Empty;
    public string CatalogueInputSha256 { get; init; } = string.Empty;
    public IReadOnlyList<CatalogueEvaluationSourceRevisionMetadata> SourceRevisions { get; init; } = [];
    public int GalleryPerPerson { get; init; }
    public int ValidationKnownPerPerson { get; init; }
    public int TestKnownPerPerson { get; init; }
    public int ValidationUnknownCount { get; init; }
    public int TestUnknownCount { get; init; }
    public int KnownPersonCount { get; init; }
    public int FallbackTimingSampleCount { get; init; }
}

internal sealed record CatalogueEvaluationScopeMetadata
{
    public string Kind { get; init; } = string.Empty;
    public string? ProcessingRunId { get; init; }
    public IReadOnlyList<string> AssetRevisionIds { get; init; } = [];
}

internal sealed record CatalogueEvaluationSourceRevisionMetadata(
    string AssetRevisionId,
    string ContentSha256);
