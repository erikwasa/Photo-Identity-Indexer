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
    int NeedsSourceVerificationImages,
    int UnverifiedSourceImages,
    int MissingImages);

public sealed record ArchiveItemStatusResponse(
    string RelativePath,
    string? RevisionId,
    string Availability,
    string SourceVerificationState,
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

public sealed record ArchiveAdvancementStatusResponse(
    string State,
    bool IsRunning,
    string? Message,
    DateTimeOffset? UpdatedAtUtc);

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
    ArchiveRunStatusResponse? LatestRun,
    ArchiveAdvancementStatusResponse? Advancement);

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

public sealed record ArchiveCoverageUpdateRequest(
    IReadOnlyList<string> IncludedFolders);

public sealed record ArchiveSyncResponse(
    int SupportedFiles,
    int LocalFiles,
    int OnlineOnlyFiles,
    int DownloadingFiles,
    int UnavailableFiles,
    int AvailabilityErrors,
    int NewRevisions,
    int UnchangedFiles,
    int VerifiedSources,
    int NeedsSourceVerification,
    int UnverifiedSources,
    int MarkedMissing,
    ArchiveStatusResponse Status);

public sealed record ArchiveAnalysisStepResponse(
    bool StartedNewRun,
    ArchiveStatusResponse Status);

public sealed record ArchiveErrorResponse(string Error);


public sealed record ArchiveThroughputStageMetricResponse(
    string Name,
    long Count,
    double TotalMilliseconds,
    double AverageMilliseconds,
    double MaxMilliseconds);

public sealed record ArchiveThroughputCounterMetricResponse(
    string Name,
    long Value);

public sealed record ArchiveThroughputHashReadMetricResponse(
    string Kind,
    long Count,
    long Bytes,
    int SubjectCount,
    double AverageReadsPerSubject,
    long MaxReadsPerSubject);

public sealed record ArchiveThroughputDiagnosticsResponse(
    long Generation,
    DateTimeOffset ResetAtUtc,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ArchiveThroughputStageMetricResponse> Stages,
    IReadOnlyList<ArchiveThroughputCounterMetricResponse> Counters,
    IReadOnlyList<ArchiveThroughputHashReadMetricResponse> HashReads);
