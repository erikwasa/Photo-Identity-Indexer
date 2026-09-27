using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Transitional test-fixture DTOs retained only to avoid rewriting historical integration setup
/// code while its persistence implementation moves to PostgreSQL. These types do not use SQLite.
/// WI-0149 removes the remaining legacy aliases at the integration-test boundary.
/// </summary>
public sealed record CatalogueSource
{
    public CatalogueSource(SourceId id, string kind, string rootLocator, DateTimeOffset createdAtUtc)
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
            throw new ArgumentOutOfRangeException(nameof(lastSeenAtUtc));
        }
        if (deleted < created)
        {
            throw new ArgumentOutOfRangeException(nameof(deletedAtUtc));
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
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
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

public sealed record CatalogueFaceOccurrence
{
    public CatalogueFaceOccurrence(
        FaceOccurrenceId id,
        AssetRevisionId assetRevisionId,
        int ordinal,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        Id = id;
        AssetRevisionId = assetRevisionId;
        Ordinal = ordinal;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceOccurrenceId Id { get; }
    public AssetRevisionId AssetRevisionId { get; }
    public int Ordinal { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

public sealed record CatalogueFaceObservation
{
    public CatalogueFaceObservation(
        FaceOccurrenceId faceOccurrenceId,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        double confidence,
        NormalizedBoundingBox boundingBox,
        NormalizedFaceLandmarks landmarks,
        DateTimeOffset observedAtUtc)
    {
        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }
        FaceOccurrenceId = faceOccurrenceId;
        DetectorModelId = detectorModelId;
        DetectorModelHash = detectorModelHash;
        Confidence = confidence;
        BoundingBox = boundingBox;
        Landmarks = landmarks;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    public FaceOccurrenceId FaceOccurrenceId { get; }
    public ModelId DetectorModelId { get; }
    public Sha256Digest DetectorModelHash { get; }
    public double Confidence { get; }
    public NormalizedBoundingBox BoundingBox { get; }
    public NormalizedFaceLandmarks Landmarks { get; }
    public DateTimeOffset ObservedAtUtc { get; }
}

public sealed record CatalogueFaceCrop
{
    public CatalogueFaceCrop(
        FaceCropId id,
        FaceOccurrenceId faceOccurrenceId,
        AlignmentProtocolId protocol,
        Sha256Digest contentHash,
        string storagePath,
        int width,
        int height,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Id = id;
        FaceOccurrenceId = faceOccurrenceId;
        Protocol = protocol;
        ContentHash = contentHash;
        StoragePath = storagePath.Trim();
        Width = width;
        Height = height;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceCropId Id { get; }
    public FaceOccurrenceId FaceOccurrenceId { get; }
    public AlignmentProtocolId Protocol { get; }
    public Sha256Digest ContentHash { get; }
    public string StoragePath { get; }
    public int Width { get; }
    public int Height { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

public sealed record CatalogueFaceEmbedding
{
    public CatalogueFaceEmbedding(
        FaceCropId faceCropId,
        ModelId modelId,
        Sha256Digest modelHash,
        EmbeddingVector vector,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(vector);
        FaceCropId = faceCropId;
        ModelId = modelId;
        ModelHash = modelHash;
        Vector = vector;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceCropId FaceCropId { get; }
    public ModelId ModelId { get; }
    public Sha256Digest ModelHash { get; }
    public EmbeddingVector Vector { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

public sealed record CatalogueFaceInspection(
    CatalogueFaceOccurrence Occurrence,
    CatalogueFaceObservation Observation,
    CatalogueFaceCrop Crop,
    CatalogueFaceEmbedding Embedding);
