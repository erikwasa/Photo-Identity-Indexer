using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CatalogueEvaluationScopeKinds
{
    public const string ProcessingRun = "processing-run";
    public const string AssetRevisions = "asset-revisions";
}

public sealed record CatalogueEvaluationScope
{
    private CatalogueEvaluationScope(
        string kind,
        ProcessingRunId? processingRunId,
        IReadOnlyList<AssetRevisionId> assetRevisionIds)
    {
        Kind = kind;
        ProcessingRunId = processingRunId;
        AssetRevisionIds = assetRevisionIds;
    }

    public string Kind { get; }
    public ProcessingRunId? ProcessingRunId { get; }
    public IReadOnlyList<AssetRevisionId> AssetRevisionIds { get; }

    public static CatalogueEvaluationScope ForRun(ProcessingRunId processingRunId) =>
        new(CatalogueEvaluationScopeKinds.ProcessingRun, processingRunId, []);

    public static CatalogueEvaluationScope ForRevisions(IReadOnlyList<AssetRevisionId> assetRevisionIds)
    {
        ArgumentNullException.ThrowIfNull(assetRevisionIds);
        AssetRevisionId[] distinct = assetRevisionIds
            .Distinct()
            .OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0)
        {
            throw new ArgumentException("At least one asset revision is required.", nameof(assetRevisionIds));
        }

        if (distinct.Length > 800)
        {
            throw new ArgumentException("An explicit evaluation scope cannot exceed 800 asset revisions.", nameof(assetRevisionIds));
        }

        return new CatalogueEvaluationScope(CatalogueEvaluationScopeKinds.AssetRevisions, null, distinct);
    }
}

public sealed record CatalogueEvaluationSourceRevision(
    AssetRevisionId Id,
    Sha256Digest ContentHash,
    DateTimeOffset? ProcessingStartedAtUtc,
    DateTimeOffset? ProcessingCompletedAtUtc);

public sealed record CatalogueEvaluationFace(
    FaceOccurrenceId Id,
    AssetRevisionId AssetRevisionId,
    int Ordinal,
    PersonId PersonId,
    ModelId DetectorModelId,
    Sha256Digest DetectorModelHash,
    ModelId EmbedderModelId,
    Sha256Digest EmbedderModelHash,
    int Dimensions,
    float[] Embedding);

public sealed record CatalogueEvaluationExportInput(
    CatalogueEvaluationScope Scope,
    IReadOnlyList<CatalogueEvaluationSourceRevision> SourceRevisions,
    IReadOnlyList<CatalogueEvaluationFace> Faces);
