using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

public sealed record CatalogueArchiveFolderStatus(
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

public sealed record CatalogueArchiveItemStatus(
    string RelativePath,
    AssetRevisionId? RevisionId,
    string Availability,
    string SourceVerificationState,
    string AnalysisState,
    string? LastError);

public sealed record CatalogueArchiveItemPage(
    int Offset,
    int Limit,
    int Total,
    IReadOnlyList<CatalogueArchiveItemStatus> Items);

public sealed record CatalogueArchiveRunStatus(
    ProcessingRunId RunId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalJobs,
    int QueuedJobs,
    int RunningJobs,
    int SucceededJobs,
    int FailedJobs,
    int CancelledJobs);

/// <summary>
/// Reads permanent-archive status and item paging without exposing the configured source root.
/// </summary>
public interface IArchiveStatusRepository
{
    Task<CatalogueArchiveFolderStatus> GetStatusAsync(
        SourceId sourceId,
        string relativeFolder,
        Sha256Digest? profileHash,
        CancellationToken cancellationToken = default);

    Task<CatalogueArchiveItemPage> GetItemsAsync(
        SourceId sourceId,
        string relativeFolder,
        Sha256Digest? profileHash,
        string state,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CatalogueArchiveItemPage> GetItemsAsync(
        SourceId sourceId,
        string relativeFolder,
        Sha256Digest? profileHash,
        string availability,
        string verification,
        string analysis,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CatalogueArchiveRunStatus?> GetLatestRunAsync(
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default);
}
