using PhotoIdentity.Core.Sources;
using PhotoIdentity.Web;

namespace PhotoIdentity.Api;

public static class ArchiveStorageEndpoints
{
    public static IEndpointRouteBuilder MapArchiveStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/configuration", GetConfigurationAsync);
        endpoints.MapGet("/api/archive/storage", GetStorageAsync);
        return endpoints;
    }

    private static async Task<IResult> GetConfigurationAsync(
        IArchiveCoverageRepository coverageRepository,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageState? configured = await coverageRepository.GetAsync(cancellationToken);
        if (configured is null)
        {
            return Results.Ok(new ArchiveConfigurationResponse(false, null, []));
        }

        string rootName = new DirectoryInfo(configured.Source.RootLocator).Name;
        return Results.Ok(new ArchiveConfigurationResponse(
            true,
            rootName,
            configured.IncludedFolders));
    }

    private static async Task<IResult> GetStorageAsync(
        ArchiveHydrationCapacityService capacity,
        ArchiveHydrationPolicyConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveStorageSnapshot value = await capacity.GetStorageSnapshotAsync(cancellationToken);
            return Results.Ok(new ArchiveStorageStatusResponse(
                value.ArchiveConfigured,
                value.PolicyConfigured,
                value.PolicyMessage,
                configuration.MinimumFreeSpaceReserveBytes,
                configuration.MaximumManagedHydrationBytes,
                configuration.MaximumConcurrentOperations,
                value.LogicalSourceBytes,
                value.AvailableFreeBytes,
                value.ManagedHydratedBytes,
                value.ManagedDownloadingBytes,
                value.ManagedReleasingBytes,
                value.ManagedReservedBytes,
                value.ActiveManagedOriginals,
                value.HydrationsInProgress,
                value.ReviewProxyBytes,
                value.ReviewProxyProfileId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Results.BadRequest(new ArchiveErrorResponse(exception.Message));
        }
    }
}
