using System.Globalization;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

internal sealed record NarrationEvaluationCommandOptions(
    string PostgresConnectionEnvironment,
    SmartCollectionId CollectionId,
    string ProxyRoot,
    string ProxyProfile,
    Uri OllamaBaseUri,
    string Model,
    int TargetCount,
    int MomentGapMinutes,
    int SampleCount,
    int TimeoutSeconds,
    string? ReportPath,
    string? ReviewOutputDirectory)
{
    public static NarrationEvaluationCommandOptions Parse(string[] args)
    {
        string? postgresEnvironment = null;
        Guid? collectionId = null;
        string? proxyRoot = null;
        string? proxyProfile = null;
        Uri ollamaBaseUri = OllamaVisionCaptionClient.DefaultBaseUri;
        string model = OllamaVisionCaptionClient.DefaultModel;
        bool modelSpecified = false;
        int targetCount = 50;
        int momentGapMinutes = 30;
        int sampleCount = 12;
        int timeoutSeconds = 180;
        string? report = null;
        string? reviewOutput = null;

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
                    if (!Guid.TryParse(value, out Guid parsedCollection) ||
                        parsedCollection == Guid.Empty)
                    {
                        throw new ArgumentException(
                            "Option '--collection' requires a non-empty GUID.");
                    }
                    collectionId = parsedCollection;
                    break;
                case "--proxy-root":
                    proxyRoot = Single(proxyRoot, value, option);
                    break;
                case "--proxy-profile":
                    proxyProfile = Single(proxyProfile, value, option);
                    break;
                case "--ollama-base-url":
                    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsedUri) ||
                        !parsedUri.IsLoopback ||
                        (parsedUri.Scheme != Uri.UriSchemeHttp &&
                         parsedUri.Scheme != Uri.UriSchemeHttps))
                    {
                        throw new ArgumentException(
                            "Option '--ollama-base-url' must be an absolute loopback HTTP(S) URL.");
                    }
                    ollamaBaseUri = parsedUri;
                    break;
                case "--model":
                    if (modelSpecified)
                    {
                        throw new ArgumentException(
                            "Option '--model' may be supplied only once.");
                    }
                    model = string.IsNullOrWhiteSpace(value)
                        ? throw new ArgumentException(
                            "Option '--model' requires a non-empty value.")
                        : value.Trim();
                    modelSpecified = true;
                    break;
                case "--target-count":
                    targetCount = PositiveInt(value, option, 1000);
                    break;
                case "--moment-gap-minutes":
                    momentGapMinutes = PositiveInt(value, option, 720);
                    break;
                case "--sample-count":
                    sampleCount = PositiveInt(value, option, 30);
                    break;
                case "--timeout-seconds":
                    timeoutSeconds = PositiveInt(value, option, 600);
                    break;
                case "--report":
                    report = Single(report, value, option);
                    break;
                case "--review-output":
                    reviewOutput = Single(reviewOutput, value, option);
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown narration-evaluation option '{option}'.");
            }
        }

        if (postgresEnvironment is null)
        {
            throw new ArgumentException(
                "Option '--postgres-connection-env' is required.");
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

        CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);
        _ = PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);

        return new(
            postgresEnvironment,
            SmartCollectionId.From(collectionId.Value),
            Path.GetFullPath(proxyRoot),
            proxyProfile.Trim(),
            ollamaBaseUri,
            model.Trim(),
            targetCount,
            momentGapMinutes,
            sampleCount,
            timeoutSeconds,
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
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed) ||
            parsed < 1 ||
            parsed > maximum)
        {
            throw new ArgumentException(
                $"Option '{option}' must be an integer between 1 and {maximum}.");
        }

        return parsed;
    }
}

internal static class NarrationEvaluationCommandRunner
{
    private const string ExperimentVersion = "wi-0128-local-caption-v1";

    public static async Task<int> RunAsync(
        NarrationEvaluationCommandOptions options,
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
        if (!Directory.Exists(options.ProxyRoot))
        {
            throw new ArgumentException(
                "The configured review-proxy root does not exist.");
        }

        using HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
        OllamaVisionCaptionClient captionClient = new(
            httpClient,
            options.OllamaBaseUri,
            options.Model);
        LocalVisionModelDescriptor model =
            await captionClient.GetInstalledModelAsync(cancellationToken);

        await using PostgresCatalogueDatabase database = new(connectionString);
        ISmartCollectionRepository definitions =
            new PostgresSmartCollectionRepository(database, TimeProvider.System);
        ISmartCollectionQueryRepository query =
            new PostgresSmartCollectionQueryRepository(
                database,
                definitions,
                TimeProvider.System);
        PostgresArchiveReviewProxyRepository proxyRepository = new(database);
        PostgresPhotoPresentationPreferenceRepository preferences =
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
        Dictionary<AssetRevisionId, SmartCollectionPhoto> catalogueByRevision =
            cataloguePhotos.ToDictionary(photo => photo.RevisionId);
        PhotoMomentCandidate[] momentCandidates = cataloguePhotos
            .Select(photo => new PhotoMomentCandidate(
                photo.RevisionId,
                photo.TakenAtLocal,
                photo.PeopleKeys,
                Latitude: photo.Latitude,
                Longitude: photo.Longitude))
            .ToArray();
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            momentCandidates,
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(
                options.MomentGapMinutes));
        CreativeCollectionCandidateSet generated =
            CreativeCollectionCandidateGenerator.Generate(
                momentCandidates,
                anchors.RevisionIds,
                moments,
                CreativeCollectionContextPolicy.BalancedV1);
        IReadOnlyDictionary<AssetRevisionId, string> effectivePreferences =
            await preferences.GetEffectiveAsync(
                generated.Candidates.Select(candidate => candidate.RevisionId),
                cancellationToken);

        CreativeCollectionSelectionResult selection =
            CreativeCollectionSelector.Select(
                generated,
                momentCandidates,
                moments,
                visualRedundancy: null,
                presentationPreferences: effectivePreferences,
                targetCount: options.TargetCount,
                CreativeCollectionSelectionPolicy.BalancedV1);
        if (selection.SelectedCount == 0)
        {
            throw new InvalidOperationException(
                "The selected Creative Collection produced no photos to caption.");
        }

        CreativeCollectionSelectedCandidate[] selectedForReview =
            PickEvenlySpaced(
                selection.Selected,
                Math.Min(options.SampleCount, selection.SelectedCount));
        AssetRevisionId[] reviewIds = selectedForReview
            .Select(item => item.Candidate.RevisionId)
            .ToArray();
        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata> proxies =
            await proxyRepository.GetManyAsync(
                reviewIds,
                options.ProxyProfile,
                cancellationToken);

        List<NarrationReviewItem> reviewItems = [];
        int unavailableProxies = 0;
        int generationFailures = 0;
        Dictionary<string, int> generationFailureKinds =
            new(StringComparer.Ordinal);

        foreach (CreativeCollectionSelectedCandidate selected in selectedForReview)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetRevisionId revisionId = selected.Candidate.RevisionId;
            SmartCollectionPhoto photo = catalogueByRevision[revisionId];
            string deterministic = CreativeCollectionDeterministicCaption.Build(
                selected.Candidate,
                photo.PeopleKeys);

            if (!proxies.TryGetValue(
                    revisionId,
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
                byte[] imageBytes =
                    await File.ReadAllBytesAsync(proxyPath, cancellationToken);
                LocalVisionCaptionResult generatedCaption =
                    await captionClient.CaptionAsync(
                        imageBytes,
                        cancellationToken);
                IReadOnlyList<string> riskFlags =
                    GeneratedCreativeTextGuard.Evaluate(
                        generatedCaption.Content);
                GeneratedCreativeTextEvidence evidence = new(
                    revisionId,
                    model.Name,
                    model.Digest,
                    OllamaVisionCaptionClient.PromptVersion,
                    generatedCaption.Content,
                    riskFlags);

                reviewItems.Add(new NarrationReviewItem(
                    selected,
                    proxy,
                    deterministic,
                    evidence,
                    generatedCaption));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                generationFailures++;
                IncrementFailure(generationFailureKinds, "timeout");
            }
            catch (HttpRequestException)
            {
                generationFailures++;
                IncrementFailure(generationFailureKinds, "http-transport");
            }
            catch (InvalidDataException)
            {
                generationFailures++;
                IncrementFailure(generationFailureKinds, "invalid-response");
            }
            catch (UnauthorizedAccessException)
            {
                generationFailures++;
                IncrementFailure(generationFailureKinds, "proxy-access");
            }
            catch (IOException)
            {
                generationFailures++;
                IncrementFailure(generationFailureKinds, "proxy-io");
            }
        }

        if (reviewItems.Count == 0)
        {
            string failureSummary = generationFailureKinds.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    generationFailureKinds
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{pair.Key}={pair.Value}"));
            throw new InvalidOperationException(
                $"No local generated captions were produced. " +
                $"sample={selectedForReview.Length}, " +
                $"proxy-unavailable={unavailableProxies}, " +
                $"generation-failures={generationFailures} " +
                $"({failureSummary}), " +
                $"timeout-seconds={options.TimeoutSeconds}.");
        }

        NarrationEvaluationReport report = BuildReport(
            options,
            generated,
            selection,
            model,
            reviewItems,
            selectedForReview.Length,
            unavailableProxies,
            generationFailures);

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

        if (options.ReviewOutputDirectory is string reviewDirectory)
        {
            string index = await WriteReviewOutputAsync(
                reviewDirectory,
                options.ProxyRoot,
                reviewItems,
                cancellationToken);
            output.WriteLine($"review-output: {index}");
        }

        output.WriteLine($"experiment-version: {ExperimentVersion}");
        output.WriteLine($"candidate-count: {generated.TotalCandidateCount}");
        output.WriteLine($"selected-count: {selection.SelectedCount}");
        output.WriteLine($"sample-requested: {selectedForReview.Length}");
        output.WriteLine($"captions-generated: {reviewItems.Count}");
        output.WriteLine($"guard-passed: {report.Guard.PassedCount}");
        output.WriteLine($"guard-flagged: {report.Guard.FlaggedCount}");
        output.WriteLine($"average-caption-ms: {report.Runtime.AverageClientMilliseconds:0.0}");
        output.WriteLine($"model-package-bytes: {model.SizeBytes}");
        output.WriteLine("loopback-only: true");
        output.WriteLine("external-photo-uploads: false");
        output.WriteLine("catalogue-writes: 0");
        return 0;
    }

    private static NarrationEvaluationReport BuildReport(
        NarrationEvaluationCommandOptions options,
        CreativeCollectionCandidateSet generated,
        CreativeCollectionSelectionResult selection,
        LocalVisionModelDescriptor model,
        IReadOnlyList<NarrationReviewItem> reviewItems,
        int requestedCount,
        int unavailableProxies,
        int generationFailures,
        IReadOnlyDictionary<string, int> generationFailureKinds)
    {
        double[] clientMilliseconds = reviewItems
            .Select(item => item.Generated.ClientMilliseconds)
            .OrderBy(value => value)
            .ToArray();
        double[] serverMilliseconds = reviewItems
            .Select(item => item.Generated.ServerMilliseconds)
            .OrderBy(value => value)
            .ToArray();
        Dictionary<string, int> riskCounts = reviewItems
            .SelectMany(item => item.Evidence.RiskFlags)
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);

        int passed = reviewItems.Count(item => item.Evidence.PassesGuard);
        int flagged = reviewItems.Count - passed;
        return new NarrationEvaluationReport(
            SchemaVersion: 1,
            ExperimentVersion,
            new NarrationPipelineEvidence(
                Runtime: "ollama",
                Model: model.Name,
                ModelDigest: model.Digest,
                model.SizeBytes,
                model.Family,
                model.ParameterSize,
                model.QuantizationLevel,
                OllamaVisionCaptionClient.PromptVersion,
                CreativeCollectionGeneratedTextPolicies.DeterministicCaptionV1,
                options.ProxyProfile,
                LoopbackOnly: true),
            new NarrationSampleEvidence(
                generated.TotalCandidateCount,
                selection.SelectedCount,
                requestedCount,
                reviewItems.Count,
                unavailableProxies,
                generationFailures,
                generationFailureKinds),
            new NarrationRuntimeEvidence(
                AverageClientMilliseconds: clientMilliseconds.Average(),
                MedianClientMilliseconds: Percentile(clientMilliseconds, 0.50),
                P95ClientMilliseconds: Percentile(clientMilliseconds, 0.95),
                AverageServerMilliseconds: serverMilliseconds.Average(),
                MedianServerMilliseconds: Percentile(serverMilliseconds, 0.50),
                P95ServerMilliseconds: Percentile(serverMilliseconds, 0.95),
                AveragePromptTokens: reviewItems.Average(
                    item => item.Generated.PromptTokenCount),
                AverageGeneratedTokens: reviewItems.Average(
                    item => item.Generated.GeneratedTokenCount)),
            new NarrationGuardEvidence(
                PassedCount: passed,
                FlaggedCount: flagged,
                RiskFlagCounts: riskCounts),
            GeneratedTextPersisted: false,
            ExternalPhotoUploads: false,
            CatalogueWrites: 0);
    }

    private static async Task<string> WriteReviewOutputAsync(
        string outputDirectory,
        string proxyRoot,
        IReadOnlyList<NarrationReviewItem> items,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        string imageDirectory = Path.Combine(outputDirectory, "images");
        Directory.CreateDirectory(imageDirectory);

        StringBuilder html = new();
        html.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine("<title>WI-0128 local caption review</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:system-ui,sans-serif;margin:24px;background:#f5f5f5;color:#111}.notice{max-width:1000px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:16px}.card{background:white;border:1px solid #ccc;border-radius:8px;overflow:hidden}.card img{width:100%;aspect-ratio:4/3;object-fit:contain;background:#222}.body{padding:12px}.label{font-size:.78rem;font-weight:700;text-transform:uppercase;color:#666;margin-top:10px}.generated{font-size:1.05rem}.flags{font-weight:700}.pass{color:#176b2c}.blocked{color:#9a1c1c}</style>");
        html.AppendLine("</head><body><h1>WI-0128 local caption review</h1>");
        html.AppendLine("<p class=\"notice\">Private local review. Compare the catalogue-derived deterministic text with the local model caption. A guard flag means the generated text must be treated as unsafe derived output, not as a fact. Judge whether the generated caption adds enough useful visible detail to justify its runtime/package cost, and note any factual error even when the guard passes.</p>");
        html.AppendLine("<div class=\"grid\">");

        for (int index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NarrationReviewItem item = items[index];
            string? source = ResolveSafePath(
                proxyRoot,
                item.Proxy.RelativePath,
                item.Proxy.EncodedByteLength);
            if (source is null)
            {
                throw new InvalidOperationException(
                    "A caption-review proxy became unavailable.");
            }

            string extension = Path.GetExtension(item.Proxy.RelativePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".img";
            }

            string destination = Path.Combine(
                imageDirectory,
                $"{index + 1:D3}{extension.ToLowerInvariant()}");
            File.Copy(source, destination, overwrite: true);
            string relative = Path.GetRelativePath(outputDirectory, destination)
                .Replace(Path.DirectorySeparatorChar, '/');

            html.AppendLine("<article class=\"card\">");
            html.Append("<img loading=\"lazy\" src=\"")
                .Append(System.Net.WebUtility.HtmlEncode(relative))
                .AppendLine("\" alt=\"Review proxy\">");
            html.Append("<div class=\"body\"><strong>#")
                .Append((index + 1).ToString("D2", CultureInfo.InvariantCulture))
                .AppendLine("</strong>");
            html.AppendLine("<div class=\"label\">Deterministic</div>");
            html.Append("<div>")
                .Append(System.Net.WebUtility.HtmlEncode(item.Deterministic))
                .AppendLine("</div>");
            html.AppendLine("<div class=\"label\">Local generated caption</div>");
            html.Append("<div class=\"generated\">")
                .Append(System.Net.WebUtility.HtmlEncode(item.Evidence.Content))
                .AppendLine("</div>");
            if (item.Evidence.PassesGuard)
            {
                html.AppendLine("<div class=\"flags pass\">Guard: pass</div>");
            }
            else
            {
                html.Append("<div class=\"flags blocked\">Guard: blocked · ")
                    .Append(System.Net.WebUtility.HtmlEncode(
                        string.Join(", ", item.Evidence.RiskFlags)))
                    .AppendLine("</div>");
            }
            html.Append("</div></article>");
        }

        html.AppendLine("</div></body></html>");
        string indexPath = Path.Combine(outputDirectory, "index.html");
        await File.WriteAllTextAsync(
            indexPath,
            html.ToString(),
            cancellationToken);
        return indexPath;
    }

    private static CreativeCollectionSelectedCandidate[] PickEvenlySpaced(
        IReadOnlyList<CreativeCollectionSelectedCandidate> values,
        int count)
    {
        if (count >= values.Count)
        {
            return values.ToArray();
        }
        if (count == 1)
        {
            return [values[0]];
        }

        List<CreativeCollectionSelectedCandidate> selected = [];
        for (int index = 0; index < count; index++)
        {
            int sourceIndex = (int)Math.Round(
                index * (values.Count - 1d) / (count - 1d),
                MidpointRounding.AwayFromZero);
            CreativeCollectionSelectedCandidate value = values[sourceIndex];
            if (!selected.Contains(value))
            {
                selected.Add(value);
            }
        }

        return selected.ToArray();
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

    private static void IncrementFailure(
        IDictionary<string, int> counts,
        string key)
    {
        counts[key] = counts.TryGetValue(key, out int current)
            ? current + 1
            : 1;
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

    private sealed record NarrationReviewItem(
        CreativeCollectionSelectedCandidate Selected,
        ArchiveReviewProxyMetadata Proxy,
        string Deterministic,
        GeneratedCreativeTextEvidence Evidence,
        LocalVisionCaptionResult Generated);
}

internal sealed record NarrationEvaluationReport(
    int SchemaVersion,
    string ExperimentVersion,
    NarrationPipelineEvidence Pipeline,
    NarrationSampleEvidence Sample,
    NarrationRuntimeEvidence Runtime,
    NarrationGuardEvidence Guard,
    bool GeneratedTextPersisted,
    bool ExternalPhotoUploads,
    int CatalogueWrites);

internal sealed record NarrationPipelineEvidence(
    string Runtime,
    string Model,
    string ModelDigest,
    long ModelSizeBytes,
    string? ModelFamily,
    string? ParameterSize,
    string? QuantizationLevel,
    string PromptVersion,
    string DeterministicCaptionVersion,
    string ProxyProfile,
    bool LoopbackOnly);

internal sealed record NarrationSampleEvidence(
    int CandidateCount,
    int SelectedCount,
    int RequestedCaptionCount,
    int GeneratedCaptionCount,
    int ProxyUnavailableCount,
    int GenerationFailureCount,
    IReadOnlyDictionary<string, int> GenerationFailureKinds);

internal sealed record NarrationRuntimeEvidence(
    double AverageClientMilliseconds,
    double MedianClientMilliseconds,
    double P95ClientMilliseconds,
    double AverageServerMilliseconds,
    double MedianServerMilliseconds,
    double P95ServerMilliseconds,
    double AveragePromptTokens,
    double AverageGeneratedTokens);

internal sealed record NarrationGuardEvidence(
    int PassedCount,
    int FlaggedCount,
    IReadOnlyDictionary<string, int> RiskFlagCounts);
