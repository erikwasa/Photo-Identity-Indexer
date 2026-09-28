using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

public static class SourceCopyPurgeStates
{
    public const string Pending = "pending";
    public const string Attempting = "attempting";
    public const string Failed = "failed";
    public const string Completed = "completed";

    public static bool IsValid(string value) =>
        value is Pending or Attempting or Failed or Completed;
}

/// <summary>
/// Minimal durable privacy tombstone for one independently controlled source locator.
/// The tombstone intentionally contains no content hash, pixels, dimensions, photo metadata,
/// face data or identity data so it can survive the WI-0090 purge safely.
/// </summary>
public sealed record SourceCopyExclusionState(
    SourceId SourceId,
    string SourceKey,
    DateTimeOffset ExcludedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    string PurgeState,
    string? PurgeErrorCode,
    DateTimeOffset PurgeUpdatedAtUtc);

public interface ISourceCopyExclusionRepository
{
    Task<SourceCopyExclusionState?> GetAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceCopyExclusionState>> ListAsync(
        SourceId? sourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the privacy boundary durable before any purge work begins. Repeated exclusion is
    /// idempotent and preserves the first exclusion timestamp while resetting failed/completed
    /// cleanup to pending for the new purge request.
    /// </summary>
    Task<SourceCopyExclusionState> ExcludeAsync(
        SourceId sourceId,
        string sourceKey,
        DateTimeOffset excludedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly removes a fully purged locator tombstone so the source copy may be indexed again.
    /// Implementations must refuse restore while cleanup is pending, attempting or failed.
    /// </summary>
    Task<bool> RestoreAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records source presence without opening, hashing or analysing excluded content. Returns
    /// true when the locator is excluded and therefore must be skipped by normal scan persistence.
    /// </summary>
    Task<bool> RecordObservedIfExcludedAsync(
        SourceId sourceId,
        string sourceKey,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records source presence for every excluded locator in one scan batch and returns the
    /// excluded keys. Implementations should override this with a set-oriented operation; the
    /// default preserves compatibility for non-production adapters.
    /// </summary>
    async Task<IReadOnlySet<string>> RecordObservedAndGetExcludedAsync(
        SourceId sourceId,
        IReadOnlyCollection<string> sourceKeys,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceKeys);
        HashSet<string> excluded = new(StringComparer.Ordinal);
        foreach (string sourceKey in sourceKeys)
        {
            if (await RecordObservedIfExcludedAsync(
                    sourceId,
                    sourceKey,
                    observedAtUtc,
                    cancellationToken))
            {
                excluded.Add(sourceKey);
            }
        }

        return excluded;
    }

    Task SetPurgeStateAsync(
        SourceId sourceId,
        string sourceKey,
        string purgeState,
        string? errorCode,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> IsAssetExcludedAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default);

    Task<bool> IsRevisionExcludedAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the excluded subset of a revision batch. Production providers should override this
    /// with a set-oriented query; the default keeps non-production adapters source-compatible.
    /// </summary>
    async Task<IReadOnlySet<AssetRevisionId>> GetExcludedRevisionIdsAsync(
        IReadOnlyCollection<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        HashSet<AssetRevisionId> excluded = [];
        foreach (AssetRevisionId revisionId in revisionIds.Distinct())
        {
            if (await IsRevisionExcludedAsync(revisionId, cancellationToken))
            {
                excluded.Add(revisionId);
            }
        }

        return excluded;
    }

    Task<bool> IsFaceOccurrenceExcludedAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default);
}

public static class SourceCopyLocator
{
    public static string NormalizeSourceKey(string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        string normalized = sourceKey.Trim().Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('/');
        if (normalized.Length == 0 ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("Source-copy key must be a normalized relative locator.", nameof(sourceKey));
        }

        return normalized;
    }
}
