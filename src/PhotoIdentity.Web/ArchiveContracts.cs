namespace PhotoIdentity.Web;

public sealed record ArchiveFolderStatusResponse(
    string RelativeFolder,
    int CurrentImages,
    int LocalImages,
    int OnlineOnlyImages,
    int DownloadingImages,
    int UnavailableImages,
    int AvailabilityErrorImages,
    int AnalysedImages,
    int PendingImages,
    int FailedImages,
    int MissingImages);

public sealed record ArchiveItemStatusResponse(
    string RelativePath,
    string Availability,
    string AnalysisState,
    string? LastError);

public sealed record ArchiveItemPageResponse(
    int Offset,
    int Limit,
    int Total,
    IReadOnlyList<ArchiveItemStatusResponse> Items);

public sealed record ArchiveRunStatusResponse(
    string RunId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalJobs,
    int QueuedJobs,
    int RunningJobs,
    int SucceededJobs,
    int FailedJobs,
    int CancelledJobs);

public sealed record ArchiveStatusResponse(
    bool Configured,
    string? RootName,
    IReadOnlyList<string> IncludedFolders,
    bool AnalysisReady,
    string? AnalysisMessage,
    string? ProfileHash,
    string DetectorModelId,
    double DetectorConfidence,
    string EmbedderModelId,
    ArchiveFolderStatusResponse Totals,
    IReadOnlyList<ArchiveFolderStatusResponse> Folders,
    ArchiveRunStatusResponse? LatestRun);

public sealed record ArchiveStorageStatusResponse(
    bool ArchiveConfigured,
    bool PolicyConfigured,
    string? PolicyMessage,
    long? MinimumFreeSpaceReserveBytes,
    long? MaximumManagedHydrationBytes,
    int? MaximumConcurrentOperations,
    long LogicalSourceBytes,
    long? AvailableFreeBytes,
    long ManagedHydratedBytes,
    long ManagedDownloadingBytes,
    long ManagedReleasingBytes,
    long ManagedReservedBytes,
    int ActiveManagedOriginals,
    int HydrationsInProgress,
    long ReviewProxyBytes,
    string? ReviewProxyProfileId);

public sealed record ArchiveIncludeRequest(
    string? RootPath,
    string RelativeFolder);

public sealed record ArchiveSyncResponse(
    int SupportedFiles,
    int LocalFiles,
    int OnlineOnlyFiles,
    int DownloadingFiles,
    int UnavailableFiles,
    int AvailabilityErrors,
    int NewRevisions,
    int UnchangedFiles,
    int MarkedMissing,
    ArchiveStatusResponse Status);

public sealed record ArchiveAnalysisStepResponse(
    bool StartedNewRun,
    ArchiveStatusResponse Status);

public sealed record ArchiveErrorResponse(string Error);
