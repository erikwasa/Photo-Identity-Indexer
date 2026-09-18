using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Recognition.Onnx.Semantic;

namespace PhotoIdentity.Cli;

internal sealed record SemanticTagEvaluationCommandOptions(
    string PostgresConnectionEnvironment,
    SmartCollectionId CollectionId,
    string ProxyRoot,
    string ProxyProfile,
    string ModelPath,
    string TokenizerVocabularyPath,
    string TokenizerMergesPath,
    string ConceptVocabularyPath,
    int TargetCount,
    int MomentGapMinutes,
    int MaximumCandidates,
    int ConceptsPerPhoto,
    int OriginalComparisonCount,
    string? ReportPath)
{
    public static SemanticTagEvaluationCommandOptions Parse(string[] args)
    {
        string? postgresEnvironment = null;
        Guid? collectionId = null;
        string? proxyRoot = null;
        string? proxyProfile = null;
        string? model = null;
        string? tokenizerVocabulary = null;
        string? tokenizerMerges = null;
        string? concepts = null;
        string? report = null;
        int targetCount = 50;
        int momentGapMinutes = 30;
        int maximumCandidates = 200;
        int conceptsPerPhoto = 2;
        int originalComparisonCount = 0;

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--postgres-connection-env":
                    postgresEnvironment = Single(postgresEnvironment, value, option);
                    break;
                case "--collection":
                    if (collectionId.HasValue)
                    {
                        throw new ArgumentException("Option '--collection' may be supplied only once.");
                    }
                    if (!Guid.TryParse(value, out Guid parsedCollection) || parsedCollection == Guid.Empty)
                    {
                        throw new ArgumentException("Option '--collection' requires a non-empty GUID.");
                    }
                    collectionId = parsedCollection;
                    break;
                case "--proxy-root":
                    proxyRoot = Single(proxyRoot, value, option);
                    break;
                case "--proxy-profile":
                    proxyProfile = Single(proxyProfile, value, option);
                    break;
                case "--model":
                    model = Single(model, value, option);
                    break;
                case "--tokenizer-vocab":
                    tokenizerVocabulary = Single(tokenizerVocabulary, value, option);
                    break;
                case "--tokenizer-merges":
                    tokenizerMerges = Single(tokenizerMerges, value, option);
                    break;
                case "--concept-vocabulary":
                    concepts = Single(concepts, value, option);
                    break;
                case "--target-count":
                    targetCount = PositiveInt(value, option, 1000);
                    break;
                case "--moment-gap-minutes":
                    momentGapMinutes = PositiveInt(value, option, 720);
                    break;
                case "--max-candidates":
                    maximumCandidates = PositiveInt(value, option, 1000);
                    break;
                case "--concepts-per-photo":
                    conceptsPerPhoto = PositiveInt(value, option, 5);
                    break;
                case "--compare-originals":
                    originalComparisonCount = NonNegativeInt(value, option, 100);
                    break;
                case "--report":
                    report = Single(report, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown semantic-tags option '{option}'.");
            }
        }

        if (postgresEnvironment is null)
        {
            throw new ArgumentException("Option '--postgres-connection-env' is required.");
        }
        if (!collectionId.HasValue)
        {
            throw new ArgumentException("Option '--collection' is required.");
        }
        if (proxyRoot is null)
        {
            throw new ArgumentException("Option '--proxy-root' is required.");
        }
        if (proxyProfile is null)
        {
            throw new ArgumentException("Option '--proxy-profile' is required.");
        }
        if (model is null)
        {
            throw new ArgumentException("Option '--model' is required.");
        }
        if (tokenizerVocabulary is null)
        {
            throw new ArgumentException("Option '--tokenizer-vocab' is required.");
        }
        if (tokenizerMerges is null)
        {
            throw new ArgumentException("Option '--tokenizer-merges' is required.");
        }
        if (concepts is null)
        {
            throw new ArgumentException("Option '--concept-vocabulary' is required.");
        }

        CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);
        _ = PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);

        return new(
            postgresEnvironment,
            SmartCollectionId.From(collectionId.Value),
            Path.GetFullPath(proxyRoot),
            proxyProfile,
            Path.GetFullPath(model),
            Path.GetFullPath(tokenizerVocabulary),
            Path.GetFullPath(tokenizerMerges),
            Path.GetFullPath(concepts),
            targetCount,
            momentGapMinutes,
            maximumCandidates,
            conceptsPerPhoto,
            originalComparisonCount,
            report is null ? null : Path.GetFullPath(report));
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Option '{option}' requires a non-empty value.")
            : value.Trim();
    }

    private static int PositiveInt(string value, string option, int maximum)
    {
        int parsed = NonNegativeInt(value, option, maximum);
        return parsed == 0
            ? throw new ArgumentException($"Option '{option}' must be at least 1.")
            : parsed;
    }

    private static int NonNegativeInt(string value, string option, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < 0 ||
            parsed > maximum)
        {
            throw new ArgumentException(
                $"Option '{option}' must be an integer between 0 and {maximum}.");
        }

        return parsed;
    }
}

internal static class SemanticTagEvaluationCommandRunner
{
    private const string ExperimentVersion = "wi-0126-clip-zero-shot-v1";

    public static async Task<int> RunAsync(
        SemanticTagEvaluationCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        string? connectionString =
            Environment.GetEnvironmentVariable(options.PostgresConnectionEnvironment);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "The selected PostgreSQL connection environment variable is empty or missing.");
        }

        RequireFile(options.ModelPath, "CLIP ONNX model");
        RequireFile(options.TokenizerVocabularyPath, "CLIP tokenizer vocabulary");
        RequireFile(options.TokenizerMergesPath, "CLIP tokenizer merges");
        RequireFile(options.ConceptVocabularyPath, "visible-content concept vocabulary");
        if (!Directory.Exists(options.ProxyRoot))
        {
            throw new ArgumentException("The configured review-proxy root does not exist.");
        }

        VisibleContentVocabulary vocabulary =
            await LoadVocabularyAsync(options.ConceptVocabularyPath, cancellationToken);
        if (vocabulary.Concepts.Count < 2)
        {
            throw new InvalidDataException(
                "Visible-content vocabulary must contain at least two concepts.");
        }

        await using PostgresCatalogueDatabase database = new(connectionString);
        ISmartCollectionRepository definitions =
            new PostgresSmartCollectionRepository(database, TimeProvider.System);
        ISmartCollectionQueryRepository query =
            new PostgresSmartCollectionQueryRepository(
                database,
                definitions,
                TimeProvider.System);
        PostgresArchiveReviewProxyRepository proxyRepository = new(database);
        PostgresAssetRevisionLookupRepository revisions = new(database);
        PostgresPhotoPresentationPreferenceRepository presentationPreferences =
            new(database, TimeProvider.System);

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(options.CollectionId, cancellationToken);
        if (definition is null)
        {
            throw new KeyNotFoundException("The selected Smart Collection was not found.");
        }

        SmartCollectionSlideshowSnapshot? anchors =
            await query.CreateSlideshowSnapshotAsync(
                options.CollectionId,
                cancellationToken);
        if (anchors is null)
        {
            throw new InvalidOperationException(
                "The selected Smart Collection could not produce an anchor snapshot.");
        }

        IReadOnlyList<SmartCollectionPhoto> cataloguePhotos =
            await query.QueryAllAsync(
                new SmartCollectionFilter(),
                cancellationToken);
        PhotoMomentCandidate[] momentCandidates = cataloguePhotos
            .Select(photo => new PhotoMomentCandidate(
                photo.RevisionId,
                photo.TakenAtLocal,
                photo.PeopleKeys,
                photo.Latitude,
                photo.Longitude))
            .ToArray();
        PhotoMomentGapPolicy momentPolicy =
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(options.MomentGapMinutes);
        PhotoMomentClusteringResult moments =
            PhotoMomentClusterer.Cluster(momentCandidates, momentPolicy);
        CreativeCollectionCandidateSet generated =
            CreativeCollectionCandidateGenerator.Generate(
                momentCandidates,
                anchors.RevisionIds,
                moments,
                CreativeCollectionContextPolicy.BalancedV1);

        CreativeCollectionCandidate[] sampledCandidates = generated.Candidates
            .Take(options.MaximumCandidates)
            .ToArray();
        AssetRevisionId[] sampledIds = sampledCandidates
            .Select(candidate => candidate.RevisionId)
            .ToArray();
        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata> proxies =
            await proxyRepository.GetManyAsync(
                sampledIds,
                options.ProxyProfile,
                cancellationToken);

        Dictionary<AssetRevisionId, ClipZeroShotResult> proxyScores = [];
        int unavailableProxies = 0;
        int proxyDecodeFailures = 0;
        using ClipZeroShotTagger tagger = new(
            options.ModelPath,
            options.TokenizerVocabularyPath,
            options.TokenizerMergesPath);
        OpenCvImageDecoder decoder = new();

        foreach (CreativeCollectionCandidate candidate in sampledCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!proxies.TryGetValue(candidate.RevisionId, out ArchiveReviewProxyMetadata? proxy))
            {
                unavailableProxies++;
                continue;
            }

            string? proxyPath = ResolveSafePath(
                options.ProxyRoot,
                proxy.RelativePath,
                proxy.EncodedByteLength);
            if (proxyPath is null)
            {
                unavailableProxies++;
                continue;
            }

            try
            {
                ImageFrame image = await DecodeAsync(
                    decoder,
                    proxyPath,
                    cancellationToken);
                proxyScores[candidate.RevisionId] = tagger.Score(
                    image,
                    vocabulary.Concepts,
                    cancellationToken);
            }
            catch (ImageDecodingException)
            {
                proxyDecodeFailures++;
            }
            catch (IOException)
            {
                proxyDecodeFailures++;
            }
            catch (UnauthorizedAccessException)
            {
                proxyDecodeFailures++;
            }
        }

        if (proxyScores.Count == 0)
        {
            throw new InvalidOperationException(
                "No sampled Creative Collection candidate had a readable review proxy.");
        }

        CreativeCollectionCandidate[] scorableCandidates = sampledCandidates
            .Where(candidate => proxyScores.ContainsKey(candidate.RevisionId))
            .ToArray();
        CreativeCollectionCandidateSet scorableSet = new(
            generated.MomentPolicyVersion,
            generated.ContextPolicyVersion,
            scorableCandidates.Count(candidate =>
                candidate.Kind == CreativeCollectionCandidateKinds.DirectAnchor),
            scorableCandidates.Count(candidate =>
                candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition),
            scorableCandidates);
        IReadOnlyDictionary<AssetRevisionId, string> preferences =
            await presentationPreferences.GetEffectiveAsync(
                scorableCandidates.Select(candidate => candidate.RevisionId),
                cancellationToken);

        Dictionary<AssetRevisionId, IReadOnlyList<string>> semanticConcepts =
            proxyScores.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Scores
                    .Take(options.ConceptsPerPhoto)
                    .Select(score => score.ConceptId)
                    .ToArray());

        int effectiveTarget = Math.Min(
            options.TargetCount,
            scorableCandidates.Length);
        CreativeCollectionSelectionResult baseline =
            CreativeCollectionSelector.Select(
                scorableSet,
                momentCandidates,
                moments,
                visualRedundancy: null,
                presentationPreferences: preferences,
                exposureHistory: null,
                noveltyEnabled: false,
                noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
                semanticConcepts: semanticConcepts,
                semanticDiversityEnabled: false,
                targetCount: Math.Max(1, effectiveTarget),
                CreativeCollectionSelectionPolicy.BalancedV1);
        CreativeCollectionSelectionResult semantic =
            CreativeCollectionSelector.Select(
                scorableSet,
                momentCandidates,
                moments,
                visualRedundancy: null,
                presentationPreferences: preferences,
                exposureHistory: null,
                noveltyEnabled: false,
                noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
                semanticConcepts: semanticConcepts,
                semanticDiversityEnabled: true,
                targetCount: Math.Max(1, effectiveTarget),
                CreativeCollectionSelectionPolicy.BalancedV1);

        OriginalComparisonSummary originalComparison =
            await CompareOriginalsAsync(
                options,
                decoder,
                tagger,
                vocabulary.Concepts,
                proxyScores,
                scorableCandidates,
                revisions,
                cancellationToken);

        string modelSha = await ComputeSha256Async(options.ModelPath, cancellationToken);
        string tokenizerVocabularySha =
            await ComputeSha256Async(options.TokenizerVocabularyPath, cancellationToken);
        string tokenizerMergesSha =
            await ComputeSha256Async(options.TokenizerMergesPath, cancellationToken);
        string conceptVocabularySha =
            await ComputeSha256Async(options.ConceptVocabularyPath, cancellationToken);

        SemanticTagEvaluationReport report = BuildReport(
            options,
            vocabulary,
            generated,
            sampledCandidates.Length,
            proxyScores,
            unavailableProxies,
            proxyDecodeFailures,
            baseline,
            semantic,
            semanticConcepts,
            originalComparison,
            modelSha,
            tokenizerVocabularySha,
            tokenizerMergesSha,
            conceptVocabularySha);

        if (options.ReportPath is string reportPath)
        {
            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            output.WriteLine($"report: {Path.GetFileName(reportPath)}");
        }

        output.WriteLine($"experiment-version: {ExperimentVersion}");
        output.WriteLine($"vocabulary-version: {vocabulary.VocabularyVersion}");
        output.WriteLine($"candidate-count: {generated.TotalCandidateCount}");
        output.WriteLine($"sampled-candidates: {sampledCandidates.Length}");
        output.WriteLine($"proxy-scored: {proxyScores.Count}");
        output.WriteLine($"proxy-unavailable: {unavailableProxies}");
        output.WriteLine($"proxy-decode-failures: {proxyDecodeFailures}");
        output.WriteLine($"baseline-selected: {baseline.SelectedCount}");
        output.WriteLine($"semantic-selected: {semantic.SelectedCount}");
        output.WriteLine($"selection-overlap: {report.Selection.SelectionOverlapCount}");
        output.WriteLine($"baseline-concept-coverage: {report.Selection.BaselineDistinctConceptCount}");
        output.WriteLine($"semantic-concept-coverage: {report.Selection.SemanticDistinctConceptCount}");
        output.WriteLine($"originals-compared: {originalComparison.ComparedCount}");
        output.WriteLine($"original-top1-agreement: {originalComparison.Top1AgreementRate:0.000}");
        output.WriteLine("catalogue-writes: 0");
        return 0;
    }

    private static SemanticTagEvaluationReport BuildReport(
        SemanticTagEvaluationCommandOptions options,
        VisibleContentVocabulary vocabulary,
        CreativeCollectionCandidateSet generated,
        int sampledCount,
        IReadOnlyDictionary<AssetRevisionId, ClipZeroShotResult> proxyScores,
        int unavailableProxies,
        int proxyDecodeFailures,
        CreativeCollectionSelectionResult baseline,
        CreativeCollectionSelectionResult semantic,
        IReadOnlyDictionary<AssetRevisionId, IReadOnlyList<string>> concepts,
        OriginalComparisonSummary originalComparison,
        string modelSha,
        string tokenizerVocabularySha,
        string tokenizerMergesSha,
        string conceptVocabularySha)
    {
        HashSet<AssetRevisionId> baselineIds = baseline.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();
        HashSet<AssetRevisionId> semanticIds = semantic.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();

        int BaselineCoverage() => baselineIds
            .SelectMany(id => concepts.GetValueOrDefault(id) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Count();
        int SemanticCoverage() => semanticIds
            .SelectMany(id => concepts.GetValueOrDefault(id) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Count();

        Dictionary<string, int> topConceptCounts = proxyScores.Values
            .Select(result => result.Scores[0].ConceptId)
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        double[] proxyMilliseconds = proxyScores.Values
            .Select(result => result.TotalDuration.TotalMilliseconds)
            .OrderBy(value => value)
            .ToArray();

        return new SemanticTagEvaluationReport(
            1,
            ExperimentVersion,
            new SemanticPipelineEvidence(
                ModelSha256: modelSha,
                TokenizerVocabularySha256: tokenizerVocabularySha,
                TokenizerMergesSha256: tokenizerMergesSha,
                ConceptVocabularySha256: conceptVocabularySha,
                vocabulary.VocabularyVersion,
                vocabulary.PromptTemplateVersion,
                ClipZeroShotTagger.TokenizerVersion,
                ClipZeroShotTagger.ImagePreprocessingVersion,
                CreativeCollectionSemanticDiversityPolicies.BalancedV1),
            new SemanticSampleEvidence(
                generated.TotalCandidateCount,
                sampledCount,
                sampledCount < generated.TotalCandidateCount,
                proxyScores.Count,
                unavailableProxies,
                proxyDecodeFailures,
                options.ConceptsPerPhoto),
            new SemanticRuntimeEvidence(
                AverageMilliseconds: proxyMilliseconds.Average(),
                MedianMilliseconds: Percentile(proxyMilliseconds, 0.50),
                P95Milliseconds: Percentile(proxyMilliseconds, 0.95)),
            originalComparison,
            new SemanticSelectionEvidence(
                baseline.SelectedCount,
                semantic.SelectedCount,
                baselineIds.Intersect(semanticIds).Count(),
                semanticIds.Except(baselineIds).Count(),
                BaselineCoverage(),
                SemanticCoverage(),
                topConceptCounts),
            CatalogueWrites: 0);
    }

    private static async Task<OriginalComparisonSummary> CompareOriginalsAsync(
        SemanticTagEvaluationCommandOptions options,
        OpenCvImageDecoder decoder,
        ClipZeroShotTagger tagger,
        IReadOnlyList<ClipVisibleContentConcept> concepts,
        IReadOnlyDictionary<AssetRevisionId, ClipZeroShotResult> proxyScores,
        IReadOnlyList<CreativeCollectionCandidate> candidates,
        PostgresAssetRevisionLookupRepository revisions,
        CancellationToken cancellationToken)
    {
        if (options.OriginalComparisonCount == 0)
        {
            return OriginalComparisonSummary.NotRequested;
        }

        int attempted = 0;
        int compared = 0;
        int unavailable = 0;
        int top1Agreements = 0;
        List<double> topKOverlap = [];
        List<double> milliseconds = [];

        foreach (CreativeCollectionCandidate candidate in candidates)
        {
            if (attempted >= options.OriginalComparisonCount)
            {
                break;
            }

            if (!proxyScores.TryGetValue(candidate.RevisionId, out ClipZeroShotResult? proxy))
            {
                continue;
            }

            attempted++;
            AssetRevisionLookup? revision =
                await revisions.GetRevisionAsync(candidate.RevisionId, cancellationToken);
            string? originalPath = revision is null
                ? null
                : ResolveSafePath(
                    revision.RootLocator,
                    revision.SourceKey,
                    revision.SizeBytes);
            if (originalPath is null)
            {
                unavailable++;
                continue;
            }

            try
            {
                ImageFrame image = await DecodeAsync(
                    decoder,
                    originalPath,
                    cancellationToken);
                ClipZeroShotResult original =
                    tagger.Score(image, concepts, cancellationToken);
                compared++;
                milliseconds.Add(original.TotalDuration.TotalMilliseconds);
                if (string.Equals(
                        proxy.Scores[0].ConceptId,
                        original.Scores[0].ConceptId,
                        StringComparison.Ordinal))
                {
                    top1Agreements++;
                }

                string[] proxyTop = proxy.Scores
                    .Take(options.ConceptsPerPhoto)
                    .Select(score => score.ConceptId)
                    .ToArray();
                string[] originalTop = original.Scores
                    .Take(options.ConceptsPerPhoto)
                    .Select(score => score.ConceptId)
                    .ToArray();
                int union = proxyTop
                    .Union(originalTop, StringComparer.Ordinal)
                    .Count();
                int intersection = proxyTop
                    .Intersect(originalTop, StringComparer.Ordinal)
                    .Count();
                topKOverlap.Add(union == 0 ? 1d : (double)intersection / union);
            }
            catch (Exception exception) when (
                exception is ImageDecodingException or
                IOException or
                UnauthorizedAccessException)
            {
                unavailable++;
            }
        }

        return new OriginalComparisonSummary(
            RequestedCount: options.OriginalComparisonCount,
            AttemptedCount: attempted,
            ComparedCount: compared,
            UnavailableCount: unavailable,
            Top1AgreementRate: compared == 0 ? 0 : (double)top1Agreements / compared,
            MeanTopKJaccard: topKOverlap.Count == 0 ? 0 : topKOverlap.Average(),
            AverageMilliseconds: milliseconds.Count == 0 ? 0 : milliseconds.Average());
    }

    private static async Task<ImageFrame> DecodeAsync(
        OpenCvImageDecoder decoder,
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await decoder.DecodeAsync(
            stream,
            new DecodeOptions(),
            cancellationToken);
    }

    private static string? ResolveSafePath(
        string rootPath,
        string relativePath,
        long expectedLength)
    {
        if (string.IsNullOrWhiteSpace(rootPath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            string root = Path.GetFullPath(rootPath);
            string platformRelative = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, platformRelative));
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!path.Equals(root, comparison) &&
                !path.StartsWith(rootPrefix, comparison))
            {
                return null;
            }

            FileInfo file = new(path);
            if (!file.Exists ||
                file.Length != expectedLength ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            return path;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static async Task<VisibleContentVocabulary> LoadVocabularyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        VisibleContentVocabulary vocabulary =
            await JsonSerializer.DeserializeAsync<VisibleContentVocabulary>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                },
                cancellationToken)
            ?? throw new InvalidDataException(
                "Visible-content vocabulary JSON is empty.");

        if (vocabulary.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(vocabulary.VocabularyVersion) ||
            string.IsNullOrWhiteSpace(vocabulary.PromptTemplateVersion))
        {
            throw new InvalidDataException(
                "Visible-content vocabulary metadata is invalid.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ClipVisibleContentConcept concept in vocabulary.Concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Id) ||
                string.IsNullOrWhiteSpace(concept.Prompt) ||
                !ids.Add(concept.Id))
            {
                throw new InvalidDataException(
                    "Visible-content concepts require unique non-empty ids and prompts.");
            }
        }

        return vocabulary;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        using SHA256 hash = SHA256.Create();
        return Convert.ToHexString(
                await hash.ComputeHashAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new ArgumentException($"{description} file does not exist.");
        }
    }
}

internal sealed record VisibleContentVocabulary(
    int SchemaVersion,
    string VocabularyVersion,
    string PromptTemplateVersion,
    IReadOnlyList<ClipVisibleContentConcept> Concepts);

internal sealed record SemanticPipelineEvidence(
    string ModelSha256,
    string TokenizerVocabularySha256,
    string TokenizerMergesSha256,
    string ConceptVocabularySha256,
    string VocabularyVersion,
    string PromptTemplateVersion,
    string TokenizerVersion,
    string ImagePreprocessingVersion,
    string SemanticSelectionPolicyVersion);

internal sealed record SemanticSampleEvidence(
    int CandidateCount,
    int SampledCandidateCount,
    bool Truncated,
    int ProxyScoredCount,
    int ProxyUnavailableCount,
    int ProxyDecodeFailureCount,
    int ConceptsPerPhoto);

internal sealed record SemanticRuntimeEvidence(
    double AverageMilliseconds,
    double MedianMilliseconds,
    double P95Milliseconds);

internal sealed record OriginalComparisonSummary(
    int RequestedCount,
    int AttemptedCount,
    int ComparedCount,
    int UnavailableCount,
    double Top1AgreementRate,
    double MeanTopKJaccard,
    double AverageMilliseconds)
{
    public static OriginalComparisonSummary NotRequested { get; } =
        new(0, 0, 0, 0, 0, 0, 0);
}

internal sealed record SemanticSelectionEvidence(
    int BaselineSelectedCount,
    int SemanticSelectedCount,
    int SelectionOverlapCount,
    int SemanticReplacementCount,
    int BaselineDistinctConceptCount,
    int SemanticDistinctConceptCount,
    IReadOnlyDictionary<string, int> TopConceptCounts);

internal sealed record SemanticTagEvaluationReport(
    int SchemaVersion,
    string ExperimentVersion,
    SemanticPipelineEvidence Pipeline,
    SemanticSampleEvidence Sample,
    SemanticRuntimeEvidence ProxyRuntime,
    OriginalComparisonSummary OriginalComparison,
    SemanticSelectionEvidence Selection,
    int CatalogueWrites);
