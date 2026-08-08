namespace PhotoIdentity.Web;

public sealed record ArchiveFolderStatusResponse(
    string RelativeFolder,
    int CurrentImages,
    int AnalysedImages,
    int PendingImages,
    int FailedImages,
    int MissingImages);

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

public sealed record ArchiveIncludeRequest(
    string? RootPath,
    string RelativeFolder);

public sealed record ArchiveSyncResponse(
    int SupportedFiles,
    int NewRevisions,
    int UnchangedFiles,
    int MarkedMissing,
    ArchiveStatusResponse Status);

public sealed record ArchiveAnalysisStepResponse(
    bool StartedNewRun,
    ArchiveStatusResponse Status);
