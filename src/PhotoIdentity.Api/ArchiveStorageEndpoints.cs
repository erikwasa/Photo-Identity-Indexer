using PhotoIdentity.Web;

namespace PhotoIdentity.Api;

public static class ArchiveStorageEndpoints
{
    public static IEndpointRouteBuilder MapArchiveStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/storage", GetStorageAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStorageAsync(
        ArchiveHydrationCapacityService capacity,
        CancellationToken cancellationToken)
    {
        try
        {
            ArchiveStorageSnapshot value = await capacity.GetStorageSnapshotAsync(cancellationToken);
            return Results.Ok(new ArchiveStorageStatusResponse(
                value.ArchiveConfigured,
                value.PolicyConfigured,
                value.PolicyMessage,
                value.MinimumFreeSpaceReserveBytes,
                value.MaximumManagedHydrationBytes,
                value.MaximumConcurrentOperations,
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
