using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persisted source configuration for a catalogue root.
/// </summary>
public sealed record CatalogueSource
{
    public CatalogueSource(
        SourceId id,
        string kind,
        string rootLocator,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Kind = Required(kind, nameof(kind));
        RootLocator = Required(rootLocator, nameof(rootLocator));
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public SourceId Id { get; }
    public string Kind { get; }
    public string RootLocator { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>
/// Stable source-owned asset identity and its latest scan-presence state.
/// </summary>
public sealed record CatalogueAsset
{
    public CatalogueAsset(
        AssetId id,
        SourceId sourceId,
        string sourceKey,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? lastSeenAtUtc = null,
        DateTimeOffset? deletedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        DateTimeOffset created = createdAtUtc.ToUniversalTime();
        DateTimeOffset lastSeen = (lastSeenAtUtc ?? createdAtUtc).ToUniversalTime();
        DateTimeOffset? deleted = deletedAtUtc?.ToUniversalTime();
        if (lastSeen < created)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastSeenAtUtc),
                "The last-seen time cannot precede asset creation.");
        }

        if (deleted < created)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletedAtUtc),
                "The deletion time cannot precede asset creation.");
        }

        Id = id;
        SourceId = sourceId;
        SourceKey = sourceKey.Trim();
        CreatedAtUtc = created;
        LastSeenAtUtc = lastSeen;
        DeletedAtUtc = deleted;
    }

    public AssetId Id { get; }
    public SourceId SourceId { get; }
    public string SourceKey { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset LastSeenAtUtc { get; }
    public DateTimeOffset? DeletedAtUtc { get; }
    public bool IsDeleted => DeletedAtUtc.HasValue;
}

/// <summary>
/// Immutable content revision observed for an asset.
/// </summary>
public sealed record CatalogueAssetRevision
{
    public CatalogueAssetRevision(
        AssetRevisionId id,
        AssetId assetId,
        Sha256Digest contentHash,
        long sizeBytes,
        DateTimeOffset observedAtUtc,
        string? mediaType = null,
        int? width = null,
        int? height = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        if ((width is null) != (height is null))
        {
            throw new ArgumentException("Width and height must either both be supplied or both be omitted.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive when supplied.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive when supplied.");
        }

        Id = id;
        AssetId = assetId;
        ContentHash = contentHash;
        SizeBytes = sizeBytes;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        MediaType = string.IsNullOrWhiteSpace(mediaType) ? null : mediaType.Trim();
        Width = width;
        Height = height;
    }

    public AssetRevisionId Id { get; }
    public AssetId AssetId { get; }
    public Sha256Digest ContentHash { get; }
    public long SizeBytes { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public string? MediaType { get; }
    public int? Width { get; }
    public int? Height { get; }
}
