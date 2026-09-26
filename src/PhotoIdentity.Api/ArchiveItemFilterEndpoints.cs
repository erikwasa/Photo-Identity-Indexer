using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record ArchiveExactDuplicateCopyResponse(
    string SourceId,
    string AssetId,
    string RevisionId,
    string SourceKey,
    bool IsMissing);

public sealed record ArchiveExactDuplicateGroupResponse(
    string ContentSha256,
    IReadOnlyList<ArchiveExactDuplicateCopyResponse> Copies);

public sealed record ArchiveSourceCopyExclusionResponse(
    string SourceId,
    string SourceKey,
    DateTimeOffset ExcludedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    string PurgeState,
    string? PurgeErrorCode,
    DateTimeOffset PurgeUpdatedAtUtc);

public sealed record ArchiveExcludeRevisionRequest(string? RevisionId);
public sealed record ArchiveExcludeRevisionsRequest(IReadOnlyList<string>? RevisionIds);
public sealed record ArchiveBulkExclusionResponse(
    int Requested,
    int Excluded,
    IReadOnlyList<ArchiveSourceCopyExclusionResponse> Exclusions);
public sealed record ArchiveRestoreSourceCopyRequest(string? SourceId, string? SourceKey);
public sealed record ArchiveRetrySourceCopyPurgeRequest(string? SourceId, string? SourceKey);

public static class ArchiveItemFilterEndpoints
{
    private const int MissingLifecycleReadBatchSize = 200;

    public static IEndpointRouteBuilder MapArchiveItemFilterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/items/filter", GetItemsAsync);
        endpoints.MapGet("/api/archive/exact-duplicates", GetExactDuplicatesAsync);
        endpoints.MapGet("/api/archive/exclusions", GetExclusionsAsync);
        endpoints.MapPost("/api/archive/exclusions/revision", ExcludeRevisionAsync);
        endpoints.MapPost("/api/archive/exclusions/revisions", ExcludeRevisionsAsync);
        endpoints.MapPost("/api/archive/exclusions/retry", RetryExclusionAsync);
        endpoints.MapPost("/api/archive/exclusions/restore", RestoreExclusionAsync);
        return endpoints;
    }

    private static async Task<IResult> GetItemsAsync(
        string? folder,
        string? availability,
        string? verification,
        string? analysis,
        int? offset,
        int? limit,
        IArchiveCoverageRepository coverageRepository,
        IArchiveStatusRepository archiveStatusRepository,
        ISourceCopyExclusionRepository exclusions,
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
            string resolvedFolder = folder ?? string.Empty;
            string resolvedAvailability = availability ?? "all";
            string resolvedVerification = verification ?? "all";
            string resolvedAnalysis = analysis ?? "all";
            int resolvedOffset = offset ?? 0;
            int resolvedLimit = limit ?? 50;

            if (string.Equals(resolvedAnalysis, "missing", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(await GetVisibleMissingItemsAsync(
                    configured.Source.SourceId,
                    resolvedFolder,
                    profileHash,
                    resolvedAvailability,
                    resolvedVerification,
                    resolvedOffset,
                    resolvedLimit,
                    archiveStatusRepository,
                    exclusions,
                    cancellationToken));
            }

            CatalogueArchiveItemPage page = await archiveStatusRepository.GetItemsAsync(
                configured.Source.SourceId,
                resolvedFolder,
                profileHash,
                resolvedAvailability,
                resolvedVerification,
                resolvedAnalysis,
                resolvedOffset,
                resolvedLimit,
                cancellationToken);
            return Results.Ok(ToResponse(page));
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new ArchiveErrorResponse(exception.Message));
        }
    }

    private static async Task<ArchiveItemPageResponse> GetVisibleMissingItemsAsync(
        SourceId sourceId,
        string folder,
        Sha256Digest? profileHash,
        string availability,
        string verification,
        int offset,
        int limit,
        IArchiveStatusRepository archiveStatusRepository,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        IReadOnlyList<SourceCopyExclusionState> exclusionStates =
            await exclusions.ListAsync(sourceId, cancellationToken);
        HashSet<string> excludedKeys = exclusionStates
            .Select(static value => value.SourceKey)
            .ToHashSet(StringComparer.Ordinal);

        List<ArchiveItemStatusResponse> visible = [];
        int sourceOffset = 0;
        int sourceTotal;
        do
        {
            CatalogueArchiveItemPage page = await archiveStatusRepository.GetItemsAsync(
                sourceId,
                folder,
                profileHash,
                availability,
                verification,
                "missing",
                sourceOffset,
                MissingLifecycleReadBatchSize,
                cancellationToken);
            sourceTotal = page.Total;
            visible.AddRange(page.Items
                .Where(item => !excludedKeys.Contains(item.RelativePath))
                .Select(ToResponse));
            sourceOffset += page.Items.Count;
            if (page.Items.Count == 0)
            {
                break;
            }
        }
        while (sourceOffset < sourceTotal);

        ArchiveItemStatusResponse[] items = visible
            .Skip(offset)
            .Take(limit)
            .ToArray();
        return new ArchiveItemPageResponse(offset, limit, visible.Count, items);
    }

    private static async Task<IResult> GetExactDuplicatesAsync(
        IArchiveCoverageRepository coverageRepository,
        ISourceCopyExclusionRepository exclusions,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveCoverageState configured = await coverageRepository.GetAsync(cancellationToken)
                ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");
            IExactDuplicateRepository repository = ResolveExactDuplicateRepository(services);
            IReadOnlyList<ExactDuplicateGroup> groups = await repository.GetGroupsAsync(
                configured.Source.SourceId,
                cancellationToken);
            IReadOnlyList<SourceCopyExclusionState> excluded = await exclusions.ListAsync(
                configured.Source.SourceId,
                cancellationToken);
            HashSet<string> excludedKeys = excluded
                .Select(static value => value.SourceKey)
                .ToHashSet(StringComparer.Ordinal);

            ArchiveExactDuplicateGroupResponse[] visibleGroups = groups
                .Select(group => new ArchiveExactDuplicateGroupResponse(
                    group.ContentHash.ToString(),
                    group.Copies
                        .Where(copy => !excludedKeys.Contains(copy.SourceKey))
                        .Select(copy => new ArchiveExactDuplicateCopyResponse(
                            copy.SourceId.ToString(),
                            copy.AssetId.ToString(),
                            copy.RevisionId.ToString(),
                            copy.SourceKey,
                            copy.IsMissing))
                        .ToArray()))
                .Where(static group => group.Copies.Count >= 2)
                .ToArray();

            return Results.Ok(visibleGroups);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new ArchiveErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> GetExclusionsAsync(
        ISourceCopyExclusionRepository exclusions,
        IArchiveCoverageRepository coverage,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageState? configured = await coverage.GetAsync(cancellationToken);
        if (configured is null)
        {
            return Results.BadRequest(new ArchiveErrorResponse("The permanent archive has not been configured yet."));
        }

        IReadOnlyList<SourceCopyExclusionState> values =
            await exclusions.ListAsync(configured.Source.SourceId, cancellationToken);
        return Results.Ok(values.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> ExcludeRevisionAsync(
        ArchiveExcludeRevisionRequest request,
        IAssetRevisionLookupRepository revisions,
        ISourceCopyExclusionRepository exclusions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(request?.RevisionId, out AssetRevisionId revisionId))
        {
            return Results.BadRequest(new ArchiveErrorResponse("A valid revision identifier is required."));
        }

        AssetRevisionLookup? revision = await revisions.GetRevisionAsync(revisionId, cancellationToken);
        if (revision is null)
        {
            return Results.NotFound();
        }

        SourceCopyExclusionState state = await exclusions.ExcludeAsync(
            revision.SourceId,
            revision.SourceKey,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return Results.Ok(ToResponse(state));
    }

    private static async Task<IResult> ExcludeRevisionsAsync(
        ArchiveExcludeRevisionsRequest request,
        IAssetRevisionLookupRepository revisions,
        ISourceCopyExclusionRepository exclusions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request?.RevisionIds is null || request.RevisionIds.Count == 0)
        {
            return Results.BadRequest(new ArchiveErrorResponse("Select at least one photo to exclude."));
        }

        if (request.RevisionIds.Count > 500)
        {
            return Results.BadRequest(new ArchiveErrorResponse("A maximum of 500 photos can be excluded in one action."));
        }

        List<AssetRevisionId> revisionIds = [];
        HashSet<Guid> seenRevisionIds = [];
        foreach (string? value in request.RevisionIds)
        {
            if (!TryParseRevisionId(value, out AssetRevisionId revisionId))
            {
                return Results.BadRequest(new ArchiveErrorResponse("Every selected photo must have a valid revision identifier."));
            }

            if (seenRevisionIds.Add(revisionId.Value))
            {
                revisionIds.Add(revisionId);
            }
        }

        List<AssetRevisionLookup> resolved = [];
        foreach (AssetRevisionId revisionId in revisionIds)
        {
            AssetRevisionLookup? revision = await revisions.GetRevisionAsync(revisionId, cancellationToken);
            if (revision is null)
            {
                return Results.NotFound(new ArchiveErrorResponse(
                    "One or more selected photos no longer exist. Refresh the archive review and try again."));
            }

            resolved.Add(revision);
        }

        DateTimeOffset excludedAtUtc = timeProvider.GetUtcNow();
        List<ArchiveSourceCopyExclusionResponse> results = [];
        foreach (AssetRevisionLookup revision in resolved
                     .DistinctBy(static value => (value.SourceId, value.SourceKey)))
        {
            SourceCopyExclusionState state = await exclusions.ExcludeAsync(
                revision.SourceId,
                revision.SourceKey,
                excludedAtUtc,
                cancellationToken);
            results.Add(ToResponse(state));
        }

        return Results.Ok(new ArchiveBulkExclusionResponse(
            request.RevisionIds.Count,
            results.Count,
            results));
    }

    private static async Task<IResult> RetryExclusionAsync(
        ArchiveRetrySourceCopyPurgeRequest request,
        ISourceCopyExclusionRepository exclusions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryParseSourceLocator(request?.SourceId, request?.SourceKey, out SourceId sourceId, out string sourceKey))
        {
            return Results.BadRequest(new ArchiveErrorResponse("A valid source locator is required."));
        }

        SourceCopyExclusionState? current = await exclusions.GetAsync(sourceId, sourceKey, cancellationToken);
        if (current is null)
        {
            return Results.NotFound();
        }

        if (current.PurgeState == SourceCopyPurgeStates.Completed)
        {
            return Results.BadRequest(new ArchiveErrorResponse(
                "Purge already completed. Restore the source copy to re-include it."));
        }

        if (current.PurgeState == SourceCopyPurgeStates.Attempting)
        {
            return Results.BadRequest(new ArchiveErrorResponse("Purge is currently running."));
        }

        if (current.PurgeState == SourceCopyPurgeStates.Pending)
        {
            return Results.Ok(ToResponse(current));
        }

        SourceCopyExclusionState retried = await exclusions.ExcludeAsync(
            sourceId,
            sourceKey,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return Results.Ok(ToResponse(retried));
    }

    private static async Task<IResult> RestoreExclusionAsync(
        ArchiveRestoreSourceCopyRequest request,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        if (!TryParseSourceLocator(request?.SourceId, request?.SourceKey, out SourceId sourceId, out string sourceKey))
        {
            return Results.BadRequest(new ArchiveErrorResponse("A valid source identifier and source key are required."));
        }

        try
        {
            SourceCopyExclusionState? current = await exclusions.GetAsync(sourceId, sourceKey, cancellationToken);
            if (current is null)
            {
                return Results.NotFound();
            }

            if (current.PurgeState != SourceCopyPurgeStates.Completed)
            {
                return Results.Conflict(new ArchiveErrorResponse(
                    "Re-inclusion is available only after privacy purge completes successfully."));
            }

            bool restored = await exclusions.RestoreAsync(sourceId, sourceKey, cancellationToken);
            return restored ? Results.NoContent() : Results.NotFound();
        }
        catch (ArgumentException)
        {
            // Do not echo a private path back through the error payload.
            return Results.BadRequest(new ArchiveErrorResponse("The source key is invalid."));
        }
    }

    private static bool TryParseRevisionId(string? value, out AssetRevisionId revisionId)
    {
        revisionId = default;
        if (!Guid.TryParse(value, out Guid revisionGuid) || revisionGuid == Guid.Empty)
        {
            return false;
        }

        revisionId = AssetRevisionId.From(revisionGuid);
        return true;
    }

    private static bool TryParseSourceLocator(
        string? sourceIdValue,
        string? sourceKeyValue,
        out SourceId sourceId,
        out string sourceKey)
    {
        sourceId = default;
        sourceKey = string.Empty;
        if (!Guid.TryParse(sourceIdValue, out Guid sourceGuid) ||
            sourceGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(sourceKeyValue))
        {
            return false;
        }

        try
        {
            sourceId = SourceId.From(sourceGuid);
            sourceKey = SourceCopyLocator.NormalizeSourceKey(sourceKeyValue);
            return true;
        }
        catch (ArgumentException)
        {
            sourceId = default;
            sourceKey = string.Empty;
            return false;
        }
    }

    private static ArchiveItemPageResponse ToResponse(CatalogueArchiveItemPage page) =>
        new(
            page.Offset,
            page.Limit,
            page.Total,
            page.Items.Select(ToResponse).ToArray());

    private static ArchiveItemStatusResponse ToResponse(CatalogueArchiveItemStatus item) =>
        new(
            item.RelativePath,
            item.RevisionId?.ToString(),
            item.Availability,
            item.SourceVerificationState,
            item.AnalysisState,
            item.LastError);

    private static ArchiveSourceCopyExclusionResponse ToResponse(SourceCopyExclusionState state) => new(
        state.SourceId.ToString(),
        state.SourceKey,
        state.ExcludedAtUtc,
        state.LastSeenAtUtc,
        state.PurgeState,
        state.PurgeErrorCode,
        state.PurgeUpdatedAtUtc);

    private static IExactDuplicateRepository ResolveExactDuplicateRepository(IServiceProvider services)
    {
        if (services.GetService(typeof(PostgresCatalogueDatabase)) is PostgresCatalogueDatabase postgres)
        {
            return new PostgresExactDuplicateRepository(postgres);
        }

        if (services.GetService(typeof(SqliteCatalogueDatabase)) is SqliteCatalogueDatabase sqlite)
        {
            return new SqliteExactDuplicateRepository(sqlite);
        }

        throw new InvalidOperationException("No supported catalogue provider is configured.");
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
}
