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
public sealed record ArchiveRestoreSourceCopyRequest(string? SourceId, string? SourceKey);

public static class ArchiveItemFilterEndpoints
{
    public static IEndpointRouteBuilder MapArchiveItemFilterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/items/filter", GetItemsAsync);
        endpoints.MapGet("/api/archive/exact-duplicates", GetExactDuplicatesAsync);
        endpoints.MapGet("/api/archive/exclusions", GetExclusionsAsync);
        endpoints.MapPost("/api/archive/exclusions/revision", ExcludeRevisionAsync);
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
                availability ?? "all",
                verification ?? "all",
                analysis ?? "all",
                offset ?? 0,
                limit ?? 50,
                cancellationToken);
            return Results.Ok(new ArchiveItemPageResponse(
                page.Offset,
                page.Limit,
                page.Total,
                page.Items.Select(item => new ArchiveItemStatusResponse(
                    item.RelativePath,
                    item.RevisionId?.ToString(),
                    item.Availability,
                    item.SourceVerificationState,
                    item.AnalysisState,
                    item.LastError)).ToArray()));
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new ArchiveErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> GetExactDuplicatesAsync(
        IArchiveCoverageRepository coverageRepository,
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

            return Results.Ok(groups.Select(group => new ArchiveExactDuplicateGroupResponse(
                group.ContentHash.ToString(),
                group.Copies.Select(copy => new ArchiveExactDuplicateCopyResponse(
                    copy.SourceId.ToString(),
                    copy.AssetId.ToString(),
                    copy.RevisionId.ToString(),
                    copy.SourceKey,
                    copy.IsMissing)).ToArray())).ToArray());
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
        if (request is null ||
            !Guid.TryParse(request.RevisionId, out Guid revisionGuid) ||
            revisionGuid == Guid.Empty)
        {
            return Results.BadRequest(new ArchiveErrorResponse("A valid revision identifier is required."));
        }

        AssetRevisionLookup? revision = await revisions.GetRevisionAsync(
            AssetRevisionId.From(revisionGuid),
            cancellationToken);
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

    private static async Task<IResult> RestoreExclusionAsync(
        ArchiveRestoreSourceCopyRequest request,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            !Guid.TryParse(request.SourceId, out Guid sourceGuid) ||
            sourceGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SourceKey))
        {
            return Results.BadRequest(new ArchiveErrorResponse("A valid source identifier and source key are required."));
        }

        try
        {
            bool restored = await exclusions.RestoreAsync(
                SourceId.From(sourceGuid),
                request.SourceKey,
                cancellationToken);
            return restored ? Results.NoContent() : Results.NotFound();
        }
        catch (ArgumentException)
        {
            // Do not echo a private path back through the error payload.
            return Results.BadRequest(new ArchiveErrorResponse("The source key is invalid."));
        }
    }

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
