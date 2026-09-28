namespace PhotoIdentity.Web;

public sealed record ArchiveStoragePolicyUpdateRequest(
    decimal MinimumFreeSpaceReserveGb,
    decimal MaximumManagedHydrationGb,
    int MaximumConcurrentOperations);

public sealed record ArchiveStoragePolicyUpdateResponse(
    decimal MinimumFreeSpaceReserveGb,
    decimal MaximumManagedHydrationGb,
    int MaximumConcurrentOperations,
    bool RestartRequired,
    string Message);
