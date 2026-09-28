using PhotoIdentity.Core.Sources;
using PhotoIdentity.Web;

namespace PhotoIdentity.Api;

public static class ArchiveStorageEndpoints
{
    public static IEndpointRouteBuilder MapArchiveStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/configuration", GetConfigurationAsync);
        endpoints.MapGet("/api/archive/storage", GetStorageAsync);
        endpoints.MapPut("/api/archive/storage/policy", UpdateStoragePolicyAsync);
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

    private static async Task<IResult> UpdateStoragePolicyAsync(
        ArchiveStoragePolicyUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            LauncherSettingsStore store = new();
            await store.SaveArchiveHydrationPolicyAsync(
                request.MinimumFreeSpaceReserveGb,
                request.MaximumManagedHydrationGb,
                request.MaximumConcurrentOperations,
                cancellationToken);

            return Results.Ok(new ArchiveStoragePolicyUpdateResponse(
                request.MinimumFreeSpaceReserveGb,
                request.MaximumManagedHydrationGb,
                request.MaximumConcurrentOperations,
                true,
                "Storage policy saved. Restart Photo Identity to apply the new limits."));
        }
        catch (Exception exception) when (exception is
            ArgumentOutOfRangeException or
            OverflowException or
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            return Results.BadRequest(new ArchiveErrorResponse(exception.Message));
        }
    }
}
