using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Recognition.Onnx.Semantic;

namespace PhotoIdentity.Cli;

internal sealed record ImageEmbeddingEvaluationCommandOptions(
    string PostgresConnectionEnvironment,
    SmartCollectionId CollectionId,
    string ProxyRoot,
    string ProxyProfile,
    string ModelPath,
    string TokenizerVocabularyPath,
    string TokenizerMergesPath,
    IReadOnlyList<string> Queries,
    int TargetCount,
    int MomentGapMinutes,
    int MaximumCandidates,
    int RetrievalCount,
    int SimilarSeedCount,
    int NeighborsPerSeed,
    string? ReportPath,
    string? ReviewOutputDirectory)
{
    public static ImageEmbeddingEvaluationCommandOptions Parse(string[] args)
    {
        string? postgresEnvironment = null;
        Guid? collectionId = null;
        string? proxyRoot = null;
        string? proxyProfile = null;
        string? model = null;
        string? tokenizerVocabulary = null;
        string? tokenizerMerges = null;
        string? report = null;
        string? reviewOutput = null;
        List<string> queries = [];
        int targetCount = 50;
        int momentGapMinutes = 30;
        int maximumCandidates = 200;
        int retrievalCount = 8;
        int similarSeedCount = 4;
        int neighborsPerSeed = 5;

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
                case "--query":
                    string query = value.Trim();
                    if (query.Length is < 1 or > 200)
                    {
                        throw new ArgumentException(
                            "Option '--query' must contain between 1 and 200 characters.");
                    }
                    queries.Add(query);
                    if (queries.Count > 8)
                    {
                        throw new ArgumentException("At most 8 '--query' values may be supplied.");
                    }
                    break;
                case "--target-count":
                    targetCount = PositiveInt(value, option, 1000);
                    break;
                case "--moment-gap-minutes":
                    momentGapMinutes = PositiveInt(value, option, 720);
                    break;
                case "--max-candidates":
                    maximumCandidates = PositiveInt(value, option, 500);
                    break;
                case "--retrieval-count":
                    retrievalCount = PositiveInt(value, option, 20);
                    break;
                case "--similar-seeds":
                    similarSeedCount = PositiveInt(value, option, 10);
                    break;
                case "--neighbors-per-seed":
                    neighborsPerSeed = PositiveInt(value, option, 10);
                    break;
                case "--report":
                    report = Single(report, value, option);
                    break;
                case "--review-output":
                    reviewOutput = Single(reviewOutput, value, option);
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown image-embeddings option '{option}'.");
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
        if (queries.Count == 0)
        {
            throw new ArgumentException(
                "At least one '--query' is required so semantic retrieval can be reviewed.");
        }

        CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);
        _ = PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);

        return new(
            postgresEnvironment,
            SmartCollectionId.From(collectionId.Value),
            Path.GetFullPath(proxyRoot),
            proxyProfile.Trim(),
            Path.GetFullPath(model),
            Path.GetFullPath(tokenizerVocabulary),
            Path.GetFullPath(tokenizerMerges),
            queries.ToArray(),
            targetCount,
            momentGapMinutes,
            maximumCandidates,
            retrievalCount,
            similarSeedCount,
            neighborsPerSeed,
            report is null ? null : Path.GetFullPath(report),
            reviewOutput is null ? null : Path.GetFullPath(reviewOutput));
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException(
                $"Option '{option}' may be supplied only once.");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                $"Option '{option}' requires a non-empty value.")
            : value.Trim();
    }

    private static int PositiveInt(string value, string option, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < 1 ||
            parsed > maximum)
        {
            throw new ArgumentException(
                $"Option '{option}' must be an integer between 1 and {maximum}.");
        }

        return parsed;
    }
}

internal static class ImageEmbeddingEvaluationCommandRunner
{
    private const string ExperimentVersion = "wi-0127-clip-embedding-v1";

    public static async Task<int> RunAsync(
        ImageEmbeddingEvaluationCommandOptions options,
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
        if (!Directory.Exists(options.ProxyRoot))
        {
            throw new ArgumentException(
                "The configured review-proxy root does not exist.");
        }

        string modelSha = await ComputeSha256Async(
            options.ModelPath,
            cancellationToken);
        string tokenizerVocabularySha = await ComputeSha256Async(
            options.TokenizerVocabularyPath,
            cancellationToken);
        string tokenizerMergesSha = await ComputeSha256Async(
            options.TokenizerMergesPath,
            cancellationToken);

        await using PostgresCatalogueDatabase database = new(connectionString);
        ISmartCollectionRepository definitions =
            new PostgresSmartCollectionRepository(database, TimeProvider.System);
        ISmartCollectionQueryRepository query =
            new PostgresSmartCollectionQueryRepository(
                database,
                definitions,
                TimeProvider.System);
        PostgresArchiveReviewProxyRepository proxyRepository = new(database);
        PostgresPhotoPresentationPreferenceRepository presentationPreferences =
            new(database, TimeProvider.System);

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(options.CollectionId, cancellationToken);
        if (definition is null)
        {
            throw new KeyNotFoundException(
                "The selected Smart Collection was not found.");
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
                Latitude: photo.Latitude,
                Longitude: photo.Longitude))
            .ToArray();
        PhotoMomentGapPolicy momentPolicy =
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(
                options.MomentGapMinutes);
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

        Dictionary<AssetRevisionId, PhotoEmbeddingEvidence> embeddings = [];
        Dictionary<AssetRevisionId, ClipEmbeddingResult> timings = [];
        int unavailableProxies = 0;
        int proxyDecodeFailures = 0;
        int? dimensions = null;
        OpenCvImageDecoder decoder = new();
        using ClipEmbeddingEncoder encoder = new(
            options.ModelPath,
            options.TokenizerVocabularyPath,
            options.TokenizerMergesPath);

        foreach (CreativeCollectionCandidate candidate in sampledCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!proxies.TryGetValue(
                    candidate.RevisionId,
                    out ArchiveReviewProxyMetadata? proxy))
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
                ClipEmbeddingResult result =
                    encoder.EncodeImage(image, cancellationToken);
                if (dimensions.HasValue && dimensions.Value != result.Dimensions)
                {
                    throw new ClipOutputException(
                        $"CLIP image embedding dimension changed from {dimensions.Value} to {result.Dimensions}.");
                }

                dimensions ??= result.Dimensions;
                timings[candidate.RevisionId] = result;
                embeddings[candidate.RevisionId] = new PhotoEmbeddingEvidence(
                    candidate.RevisionId,
                    ClipEmbeddingEncoder.ModelId,
                    modelSha,
                    ClipZeroShotTagger.ImagePreprocessingVersion,
                    result.Vector);
            }
            catch (Exception exception) when (
                exception is ImageDecodingException or
                IOException or
                UnauthorizedAccessException)
            {
                proxyDecodeFailures++;
            }
        }

        if (embeddings.Count == 0 || !dimensions.HasValue)
        {
            throw new InvalidOperationException(
                "No sampled Creative Collection candidate had a readable review proxy.");
        }

        CreativeCollectionCandidate[] scorableCandidates = sampledCandidates
            .Where(candidate => embeddings.ContainsKey(candidate.RevisionId))
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

        Dictionary<AssetRevisionId, IReadOnlyList<float>> embeddingVectors =
            embeddings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Values);

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
                semanticConcepts: null,
                semanticDiversityEnabled: false,
                imageEmbeddings: null,
                embeddingDiversityEnabled: false,
                targetCount: Math.Max(1, effectiveTarget),
                CreativeCollectionSelectionPolicy.BalancedV1);
        CreativeCollectionSelectionResult diversified =
            CreativeCollectionSelector.Select(
                scorableSet,
                momentCandidates,
                moments,
                visualRedundancy: null,
                presentationPreferences: preferences,
                exposureHistory: null,
                noveltyEnabled: false,
                noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
                semanticConcepts: null,
                semanticDiversityEnabled: false,
                imageEmbeddings: embeddingVectors,
                embeddingDiversityEnabled: true,
                targetCount: Math.Max(1, effectiveTarget),
                CreativeCollectionSelectionPolicy.BalancedV1);

        IReadOnlyList<SemanticRetrievalReview> semanticRetrieval =
            BuildSemanticRetrieval(
                options,
                encoder,
                embeddings,
                dimensions.Value,
                cancellationToken);
        IReadOnlyList<SimilarPhotoReview> similarPhotos =
            BuildSimilarPhotoRetrieval(
                options,
                baseline,
                embeddings,
                cancellationToken);

        ImageEmbeddingEvaluationReport report = BuildReport(
            options,
            generated,
            sampledCandidates.Length,
            embeddings,
            timings,
            dimensions.Value,
            unavailableProxies,
            proxyDecodeFailures,
            baseline,
            diversified,
            semanticRetrieval,
            similarPhotos,
            modelSha,
            tokenizerVocabularySha,
            tokenizerMergesSha);

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

        if (options.ReviewOutputDirectory is string reviewOutputDirectory)
        {
            string reviewIndex = await WriteReviewOutputAsync(
                reviewOutputDirectory,
                options.ProxyRoot,
                proxies,
                baseline,
                diversified,
                semanticRetrieval,
                similarPhotos,
                cancellationToken);
            output.WriteLine($"review-output: {reviewIndex}");
        }

        output.WriteLine($"experiment-version: {ExperimentVersion}");
        output.WriteLine($"candidate-count: {generated.TotalCandidateCount}");
        output.WriteLine($"sampled-candidates: {sampledCandidates.Length}");
        output.WriteLine($"proxy-embedded: {embeddings.Count}");
        output.WriteLine($"embedding-dimensions: {dimensions.Value}");
        output.WriteLine($"bytes-per-embedding: {report.Storage.BytesPerEmbedding}");
        output.WriteLine($"semantic-queries: {semanticRetrieval.Count}");
        output.WriteLine($"similar-photo-seeds: {similarPhotos.Count}");
        output.WriteLine($"baseline-selected: {baseline.SelectedCount}");
        output.WriteLine($"embedding-selected: {diversified.SelectedCount}");
        output.WriteLine($"selection-overlap: {report.Selection.SelectionOverlapCount}");
        output.WriteLine($"embedding-replacements: {report.Selection.EmbeddingReplacementCount}");
        output.WriteLine($"baseline-mean-pairwise-cosine: {report.Selection.BaselineMeanPairwiseCosine:0.000}");
        output.WriteLine($"embedding-mean-pairwise-cosine: {report.Selection.EmbeddingMeanPairwiseCosine:0.000}");
        output.WriteLine("exact-vector-search: true");
        output.WriteLine("ann-index-used: false");
        output.WriteLine("catalogue-writes: 0");
        return 0;
    }

    private static IReadOnlyList<SemanticRetrievalReview> BuildSemanticRetrieval(
        ImageEmbeddingEvaluationCommandOptions options,
        ClipEmbeddingEncoder encoder,
        IReadOnlyDictionary<AssetRevisionId, PhotoEmbeddingEvidence> embeddings,
        int dimensions,
        CancellationToken cancellationToken)
    {
        List<SemanticRetrievalReview> results = [];
        for (int queryIndex = 0; queryIndex < options.Queries.Count; queryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string query = options.Queries[queryIndex];
            ClipEmbeddingResult text =
                encoder.EncodeText(query, cancellationToken);
            if (text.Dimensions != dimensions)
            {
                throw new ClipOutputException(
                    $"CLIP text embedding dimension {text.Dimensions} does not match image dimension {dimensions}.");
            }

            SemanticRetrievalHit[] hits = embeddings
                .Select(pair => new SemanticRetrievalHit(
                    pair.Key,
                    PhotoEmbeddingSimilarity.Cosine(
                        text.Vector,
                        pair.Value.Values)))
                .OrderByDescending(hit => hit.Similarity)
                .ThenBy(hit => hit.RevisionId.ToString(), StringComparer.Ordinal)
                .Take(Math.Min(options.RetrievalCount, embeddings.Count))
                .ToArray();

            results.Add(new SemanticRetrievalReview(
                queryIndex + 1,
                query,
                text.TotalDuration.TotalMilliseconds,
                hits));
        }

        return results;
    }

    private static IReadOnlyList<SimilarPhotoReview> BuildSimilarPhotoRetrieval(
        ImageEmbeddingEvaluationCommandOptions options,
        CreativeCollectionSelectionResult baseline,
        IReadOnlyDictionary<AssetRevisionId, PhotoEmbeddingEvidence> embeddings,
        CancellationToken cancellationToken)
    {
        AssetRevisionId[] availableSeeds = baseline.Selected
            .Select(item => item.Candidate.RevisionId)
            .Where(embeddings.ContainsKey)
            .ToArray();
        AssetRevisionId[] seeds = PickEvenlySpaced(
            availableSeeds,
            Math.Min(options.SimilarSeedCount, availableSeeds.Length));

        List<SimilarPhotoReview> results = [];
        for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetRevisionId seed = seeds[seedIndex];
            IReadOnlyList<float> seedVector = embeddings[seed].Values;
            SimilarPhotoHit[] hits = embeddings
                .Where(pair => pair.Key != seed)
                .Select(pair => new SimilarPhotoHit(
                    pair.Key,
                    PhotoEmbeddingSimilarity.Cosine(
                        seedVector,
                        pair.Value.Values)))
                .OrderByDescending(hit => hit.Similarity)
                .ThenBy(hit => hit.RevisionId.ToString(), StringComparer.Ordinal)
                .Take(Math.Min(
                    options.NeighborsPerSeed,
                    Math.Max(0, embeddings.Count - 1)))
                .ToArray();

            results.Add(new SimilarPhotoReview(
                seedIndex + 1,
                seed,
                hits));
        }

        return results;
    }

    private static AssetRevisionId[] PickEvenlySpaced(
        IReadOnlyList<AssetRevisionId> values,
        int count)
    {
        if (count <= 0 || values.Count == 0)
        {
            return [];
        }
        if (count >= values.Count)
        {
            return values.ToArray();
        }
        if (count == 1)
        {
            return [values[0]];
        }

        List<AssetRevisionId> selected = [];
        for (int index = 0; index < count; index++)
        {
            int sourceIndex = (int)Math.Round(
                index * (values.Count - 1d) / (count - 1d),
                MidpointRounding.AwayFromZero);
            AssetRevisionId value = values[sourceIndex];
            if (!selected.Contains(value))
            {
                selected.Add(value);
            }
        }

        return selected.ToArray();
    }

    private static ImageEmbeddingEvaluationReport BuildReport(
        ImageEmbeddingEvaluationCommandOptions options,
        CreativeCollectionCandidateSet generated,
        int sampledCount,
        IReadOnlyDictionary<AssetRevisionId, PhotoEmbeddingEvidence> embeddings,
        IReadOnlyDictionary<AssetRevisionId, ClipEmbeddingResult> timings,
        int dimensions,
        int unavailableProxies,
        int proxyDecodeFailures,
        CreativeCollectionSelectionResult baseline,
        CreativeCollectionSelectionResult diversified,
        IReadOnlyList<SemanticRetrievalReview> semanticRetrieval,
        IReadOnlyList<SimilarPhotoReview> similarPhotos,
        string modelSha,
        string tokenizerVocabularySha,
        string tokenizerMergesSha)
    {
        HashSet<AssetRevisionId> baselineIds = baseline.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();
        HashSet<AssetRevisionId> embeddingIds = diversified.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();
        double[] milliseconds = timings.Values
            .Select(value => value.TotalDuration.TotalMilliseconds)
            .OrderBy(value => value)
            .ToArray();

        PairwiseSummary baselinePairs =
            PairwiseForSelection(baselineIds, embeddings);
        PairwiseSummary embeddingPairs =
            PairwiseForSelection(embeddingIds, embeddings);

        long bytesPerEmbedding = checked((long)dimensions * sizeof(float));
        long sampleBytes = checked(bytesPerEmbedding * embeddings.Count);
        long bytesPerHundredThousand =
            checked(bytesPerEmbedding * 100_000L);

        double averageSemanticTop1 = semanticRetrieval.Count == 0
            ? 0
            : semanticRetrieval.Average(result =>
                result.Hits.Count == 0 ? 0 : result.Hits[0].Similarity);
        double averageSemanticTopK = semanticRetrieval
            .SelectMany(result => result.Hits)
            .Select(hit => hit.Similarity)
            .DefaultIfEmpty(0)
            .Average();
        double averageNeighborTop1 = similarPhotos.Count == 0
            ? 0
            : similarPhotos.Average(result =>
                result.Hits.Count == 0 ? 0 : result.Hits[0].Similarity);
        double averageNeighborTopK = similarPhotos
            .SelectMany(result => result.Hits)
            .Select(hit => hit.Similarity)
            .DefaultIfEmpty(0)
            .Average();

        return new ImageEmbeddingEvaluationReport(
            SchemaVersion: 1,
            ExperimentVersion,
            new ImageEmbeddingPipelineEvidence(
                ClipEmbeddingEncoder.ModelId,
                ModelSha256: modelSha,
                TokenizerVocabularySha256: tokenizerVocabularySha,
                TokenizerMergesSha256: tokenizerMergesSha,
                ClipZeroShotTagger.TokenizerVersion,
                ClipZeroShotTagger.ImagePreprocessingVersion,
                PhotoEmbeddingEvidence.Float32L2NormalizedEncoding,
                dimensions,
                CreativeCollectionEmbeddingDiversityPolicies.BalancedV1),
            new ImageEmbeddingSampleEvidence(
                generated.TotalCandidateCount,
                sampledCount,
                sampledCount < generated.TotalCandidateCount,
                embeddings.Count,
                unavailableProxies,
                proxyDecodeFailures),
            new ImageEmbeddingRuntimeEvidence(
                AverageMilliseconds: milliseconds.Average(),
                MedianMilliseconds: Percentile(milliseconds, 0.50),
                P95Milliseconds: Percentile(milliseconds, 0.95)),
            new ImageEmbeddingStorageEvidence(
                BytesPerEmbedding: bytesPerEmbedding,
                SampleEmbeddingBytes: sampleBytes,
                EstimatedBytesPer100000Photos: bytesPerHundredThousand),
            new SemanticRetrievalEvidence(
                QueryCount: semanticRetrieval.Count,
                options.RetrievalCount,
                ExactComparisonCount:
                    checked((long)semanticRetrieval.Count * embeddings.Count),
                AverageTop1Cosine: averageSemanticTop1,
                AverageReturnedCosine: averageSemanticTopK),
            new SimilarPhotoRetrievalEvidence(
                SeedCount: similarPhotos.Count,
                options.NeighborsPerSeed,
                ExactComparisonCount:
                    checked((long)similarPhotos.Count * Math.Max(0, embeddings.Count - 1)),
                AverageTop1NeighborCosine: averageNeighborTop1,
                AverageReturnedNeighborCosine: averageNeighborTopK),
            new ImageEmbeddingSelectionEvidence(
                baseline.SelectedCount,
                diversified.SelectedCount,
                baselineIds.Intersect(embeddingIds).Count(),
                embeddingIds.Except(baselineIds).Count(),
                baselinePairs.Mean,
                baselinePairs.P95,
                embeddingPairs.Mean,
                embeddingPairs.P95),
            ExactVectorSearch: true,
            AnnIndexUsed: false,
            CatalogueWrites: 0);
    }

    private static PairwiseSummary PairwiseForSelection(
        IReadOnlySet<AssetRevisionId> ids,
        IReadOnlyDictionary<AssetRevisionId, PhotoEmbeddingEvidence> embeddings)
    {
        PhotoEmbeddingEvidence[] selected = ids
            .Where(embeddings.ContainsKey)
            .Select(id => embeddings[id])
            .OrderBy(value => value.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();
        List<double> similarities = [];
        for (int left = 0; left < selected.Length; left++)
        {
            for (int right = left + 1; right < selected.Length; right++)
            {
                similarities.Add(PhotoEmbeddingSimilarity.Cosine(
                    selected[left].Values,
                    selected[right].Values));
            }
        }

        if (similarities.Count == 0)
        {
            return new(0, 0);
        }

        double[] ordered = similarities.OrderBy(value => value).ToArray();
        return new(
            ordered.Average(),
            Percentile(ordered, 0.95));
    }

    private static async Task<string> WriteReviewOutputAsync(
        string outputDirectory,
        string proxyRoot,
        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata> proxies,
        CreativeCollectionSelectionResult baseline,
        CreativeCollectionSelectionResult diversified,
        IReadOnlyList<SemanticRetrievalReview> semanticRetrieval,
        IReadOnlyList<SimilarPhotoReview> similarPhotos,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        string baselineDirectory = Path.Combine(outputDirectory, "baseline");
        string embeddingDirectory = Path.Combine(outputDirectory, "embedding");
        string semanticDirectory = Path.Combine(outputDirectory, "semantic-retrieval");
        string similarDirectory = Path.Combine(outputDirectory, "similar-photos");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(embeddingDirectory);
        Directory.CreateDirectory(semanticDirectory);
        Directory.CreateDirectory(similarDirectory);

        HashSet<AssetRevisionId> baselineIds = baseline.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();
        HashSet<AssetRevisionId> embeddingIds = diversified.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();

        IReadOnlyList<EmbeddingSelectionCard> baselineCards =
            CopySelection(
                outputDirectory,
                baselineDirectory,
                proxyRoot,
                proxies,
                baseline,
                embeddingIds,
                "baseline-only",
                cancellationToken);
        IReadOnlyList<EmbeddingSelectionCard> embeddingCards =
            CopySelection(
                outputDirectory,
                embeddingDirectory,
                proxyRoot,
                proxies,
                diversified,
                baselineIds,
                "embedding-replacement",
                cancellationToken);

        List<SemanticRetrievalPage> semanticPages = [];
        foreach (SemanticRetrievalReview review in semanticRetrieval)
        {
            string queryDirectory = Path.Combine(
                semanticDirectory,
                $"q{review.QueryIndex:D2}");
            Directory.CreateDirectory(queryDirectory);
            List<RetrievalCard> cards = [];
            for (int index = 0; index < review.Hits.Count; index++)
            {
                SemanticRetrievalHit hit = review.Hits[index];
                string relative = CopyProxy(
                    outputDirectory,
                    queryDirectory,
                    proxyRoot,
                    proxies,
                    hit.RevisionId,
                    index + 1,
                    cancellationToken);
                cards.Add(new(
                    index + 1,
                    relative,
                    hit.Similarity));
            }
            semanticPages.Add(new(
                review.QueryIndex,
                review.Query,
                cards));
        }

        List<SimilarPhotoPage> similarPages = [];
        foreach (SimilarPhotoReview review in similarPhotos)
        {
            string seedDirectory = Path.Combine(
                similarDirectory,
                $"s{review.SeedIndex:D2}");
            Directory.CreateDirectory(seedDirectory);
            string seedPath = CopyProxy(
                outputDirectory,
                seedDirectory,
                proxyRoot,
                proxies,
                review.SeedRevisionId,
                rank: 0,
                cancellationToken,
                fileStem: "seed");
            List<RetrievalCard> cards = [];
            for (int index = 0; index < review.Hits.Count; index++)
            {
                SimilarPhotoHit hit = review.Hits[index];
                string relative = CopyProxy(
                    outputDirectory,
                    seedDirectory,
                    proxyRoot,
                    proxies,
                    hit.RevisionId,
                    index + 1,
                    cancellationToken);
                cards.Add(new(
                    index + 1,
                    relative,
                    hit.Similarity));
            }
            similarPages.Add(new(
                review.SeedIndex,
                seedPath,
                cards));
        }

        int sharedCount = baselineIds.Intersect(embeddingIds).Count();
        int replacementCount = embeddingIds.Except(baselineIds).Count();
        StringBuilder html = new();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine("<title>WI-0127 whole-image embedding review</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:system-ui,sans-serif;margin:24px;background:#f5f5f5;color:#111}h1,h2,h3{margin:.35em 0}.notice{max-width:1100px}.columns{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px;align-items:start}.gallery{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:12px}.card{background:white;border:1px solid #ccc;border-radius:8px;overflow:hidden}.card img{width:100%;aspect-ratio:4/3;object-fit:contain;background:#222}.meta{padding:8px;font-size:.9rem}.status{font-weight:600}.query,.seed{margin:24px 0;padding:16px;background:#fff;border:1px solid #ccc;border-radius:8px}.seed-image{max-width:360px;width:100%;background:#222}@media(max-width:900px){.columns{grid-template-columns:1fr}}</style>");
        html.AppendLine("</head><body>");
        html.AppendLine("<h1>WI-0127 whole-image embedding review</h1>");
        html.Append("<p class=\"notice\">Private local review artifact. ")
            .Append("Baseline selected ").Append(baseline.SelectedCount)
            .Append(", embedding-diversified selected ").Append(diversified.SelectedCount)
            .Append(", shared ").Append(sharedCount)
            .Append(", replacements ").Append(replacementCount)
            .AppendLine(". Retrieval is exact cosine comparison over review-proxy embeddings; no ANN/vector index is used and no catalogue evidence is written.</p>");

        html.AppendLine("<h2>Creative Collection selection</h2><div class=\"columns\">");
        AppendSelectionColumn(html, "Metadata-only baseline", baselineCards);
        AppendSelectionColumn(html, "Embedding diversity", embeddingCards);
        html.AppendLine("</div>");

        html.AppendLine("<h2>Semantic text → image retrieval</h2>");
        foreach (SemanticRetrievalPage page in semanticPages)
        {
            html.Append("<section class=\"query\"><h3>Query ")
                .Append(page.QueryIndex.ToString(CultureInfo.InvariantCulture))
                .Append(": ")
                .Append(System.Net.WebUtility.HtmlEncode(page.Query))
                .AppendLine("</h3><div class=\"gallery\">");
            AppendRetrievalCards(html, page.Cards);
            html.AppendLine("</div></section>");
        }

        html.AppendLine("<h2>Similar-photo retrieval</h2>");
        foreach (SimilarPhotoPage page in similarPages)
        {
            html.Append("<section class=\"seed\"><h3>Seed ")
                .Append(page.SeedIndex.ToString(CultureInfo.InvariantCulture))
                .AppendLine("</h3>");
            html.Append("<img class=\"seed-image\" src=\"")
                .Append(System.Net.WebUtility.HtmlEncode(page.SeedImageRelativePath))
                .AppendLine("\" alt=\"Seed review proxy\">");
            html.AppendLine("<div class=\"gallery\">");
            AppendRetrievalCards(html, page.Cards);
            html.AppendLine("</div></section>");
        }

        html.AppendLine("</body></html>");
        string indexPath = Path.Combine(outputDirectory, "index.html");
        await File.WriteAllTextAsync(
            indexPath,
            html.ToString(),
            cancellationToken);
        return indexPath;
    }

    private static IReadOnlyList<EmbeddingSelectionCard> CopySelection(
        string outputDirectory,
        string selectionDirectory,
        string proxyRoot,
        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata> proxies,
        CreativeCollectionSelectionResult selection,
        IReadOnlySet<AssetRevisionId> otherSelectionIds,
        string exclusiveStatus,
        CancellationToken cancellationToken)
    {
        List<EmbeddingSelectionCard> cards = [];
        for (int index = 0; index < selection.Selected.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreativeCollectionSelectedCandidate selected = selection.Selected[index];
            AssetRevisionId revisionId = selected.Candidate.RevisionId;
            string relative = CopyProxy(
                outputDirectory,
                selectionDirectory,
                proxyRoot,
                proxies,
                revisionId,
                index + 1,
                cancellationToken);
            cards.Add(new(
                index + 1,
                relative,
                otherSelectionIds.Contains(revisionId)
                    ? "shared"
                    : exclusiveStatus,
                selected.SelectionScore));
        }

        return cards;
    }

    private static string CopyProxy(
        string outputDirectory,
        string destinationDirectory,
        string proxyRoot,
        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata> proxies,
        AssetRevisionId revisionId,
        int rank,
        CancellationToken cancellationToken,
        string? fileStem = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!proxies.TryGetValue(
                revisionId,
                out ArchiveReviewProxyMetadata? proxy))
        {
            throw new InvalidOperationException(
                "A review photo has no review-proxy metadata.");
        }

        string? sourcePath = ResolveSafePath(
            proxyRoot,
            proxy.RelativePath,
            proxy.EncodedByteLength);
        if (sourcePath is null)
        {
            throw new InvalidOperationException(
                "A review-proxy file became unavailable.");
        }

        string extension = Path.GetExtension(proxy.RelativePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".img";
        }

        string stem = fileStem ?? rank.ToString("D3", CultureInfo.InvariantCulture);
        string destinationPath = Path.Combine(
            destinationDirectory,
            stem + extension.ToLowerInvariant());
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return Path.GetRelativePath(outputDirectory, destinationPath)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void AppendSelectionColumn(
        StringBuilder html,
        string heading,
        IReadOnlyList<EmbeddingSelectionCard> cards)
    {
        html.Append("<section><h3>")
            .Append(System.Net.WebUtility.HtmlEncode(heading))
            .AppendLine("</h3><div class=\"gallery\">");
        foreach (EmbeddingSelectionCard card in cards)
        {
            html.AppendLine("<article class=\"card\">");
            html.Append("<img loading=\"lazy\" src=\"")
                .Append(System.Net.WebUtility.HtmlEncode(card.ImageRelativePath))
                .AppendLine("\" alt=\"Selected review proxy\">");
            html.Append("<div class=\"meta\"><strong>#")
                .Append(card.Rank.ToString("D2", CultureInfo.InvariantCulture))
                .Append("</strong> · score ")
                .Append(card.SelectionScore.ToString(CultureInfo.InvariantCulture))
                .Append("<div class=\"status\">")
                .Append(System.Net.WebUtility.HtmlEncode(card.Status))
                .AppendLine("</div></div></article>");
        }
        html.AppendLine("</div></section>");
    }

    private static void AppendRetrievalCards(
        StringBuilder html,
        IReadOnlyList<RetrievalCard> cards)
    {
        foreach (RetrievalCard card in cards)
        {
            html.AppendLine("<article class=\"card\">");
            html.Append("<img loading=\"lazy\" src=\"")
                .Append(System.Net.WebUtility.HtmlEncode(card.ImageRelativePath))
                .AppendLine("\" alt=\"Retrieved review proxy\">");
            html.Append("<div class=\"meta\"><strong>#")
                .Append(card.Rank.ToString("D2", CultureInfo.InvariantCulture))
                .Append("</strong> · cosine ")
                .Append(card.Similarity.ToString("0.000", CultureInfo.InvariantCulture))
                .AppendLine("</div></article>");
        }
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
            string path = Path.GetFullPath(
                Path.Combine(root, platformRelative));
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

    private static double Percentile(
        IReadOnlyList<double> ordered,
        double percentile)
    {
        if (ordered.Count == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new ArgumentException($"{label} does not exist.");
        }
    }

    private sealed record PairwiseSummary(double Mean, double P95);

    private sealed record EmbeddingSelectionCard(
        int Rank,
        string ImageRelativePath,
        string Status,
        int SelectionScore);

    private sealed record RetrievalCard(
        int Rank,
        string ImageRelativePath,
        double Similarity);

    private sealed record SemanticRetrievalPage(
        int QueryIndex,
        string Query,
        IReadOnlyList<RetrievalCard> Cards);

    private sealed record SimilarPhotoPage(
        int SeedIndex,
        string SeedImageRelativePath,
        IReadOnlyList<RetrievalCard> Cards);
}

internal sealed record SemanticRetrievalHit(
    AssetRevisionId RevisionId,
    double Similarity);

internal sealed record SemanticRetrievalReview(
    int QueryIndex,
    string Query,
    double TextEmbeddingMilliseconds,
    IReadOnlyList<SemanticRetrievalHit> Hits);

internal sealed record SimilarPhotoHit(
    AssetRevisionId RevisionId,
    double Similarity);

internal sealed record SimilarPhotoReview(
    int SeedIndex,
    AssetRevisionId SeedRevisionId,
    IReadOnlyList<SimilarPhotoHit> Hits);

internal sealed record ImageEmbeddingEvaluationReport(
    int SchemaVersion,
    string ExperimentVersion,
    ImageEmbeddingPipelineEvidence Pipeline,
    ImageEmbeddingSampleEvidence Sample,
    ImageEmbeddingRuntimeEvidence Runtime,
    ImageEmbeddingStorageEvidence Storage,
    SemanticRetrievalEvidence SemanticRetrieval,
    SimilarPhotoRetrievalEvidence SimilarPhotoRetrieval,
    ImageEmbeddingSelectionEvidence Selection,
    bool ExactVectorSearch,
    bool AnnIndexUsed,
    int CatalogueWrites);

internal sealed record ImageEmbeddingPipelineEvidence(
    string ModelId,
    string ModelSha256,
    string TokenizerVocabularySha256,
    string TokenizerMergesSha256,
    string TokenizerVersion,
    string ImagePreprocessingVersion,
    string VectorEncoding,
    int Dimensions,
    string EmbeddingDiversityPolicyVersion);

internal sealed record ImageEmbeddingSampleEvidence(
    int CandidateCount,
    int SampledCandidateCount,
    bool Truncated,
    int ProxyEmbeddedCount,
    int ProxyUnavailableCount,
    int ProxyDecodeFailureCount);

internal sealed record ImageEmbeddingRuntimeEvidence(
    double AverageMilliseconds,
    double MedianMilliseconds,
    double P95Milliseconds);

internal sealed record ImageEmbeddingStorageEvidence(
    long BytesPerEmbedding,
    long SampleEmbeddingBytes,
    long EstimatedBytesPer100000Photos);

internal sealed record SemanticRetrievalEvidence(
    int QueryCount,
    int RetrievalCount,
    long ExactComparisonCount,
    double AverageTop1Cosine,
    double AverageReturnedCosine);

internal sealed record SimilarPhotoRetrievalEvidence(
    int SeedCount,
    int NeighborsPerSeed,
    long ExactComparisonCount,
    double AverageTop1NeighborCosine,
    double AverageReturnedNeighborCosine);

internal sealed record ImageEmbeddingSelectionEvidence(
    int BaselineSelectedCount,
    int EmbeddingSelectedCount,
    int SelectionOverlapCount,
    int EmbeddingReplacementCount,
    double BaselineMeanPairwiseCosine,
    double BaselineP95PairwiseCosine,
    double EmbeddingMeanPairwiseCosine,
    double EmbeddingP95PairwiseCosine);
