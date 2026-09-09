using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Web;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record ArchiveOperatorConfiguration(
    string OutputRoot,
    string? RepositoryRoot,
    string? ModelDirectory)
{
    public bool TryResolveAnalysisConfiguration(
        out ArchiveAnalysisConfiguration? configuration,
        out string? message)
    {
        string? repositoryRoot = ResolveRepositoryRoot(RepositoryRoot);
        if (repositoryRoot is null)
        {
            configuration = null;
            message = "Archive analysis is unavailable because the repository root could not be resolved. Set PhotoIdentity__RepositoryRoot to the Photo Identity Indexer checkout.";
            return false;
        }

        try
        {
            configuration = new ArchiveAnalysisConfiguration(OutputRoot, repositoryRoot, ModelDirectory);
            message = null;
            return true;
        }
        catch (Exception exception)
        {
            configuration = null;
            message = exception.Message;
            return false;
        }
    }

    private static string? ResolveRepositoryRoot(string? configuredRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string configured = Path.GetFullPath(configuredRoot);
            return HasRequiredManifests(configured) ? configured : null;
        }

        foreach (string candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(candidate));
            for (int depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                if (HasRequiredManifests(directory.FullName))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }

    private static bool HasRequiredManifests(string root) =>
        File.Exists(Path.Combine(root, "models", "manifests", "centerface-2019-fp32.json")) &&
        File.Exists(Path.Combine(root, "models", "manifests", "sface-2021dec-fp32.json"));
}

public static class ArchiveEndpoints
{
    public static IEndpointRouteBuilder MapArchiveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/archive");
        group.MapGet("/status", GetStatusAsync);
        group.MapGet("/items", GetItemsAsync);
        group.MapPost("/include", IncludeAsync);
        group.MapPut("/coverage", ReplaceCoverageAsync);
        group.MapPost("/sync", SyncAsync);
        group.MapPost("/advance/start", StartAdvancementAsync);
        group.MapPost("/advance/pause", PauseAdvancementAsync);
        group.MapPost("/analysis/step", AnalysisStepAsync);
        group.MapGet("/diagnostics/throughput", GetThroughputDiagnostics);
        group.MapPost("/diagnostics/throughput/reset", ResetThroughputDiagnostics);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await BuildStatusAsync(
                coverageRepository,
                archiveStatusRepository,
                advancementControl,
                operatorConfiguration,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> GetItemsAsync(
        string? folder,
        string? state,
        int? offset,
        int? limit,
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveCoverageState configured = await coverageRepository.GetAsync(cancellationToken)
                ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");
            Sha256Digest? profileHash = await ResolveProfileHashAsync(
                configured,
                operatorConfiguration,
                cancellationToken);
            CatalogueArchiveItemPage page = await archiveStatusRepository.GetItemsAsync(
                configured.Source.SourceId,
                folder ?? string.Empty,
                profileHash,
                state ?? "all",
                offset ?? 0,
                limit ?? 50,
                cancellationToken);
            return Results.Ok(new ArchiveItemPageResponse(
                page.Offset,
                page.Limit,
                page.Total,
                page.Items
                    .Select(item => new ArchiveItemStatusResponse(
                        item.RelativePath,
                        item.RevisionId?.ToString(),
                        item.Availability,
                        item.SourceVerificationState,
                        item.AnalysisState,
                        item.LastError))
                    .ToArray()));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> IncludeAsync(
        ArchiveIncludeRequest request,
        SqliteCatalogueDatabase database,
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArchiveCoverageState? configured = await coverageRepository.GetAsync(cancellationToken);
            ArchiveCatalogueSource source;

            if (configured is null)
            {
                if (string.IsNullOrWhiteSpace(request.RootPath))
                {
                    throw new ArgumentException("The archive root is required when configuring a fresh catalogue.");
                }

                string root = Path.GetFullPath(request.RootPath);
                if (!Directory.Exists(root))
                {
                    throw new DirectoryNotFoundException($"The archive root does not exist: {root}");
                }

                CatalogueSource catalogueSource = await new SqliteLocalBatchRepository(database).GetOrCreateLocalFolderSourceAsync(
                    root,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                source = ToArchiveCatalogueSource(catalogueSource);
            }
            else
            {
                source = configured.Source;
                if (!string.IsNullOrWhiteSpace(request.RootPath) &&
                    !PathsEqual(source.RootLocator, request.RootPath))
                {
                    throw new ArgumentException("This catalogue is already configured for a different permanent archive root.");
                }
            }

            _ = await coverageRepository.ConfigureAndIncludeAsync(
                source,
                request.RelativeFolder,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(await BuildStatusAsync(
                coverageRepository,
                archiveStatusRepository,
                advancementControl,
                operatorConfiguration,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> ReplaceCoverageAsync(
        ArchiveCoverageUpdateRequest request,
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.IncludedFolders);
            _ = await coverageRepository.ReplaceIncludedFoldersAsync(
                request.IncludedFolders,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(await BuildStatusAsync(
                coverageRepository,
                archiveStatusRepository,
                advancementControl,
                operatorConfiguration,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> SyncAsync(
        SqliteCatalogueDatabase database,
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        ArchiveThroughputMetrics metrics,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveCoverageState configured = await coverageRepository.GetAsync(cancellationToken)
                ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");

            LocalFolderAssetSource source = new(configured.Source.SourceId, configured.Source.RootLocator);
            CatalogueSource catalogueSource = ToCatalogueSource(configured.Source);
            LocalArchiveSyncSummary summary;
            using (IDisposable syncTiming = metrics.Measure(ArchiveThroughputMetricNames.Synchronization))
            {
                summary = await new LocalArchiveSyncCoordinator(database, metrics).SyncAsync(
                    source,
                    catalogueSource,
                    configured.IncludedFolders,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            ArchiveStatusResponse status = await BuildStatusAsync(
                coverageRepository,
                archiveStatusRepository,
                advancementControl,
                operatorConfiguration,
                cancellationToken);
            return Results.Ok(new ArchiveSyncResponse(
                summary.SupportedFileCount,
                summary.LocalFileCount,
                summary.OnlineOnlyFileCount,
                summary.DownloadingFileCount,
                summary.UnavailableFileCount,
                summary.AvailabilityErrorCount,
                summary.NewRevisionCount,
                summary.UnchangedFileCount,
                summary.VerifiedSourceCount,
                summary.NeedsSourceVerificationCount,
                summary.UnverifiedSourceCount,
                summary.MarkedDeletedCount,
                status));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> StartAdvancementAsync(
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveCoverageState configured = await coverageRepository.GetAsync(cancellationToken)
                ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");
            await advancementControl.RequestRunAsync(
                configured.Source.SourceId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(await BuildStatusAsync(
                coverageRepository,
                archiveStatusRepository,
                advancementControl,
                operatorConfiguration,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> PauseAdvancementAsync(
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveCoverageState configured = await coverageRepository.GetAsync(cancellationToken)
                ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");
            await advancementControl.PauseAsync(
                configured.Source.SourceId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(await BuildStatusAsync(
                coverageRepository,
                archiveStatusRepository,
                advancementControl,
                operatorConfiguration,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static async Task<IResult> AnalysisStepAsync(
        SqliteCatalogueDatabase database,
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        ArchiveBoundedAnalysisService boundedAnalysis,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveBoundedAnalysisAdvanceResult advanced = await boundedAnalysis.AdvanceAsync(
                operatorConfiguration,
                cancellationToken);
            return Results.Ok(new ArchiveAnalysisStepResponse(
                advanced.StartedNewRun,
                await BuildStatusAsync(
                    coverageRepository,
                    archiveStatusRepository,
                    advancementControl,
                    operatorConfiguration,
                    cancellationToken)));
        }
        catch (Exception exception)
        {
            return BadRequest(exception);
        }
    }

    private static IResult GetThroughputDiagnostics(ArchiveThroughputMetrics metrics) =>
        Results.Ok(ToResponse(metrics.GetSnapshot()));

    private static IResult ResetThroughputDiagnostics(ArchiveThroughputMetrics metrics) =>
        Results.Ok(ToResponse(metrics.Reset()));

    private static ArchiveThroughputDiagnosticsResponse ToResponse(ArchiveThroughputSnapshot snapshot) =>
        new(
            snapshot.Generation,
            snapshot.ResetAtUtc,
            snapshot.CapturedAtUtc,
            snapshot.Stages
                .Select(value => new ArchiveThroughputStageMetricResponse(
                    value.Name,
                    value.Count,
                    value.TotalMilliseconds,
                    value.AverageMilliseconds,
                    value.MaxMilliseconds))
                .ToArray(),
            snapshot.Counters
                .Select(value => new ArchiveThroughputCounterMetricResponse(value.Name, value.Value))
                .ToArray(),
            snapshot.HashReads
                .Select(value => new ArchiveThroughputHashReadMetricResponse(
                    value.Kind,
                    value.Count,
                    value.Bytes,
                    value.SubjectCount,
                    value.AverageReadsPerSubject,
                    value.MaxReadsPerSubject))
                .ToArray());

    private static async Task<ArchiveStatusResponse> BuildStatusAsync(
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        IArchiveAdvancementControlRepository advancementControl,
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageState? configured = await coverageRepository.GetAsync(cancellationToken);
        ArchiveFolderStatusResponse empty = new("", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (configured is null)
        {
            return new ArchiveStatusResponse(
                false,
                null,
                [],
                false,
                "Configure the permanent archive root and include a folder before synchronizing or analysing.",
                null,
                ArchiveAnalysisConfiguration.DetectorModelId,
                ArchiveAnalysisConfiguration.ConfidenceThreshold,
                ArchiveAnalysisConfiguration.EmbedderModelId,
                empty,
                [],
                null,
                null);
        }

        Sha256Digest? profileHash = null;
        bool analysisReady = false;
        string? analysisMessage = null;
        if (operatorConfiguration.TryResolveAnalysisConfiguration(
                out ArchiveAnalysisConfiguration? analysisConfiguration,
                out string? resolutionMessage) &&
            analysisConfiguration is not null)
        {
            try
            {
                AnalysisProfileDefinition profile = await ArchiveAnalysisProfileFactory.CreateAsync(
                    analysisConfiguration.ToBatchConfiguration(configured.Source.RootLocator),
                    cancellationToken);
                profileHash = profile.ComputeHash();
                analysisReady = true;
            }
            catch (Exception exception)
            {
                analysisMessage = exception.Message;
            }
        }
        else
        {
            analysisMessage = resolutionMessage;
        }

        CatalogueArchiveFolderStatus total = await archiveStatusRepository.GetStatusAsync(
            configured.Source.SourceId,
            string.Empty,
            profileHash,
            cancellationToken);
        List<ArchiveFolderStatusResponse> folders = [];
        foreach (string folder in configured.IncludedFolders)
        {
            CatalogueArchiveFolderStatus value = await archiveStatusRepository.GetStatusAsync(
                configured.Source.SourceId,
                folder,
                profileHash,
                cancellationToken);
            folders.Add(ToResponse(value));
        }

        ArchiveRunStatusResponse? latestRun = null;
        if (profileHash is Sha256Digest resolvedProfile)
        {
            CatalogueArchiveRunStatus? latest = await archiveStatusRepository.GetLatestRunAsync(resolvedProfile, cancellationToken);
            latestRun = latest is null ? null : ToResponse(latest);
        }

        ArchiveAdvancementControlState? advancement = await advancementControl
            .GetAsync(configured.Source.SourceId, cancellationToken);
        ArchiveAdvancementStatusResponse? advancementResponse = advancement is null
            ? null
            : new ArchiveAdvancementStatusResponse(
                advancement.RuntimeState,
                advancement.IsRequested,
                advancement.Message,
                advancement.UpdatedAtUtc);

        string rootName = new DirectoryInfo(configured.Source.RootLocator).Name;
        return new ArchiveStatusResponse(
            true,
            rootName,
            configured.IncludedFolders,
            analysisReady,
            analysisMessage,
            profileHash?.ToString(),
            ArchiveAnalysisConfiguration.DetectorModelId,
            ArchiveAnalysisConfiguration.ConfidenceThreshold,
            ArchiveAnalysisConfiguration.EmbedderModelId,
            ToResponse(total),
            folders,
            latestRun,
            advancementResponse);
    }

    private static async Task<Sha256Digest?> ResolveProfileHashAsync(
        ArchiveCoverageState configured,
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken)
    {
        if (!operatorConfiguration.TryResolveAnalysisConfiguration(
                out ArchiveAnalysisConfiguration? analysisConfiguration,
                out _) ||
            analysisConfiguration is null)
        {
            return null;
        }

        AnalysisProfileDefinition profile = await ArchiveAnalysisProfileFactory.CreateAsync(
            analysisConfiguration.ToBatchConfiguration(configured.Source.RootLocator),
            cancellationToken);
        return profile.ComputeHash();
    }

    private static ArchiveCatalogueSource ToArchiveCatalogueSource(CatalogueSource source) =>
        new(source.Id, source.Kind, source.RootLocator, source.CreatedAtUtc);

    private static CatalogueSource ToCatalogueSource(ArchiveCatalogueSource source) =>
        new(source.SourceId, source.Kind, source.RootLocator, source.CreatedAtUtc);

    private static ArchiveFolderStatusResponse ToResponse(CatalogueArchiveFolderStatus status) => new(
        status.RelativeFolder,
        status.CurrentImages,
        status.LocalImages,
        status.OnlineOnlyImages,
        status.DownloadingImages,
        status.UnavailableImages,
        status.AvailabilityErrorImages,
        status.AnalysedImages,
        status.PendingImages,
        status.FailedImages,
        status.NeedsSourceVerificationImages,
        status.UnverifiedSourceImages,
        status.MissingImages);

    private static ArchiveRunStatusResponse ToResponse(CatalogueArchiveRunStatus run) => new(
        run.RunId.ToString(),
        run.Status,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.TotalJobs,
        run.QueuedJobs,
        run.RunningJobs,
        run.SucceededJobs,
        run.FailedJobs,
        run.CancelledJobs);

    private static IResult BadRequest(Exception exception) =>
        Results.BadRequest(new ArchiveErrorResponse(exception.Message));

    private static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
