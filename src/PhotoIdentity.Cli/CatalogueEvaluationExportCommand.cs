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
    private const int ExportSchemaVersion = 1;
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
            ModelId detectorModelId = new(options.DetectorModelId);
            Sha256Digest detectorModelHash = new(options.DetectorModelHash);
            ModelId embedderModelId = new(options.EmbedderModelId);
            Sha256Digest embedderModelHash = new(options.EmbedderModelHash);
            CatalogueEvaluationExportInput input = await new SqliteCatalogueEvaluationExportRepository(database)
                .LoadAsync(
                    scope,
                    detectorModelId,
                    detectorModelHash,
                    embedderModelId,
                    embedderModelHash,
                    cancellationToken);

            EvaluationDataset dataset = BuildDataset(input, options);
            string json = JsonSerializer.Serialize(dataset, JsonOptions)
                .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
            await WriteAtomicAsync(outputPath, json, cancellationToken);

            EvaluationCatalogueExportMetadata metadata = dataset.CatalogueExport!;
            output.WriteLine($"dataset: {dataset.DatasetId}");
            output.WriteLine($"scope: {metadata.Scope.Kind}");
            output.WriteLine($"source-revisions: {metadata.SourceRevisions.Count}");
            output.WriteLine($"gallery: {dataset.Gallery.Count}");
            output.WriteLine($"validation: {dataset.Validation.Count}");
            output.WriteLine($"test: {dataset.Test.Count}");
            output.WriteLine($"catalogue-input-sha256: {metadata.CatalogueInputSha256}");
            if (metadata.FallbackTimingSampleCount > 0)
            {
                output.WriteLine(
                    $"warning: {metadata.FallbackTimingSampleCount} exported sample(s) use the deterministic 1 ms timing fallback because processing-job timing was unavailable");
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

    private static EvaluationDataset BuildDataset(
        CatalogueEvaluationExportInput input,
        CatalogueEvaluationExportCommandOptions options)
    {
        if (input.Faces.Count == 0)
        {
            throw new ArgumentException(
                "No human-assigned faces in the selected scope have both requested model revisions.");
        }

        int[] dimensions = input.Faces.Select(face => face.Dimensions).Distinct().ToArray();
        if (dimensions.Length != 1)
        {
            throw new InvalidDataException(
                "The selected embedder revision has inconsistent stored dimensions.");
        }

        IReadOnlyDictionary<AssetRevisionId, CatalogueEvaluationSourceRevision> revisions = input.SourceRevisions
            .ToDictionary(revision => revision.Id);
        IReadOnlyDictionary<AssetRevisionId, int> faceCounts = input.Faces
            .GroupBy(face => face.AssetRevisionId)
            .ToDictionary(group => group.Key, group => group.Count());
        ExportFace[] faces = input.Faces
            .Select(face => CreateExportFace(face, revisions[face.AssetRevisionId], faceCounts[face.AssetRevisionId]))
            .ToArray();
        PhotoGroup[] groups = faces
            .GroupBy(face => face.Face.AssetRevisionId)
            .Select(group => new PhotoGroup(
                group.Key,
                group.OrderBy(face => face.Face.Id.ToString(), StringComparer.Ordinal).ToArray()))
            .OrderBy(group => group.Id.ToString(), StringComparer.Ordinal)
            .ToArray();

        Dictionary<AssetRevisionId, string> groupAssignments = [];
        List<PersonAllocation> knownAllocations = [];
        PersonId[] people = faces
            .Select(face => face.Face.PersonId)
            .Distinct()
            .OrderBy(person => StableKey(options.Seed, "person", person.ToString()), StringComparer.Ordinal)
            .ThenBy(person => person.ToString(), StringComparer.Ordinal)
            .ToArray();
        foreach (PersonId personId in people)
        {
            if (TryAllocateKnownPerson(
                    personId,
                    groups,
                    groupAssignments,
                    options,
                    out PersonAllocation? allocation))
            {
                knownAllocations.Add(allocation);
            }
        }

        if (knownAllocations.Count == 0)
        {
            int requiredPhotos = options.GalleryPerPerson +
                options.ValidationKnownPerPerson +
                options.TestKnownPerPerson;
            int bestAvailable = people
                .Select(person => groups.Count(group => group.Faces.Any(face => face.Face.PersonId == person)))
                .DefaultIfEmpty(0)
                .Max();
            throw new ArgumentException(
                $"Insufficient known examples: at least one assigned person needs {requiredPhotos} distinct source photos " +
                $"({options.GalleryPerPerson} gallery, {options.ValidationKnownPerPerson} validation and " +
                $"{options.TestKnownPerPerson} test); the best available person has {bestAvailable}.");
        }

        HashSet<PersonId> knownPeople = knownAllocations.Select(allocation => allocation.PersonId).ToHashSet();
        HashSet<AssetRevisionId> unknownUsedGroups = [];
        ExportFace[] validationUnknown = ReserveUnknown(
            CatalogueEvaluationSplitNames.Validation,
            options.ValidationUnknownCount,
            groups,
            groupAssignments,
            knownPeople,
            unknownUsedGroups,
            options.Seed);
        ExportFace[] testUnknown = ReserveUnknown(
            CatalogueEvaluationSplitNames.Test,
            options.TestUnknownCount,
            groups,
            groupAssignments,
            knownPeople,
            unknownUsedGroups,
            options.Seed);

        EvaluationGalleryItem[] gallery = knownAllocations
            .SelectMany(allocation => allocation.GalleryGroups.Select(group => SelectPersonFace(
                group,
                allocation.PersonId,
                options.Seed,
                CatalogueEvaluationSplitNames.Gallery)))
            .Select(face => new EvaluationGalleryItem
            {
                FaceId = face.Face.Id.ToString(),
                SourceRevisionId = face.Face.AssetRevisionId.ToString(),
                PersonId = face.Face.PersonId.ToString(),
                Embedding = face.Face.Embedding,
            })
            .OrderBy(item => item.PersonId, StringComparer.Ordinal)
            .ThenBy(item => item.FaceId, StringComparer.Ordinal)
            .ToArray();
        EvaluationSample[] validation = knownAllocations
            .SelectMany(allocation => allocation.ValidationGroups.Select(group => CreateKnownSample(
                SelectPersonFace(
                    group,
                    allocation.PersonId,
                    options.Seed,
                    CatalogueEvaluationSplitNames.Validation))))
            .Concat(validationUnknown.Select(CreateUnknownSample))
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .ToArray();
        EvaluationSample[] test = knownAllocations
            .SelectMany(allocation => allocation.TestGroups.Select(group => CreateKnownSample(
                SelectPersonFace(
                    group,
                    allocation.PersonId,
                    options.Seed,
                    CatalogueEvaluationSplitNames.Test))))
            .Concat(testUnknown.Select(CreateUnknownSample))
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .ToArray();

        HashSet<string> selectedFaceIds = gallery.Select(item => item.FaceId)
            .Concat(validation.Select(sample => sample.FaceId!))
            .Concat(test.Select(sample => sample.FaceId!))
            .ToHashSet(StringComparer.Ordinal);
        int fallbackTimingCount = faces.Count(face => selectedFaceIds.Contains(face.Face.Id.ToString()) && face.UsesTimingFallback);
        string inputDigest = ComputeInputDigest(input, options);
        EvaluationCatalogueExportMetadata metadata = new()
        {
            SchemaVersion = ExportSchemaVersion,
            Scope = new EvaluationCatalogueScopeMetadata
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
            CatalogueInputSha256 = inputDigest,
            SourceRevisions = input.SourceRevisions
                .OrderBy(revision => revision.Id.ToString(), StringComparer.Ordinal)
                .Select(revision => new EvaluationSourceRevisionMetadata(
                    revision.Id.ToString(),
                    revision.ContentHash.ToString()))
                .ToArray(),
            GalleryPerPerson = options.GalleryPerPerson,
            ValidationKnownPerPerson = options.ValidationKnownPerPerson,
            TestKnownPerPerson = options.TestKnownPerPerson,
            ValidationUnknownCount = options.ValidationUnknownCount,
            TestUnknownCount = options.TestUnknownCount,
            KnownPersonCount = knownAllocations.Count,
            FallbackTimingSampleCount = fallbackTimingCount,
        };

        return new EvaluationDataset
        {
            SchemaVersion = 1,
            DatasetId = options.DatasetId,
            PipelineVersion = options.PipelineVersion,
            Detector = new EvaluationModelDescriptor
            {
                ModelId = options.DetectorModelId,
                ModelHash = options.DetectorModelHash.ToLowerInvariant(),
            },
            Embedder = new EvaluationEmbeddingModelDescriptor
            {
                ModelId = options.EmbedderModelId,
                ModelHash = options.EmbedderModelHash.ToLowerInvariant(),
                Dimensions = dimensions[0],
            },
            Thresholds = options.Thresholds.OrderBy(value => value).ToList(),
            Gallery = gallery.ToList(),
            Validation = validation.ToList(),
            Test = test.ToList(),
            CatalogueExport = metadata,
        };
    }

    private static ExportFace CreateExportFace(
        CatalogueEvaluationFace face,
        CatalogueEvaluationSourceRevision revision,
        int revisionFaceCount)
    {
        double elapsedMilliseconds = 1d;
        bool fallback = true;
        if (revision.ProcessingStartedAtUtc is DateTimeOffset started &&
            revision.ProcessingCompletedAtUtc is DateTimeOffset completed)
        {
            double duration = (completed - started).TotalMilliseconds;
            if (duration > 0 && revisionFaceCount > 0)
            {
                elapsedMilliseconds = duration / revisionFaceCount;
                fallback = elapsedMilliseconds <= 0 || !double.IsFinite(elapsedMilliseconds);
                if (fallback)
                {
                    elapsedMilliseconds = 1d;
                }
            }
        }

        return new ExportFace(face, elapsedMilliseconds, fallback);
    }

    private static bool TryAllocateKnownPerson(
        PersonId personId,
        IReadOnlyList<PhotoGroup> groups,
        IDictionary<AssetRevisionId, string> assignments,
        CatalogueEvaluationExportCommandOptions options,
        out PersonAllocation? allocation)
    {
        Dictionary<AssetRevisionId, string> trial = new(assignments);
        HashSet<AssetRevisionId> used = [];
        PhotoGroup[]? gallery = ReservePersonGroups(
            personId,
            CatalogueEvaluationSplitNames.Gallery,
            options.GalleryPerPerson,
            groups,
            trial,
            used,
            options.Seed);
        PhotoGroup[]? validation = gallery is null ? null : ReservePersonGroups(
            personId,
            CatalogueEvaluationSplitNames.Validation,
            options.ValidationKnownPerPerson,
            groups,
            trial,
            used,
            options.Seed);
        PhotoGroup[]? test = validation is null ? null : ReservePersonGroups(
            personId,
            CatalogueEvaluationSplitNames.Test,
            options.TestKnownPerPerson,
            groups,
            trial,
            used,
            options.Seed);
        if (gallery is null || validation is null || test is null)
        {
            allocation = null;
            return false;
        }

        assignments.Clear();
        foreach ((AssetRevisionId revisionId, string split) in trial)
        {
            assignments[revisionId] = split;
        }

        allocation = new PersonAllocation(personId, gallery, validation, test);
        return true;
    }

    private static PhotoGroup[]? ReservePersonGroups(
        PersonId personId,
        string split,
        int count,
        IReadOnlyList<PhotoGroup> groups,
        IDictionary<AssetRevisionId, string> assignments,
        ISet<AssetRevisionId> used,
        string seed)
    {
        PhotoGroup[] selected = groups
            .Where(group =>
                !used.Contains(group.Id) &&
                group.Faces.Any(face => face.Face.PersonId == personId) &&
                (!assignments.TryGetValue(group.Id, out string? assigned) || assigned == split))
            .OrderBy(group => assignments.ContainsKey(group.Id) ? 0 : 1)
            .ThenBy(group => StableKey(seed, $"{split}:{personId}", group.Id.ToString()), StringComparer.Ordinal)
            .ThenBy(group => group.Id.ToString(), StringComparer.Ordinal)
            .Take(count)
            .ToArray();
        if (selected.Length != count)
        {
            return null;
        }

        foreach (PhotoGroup group in selected)
        {
            used.Add(group.Id);
            assignments[group.Id] = split;
        }

        return selected;
    }

    private static ExportFace[] ReserveUnknown(
        string split,
        int count,
        IReadOnlyList<PhotoGroup> groups,
        IDictionary<AssetRevisionId, string> assignments,
        IReadOnlySet<PersonId> knownPeople,
        ISet<AssetRevisionId> used,
        string seed)
    {
        PhotoGroup[] selectedGroups = groups
            .Where(group =>
                !used.Contains(group.Id) &&
                group.Faces.Any(face => !knownPeople.Contains(face.Face.PersonId)) &&
                (!assignments.TryGetValue(group.Id, out string? assigned) || assigned == split))
            .OrderBy(group => assignments.ContainsKey(group.Id) ? 0 : 1)
            .ThenBy(group => StableKey(seed, $"{split}:unknown", group.Id.ToString()), StringComparer.Ordinal)
            .ThenBy(group => group.Id.ToString(), StringComparer.Ordinal)
            .Take(count)
            .ToArray();
        if (selectedGroups.Length != count)
        {
            int available = groups.Count(group =>
                !used.Contains(group.Id) &&
                group.Faces.Any(face => !knownPeople.Contains(face.Face.PersonId)) &&
                (!assignments.TryGetValue(group.Id, out string? assigned) || assigned == split));
            throw new ArgumentException(
                $"Insufficient unknown examples for {split}: requested {count} distinct source photos from assigned people absent from the gallery, but only {available} are available.");
        }

        ExportFace[] selectedFaces = new ExportFace[count];
        for (int index = 0; index < selectedGroups.Length; index++)
        {
            PhotoGroup group = selectedGroups[index];
            used.Add(group.Id);
            assignments[group.Id] = split;
            selectedFaces[index] = group.Faces
                .Where(face => !knownPeople.Contains(face.Face.PersonId))
                .OrderBy(face => StableKey(seed, $"{split}:unknown-face", face.Face.Id.ToString()), StringComparer.Ordinal)
                .ThenBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
                .First();
        }

        return selectedFaces;
    }

    private static ExportFace SelectPersonFace(
        PhotoGroup group,
        PersonId personId,
        string seed,
        string split) =>
        group.Faces
            .Where(face => face.Face.PersonId == personId)
            .OrderBy(face => StableKey(seed, $"{split}:{personId}:face", face.Face.Id.ToString()), StringComparer.Ordinal)
            .ThenBy(face => face.Face.Id.ToString(), StringComparer.Ordinal)
            .First();

    private static EvaluationSample CreateKnownSample(ExportFace face) => new()
    {
        SampleId = face.Face.Id.ToString(),
        FaceId = face.Face.Id.ToString(),
        SourceRevisionId = face.Face.AssetRevisionId.ToString(),
        ExpectedPersonId = face.Face.PersonId.ToString(),
        FaceExpected = true,
        FaceDetected = true,
        Embedding = face.Face.Embedding,
        ElapsedMilliseconds = face.ElapsedMilliseconds,
    };

    private static EvaluationSample CreateUnknownSample(ExportFace face) => new()
    {
        SampleId = face.Face.Id.ToString(),
        FaceId = face.Face.Id.ToString(),
        SourceRevisionId = face.Face.AssetRevisionId.ToString(),
        ExpectedPersonId = null,
        FaceExpected = true,
        FaceDetected = true,
        Embedding = face.Face.Embedding,
        ElapsedMilliseconds = face.ElapsedMilliseconds,
    };

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
            Append(canonical, "face", string.Join(":", new[]
            {
                face.AssetRevisionId.ToString(),
                face.Id.ToString(),
                face.Ordinal.ToString(CultureInfo.InvariantCulture),
                face.PersonId.ToString(),
                face.Dimensions.ToString(CultureInfo.InvariantCulture),
                string.Join(",", face.Embedding.Select(value =>
                    BitConverter.SingleToInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture))),
            }));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('=').Append(value).Append('\n');

    private static string StableKey(string seed, string category, string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\n{category}\n{value}")))
            .ToLowerInvariant();

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

    private sealed record ExportFace(
        CatalogueEvaluationFace Face,
        double ElapsedMilliseconds,
        bool UsesTimingFallback);

    private sealed record PhotoGroup(
        AssetRevisionId Id,
        IReadOnlyList<ExportFace> Faces);

    private sealed record PersonAllocation(
        PersonId PersonId,
        IReadOnlyList<PhotoGroup> GalleryGroups,
        IReadOnlyList<PhotoGroup> ValidationGroups,
        IReadOnlyList<PhotoGroup> TestGroups);
}

internal static class CatalogueEvaluationSplitNames
{
    public const string Gallery = "gallery";
    public const string Validation = "validation";
    public const string Test = "test";
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

    public CatalogueEvaluationScope CreateScope()
    {
        if (RunId is not null)
        {
            return CatalogueEvaluationScope.ForRun(ProcessingRunId.From(Guid.Parse(RunId)));
        }

        return CatalogueEvaluationScope.ForRevisions(
            RevisionIds.Select(value => AssetRevisionId.From(Guid.Parse(value))).ToArray());
    }

    public static CatalogueEvaluationExportCommandOptions Parse(string[] args)
    {
        string? database = null;
        string? output = null;
        string? datasetId = null;
        string? pipelineVersion = null;
        string? detectorId = null;
        string? detectorHash = null;
        string? embedderId = null;
        string? embedderHash = null;
        string? seed = null;
        string? runId = null;
        List<string> revisionIds = [];
        int galleryPerPerson = 1;
        int validationKnownPerPerson = 1;
        int testKnownPerPerson = 1;
        int validationUnknown = 1;
        int testUnknown = 1;
        List<double> thresholds = [];

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");
            switch (option)
            {
                case "--database":
                    database = Single(database, value, option);
                    break;
                case "--output":
                    output = Single(output, value, option);
                    break;
                case "--dataset-id":
                    datasetId = Single(datasetId, Required(value, option), option);
                    break;
                case "--pipeline-version":
                    pipelineVersion = Single(pipelineVersion, Required(value, option), option);
                    break;
                case "--detector-id":
                    detectorId = Single(detectorId, Required(value, option), option);
                    break;
                case "--detector-hash":
                    detectorHash = Single(detectorHash, Hash(value, option), option);
                    break;
                case "--embedder-id":
                    embedderId = Single(embedderId, Required(value, option), option);
                    break;
                case "--embedder-hash":
                    embedderHash = Single(embedderHash, Hash(value, option), option);
                    break;
                case "--seed":
                    seed = Single(seed, Required(value, option), option);
                    break;
                case "--run":
                    runId = Single(runId, GuidValue(value, option), option);
                    break;
                case "--revision":
                    revisionIds.Add(GuidValue(value, option));
                    break;
                case "--gallery-per-person":
                    galleryPerPerson = PositiveCount(value, option);
                    break;
                case "--validation-known-per-person":
                    validationKnownPerPerson = PositiveCount(value, option);
                    break;
                case "--test-known-per-person":
                    testKnownPerPerson = PositiveCount(value, option);
                    break;
                case "--validation-unknown":
                    validationUnknown = PositiveCount(value, option);
                    break;
                case "--test-unknown":
                    testUnknown = PositiveCount(value, option);
                    break;
                case "--threshold":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold) ||
                        !double.IsFinite(threshold) || threshold is < -1 or > 1)
                    {
                        throw new ArgumentException($"{option} must be a finite cosine score between -1 and 1.");
                    }
                    thresholds.Add(threshold);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (database is null || output is null || datasetId is null || pipelineVersion is null ||
            detectorId is null || detectorHash is null || embedderId is null || embedderHash is null || seed is null)
        {
            throw new ArgumentException(
                "--database, --output, --dataset-id, --pipeline-version, --detector-id, --detector-hash, " +
                "--embedder-id, --embedder-hash and --seed are required.");
        }

        if ((runId is null) == (revisionIds.Count == 0))
        {
            throw new ArgumentException("Select exactly one scope: --run or one or more --revision values.");
        }

        if (revisionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != revisionIds.Count)
        {
            throw new ArgumentException("Each --revision value must be unique.");
        }

        IReadOnlyList<double> selectedThresholds = thresholds.Count == 0 ? DefaultThresholds : thresholds;
        if (selectedThresholds.Distinct().Count() != selectedThresholds.Count)
        {
            throw new ArgumentException("Each --threshold value must be unique.");
        }

        return new CatalogueEvaluationExportCommandOptions(
            database,
            output,
            datasetId,
            pipelineVersion,
            detectorId,
            detectorHash,
            embedderId,
            embedderHash,
            seed,
            runId,
            revisionIds,
            galleryPerPerson,
            validationKnownPerPerson,
            testKnownPerPerson,
            validationUnknown,
            testUnknown,
            selectedThresholds);
    }

    private static string Required(string value, string option)
    {
        string normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > 200)
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

    private static int PositiveCount(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed is >= 1 and <= 100
            ? parsed
            : throw new ArgumentException($"{option} must be between 1 and 100.");

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }
        return value;
    }
}

internal sealed record EvaluationCatalogueExportMetadata
{
    public int SchemaVersion { get; init; }
    public EvaluationCatalogueScopeMetadata Scope { get; init; } = new();
    public string Seed { get; init; } = string.Empty;
    public string SplitPolicy { get; init; } = string.Empty;
    public string TimingPolicy { get; init; } = string.Empty;
    public string CatalogueInputSha256 { get; init; } = string.Empty;
    public IReadOnlyList<EvaluationSourceRevisionMetadata> SourceRevisions { get; init; } = [];
    public int GalleryPerPerson { get; init; }
    public int ValidationKnownPerPerson { get; init; }
    public int TestKnownPerPerson { get; init; }
    public int ValidationUnknownCount { get; init; }
    public int TestUnknownCount { get; init; }
    public int KnownPersonCount { get; init; }
    public int FallbackTimingSampleCount { get; init; }
}

internal sealed record EvaluationCatalogueScopeMetadata
{
    public string Kind { get; init; } = string.Empty;
    public string? ProcessingRunId { get; init; }
    public IReadOnlyList<string> AssetRevisionIds { get; init; } = [];
}

internal sealed record EvaluationSourceRevisionMetadata(
    string AssetRevisionId,
    string ContentSha256);
