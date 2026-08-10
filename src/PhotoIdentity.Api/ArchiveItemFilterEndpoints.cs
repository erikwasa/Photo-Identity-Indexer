using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public static class ArchiveItemFilterEndpoints
{
    public static IEndpointRouteBuilder MapArchiveItemFilterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/items/filter", GetItemsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetItemsAsync(
        string? folder,
        string? availability,
        string? verification,
        string? analysis,
        int? offset,
        int? limit,
        SqliteCatalogueDatabase database,
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveCoverageConfiguration configured = await new SqliteArchiveCoverageRepository(database)
                .GetAsync(cancellationToken)
                ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");
            Sha256Digest? profileHash = await ResolveProfileHashAsync(
                configured,
                operatorConfiguration,
                cancellationToken);
            CatalogueArchiveItemPage page = await new SqliteArchiveItemFilterRepository(database).GetItemsAsync(
                configured.Source.Id,
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

    private static async Task<Sha256Digest?> ResolveProfileHashAsync(
        ArchiveCoverageConfiguration configured,
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
