using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

/// <summary>Read current human-review state and active people independently of storage.</summary>
public interface IReviewFaceRepository
{
    Task<CatalogueReviewFace?> GetFaceAsync(FaceOccurrenceId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogueReviewPerson>> GetPeopleAsync(CancellationToken cancellationToken = default);
}

/// <summary>Scoped review gallery paging, navigation and available filters.</summary>
public interface IReviewFilterRepository
{
    Task<CatalogueReviewFacePage> GetFacesAsync(
        int offset = 0, int limit = 40, string state = CatalogueReviewStates.Unreviewed,
        ProcessingRunId? processingRunId = null, ModelId? modelId = null, Sha256Digest? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending, CancellationToken cancellationToken = default);
    Task<CatalogueReviewFaceNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId, string state = "all",
        ProcessingRunId? processingRunId = null, ModelId? modelId = null, Sha256Digest? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending, CancellationToken cancellationToken = default);
    Task<CatalogueReviewFilterOptions> GetOptionsAsync(CancellationToken cancellationToken = default);
}
