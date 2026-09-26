using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

public sealed record SourceCopyPurgeRoots(
    string ArchiveAnalysisRoot,
    string? ReviewProxyRoot,
    string DetectorEvaluationRoot)
{
    public string NormalizedArchiveAnalysisRoot => Path.GetFullPath(ArchiveAnalysisRoot);
    public string? NormalizedReviewProxyRoot => string.IsNullOrWhiteSpace(ReviewProxyRoot)
        ? null
        : Path.GetFullPath(ReviewProxyRoot);
    public string NormalizedDetectorEvaluationRoot => Path.GetFullPath(DetectorEvaluationRoot);
}

public sealed record SourceCopyPurgeArtifact(
    string AbsolutePath,
    bool IsDirectory);

/// <summary>
/// Durable inventory of filesystem artifacts that must be removed before the catalogue rows for an
/// excluded locator are discarded. The manifest is operational purge state only and is deleted when
/// cleanup completes.
/// </summary>
public sealed record SourceCopyPurgeManifest(
    SourceId SourceId,
    string SourceKey,
    DateTimeOffset PreparedAtUtc,
    IReadOnlyList<SourceCopyPurgeArtifact> Artifacts);

public interface ISourceCopyPurgeRepository
{
    /// <summary>
    /// Returns the existing durable manifest or atomically discovers and stores the complete known
    /// derivative inventory before any filesystem deletion is permitted.
    /// </summary>
    Task<SourceCopyPurgeManifest> PrepareManifestAsync(
        SourceId sourceId,
        string sourceKey,
        SourceCopyPurgeRoots roots,
        DateTimeOffset preparedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the source copy's catalogue asset and all revision-linked state through database
    /// cascade semantics. Repeated calls are idempotent.
    /// </summary>
    Task PurgeCatalogueAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the temporary purge manifest after filesystem and catalogue cleanup have both been
    /// verified. The source-copy exclusion tombstone is intentionally retained.
    /// </summary>
    Task ClearManifestAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default);
}
