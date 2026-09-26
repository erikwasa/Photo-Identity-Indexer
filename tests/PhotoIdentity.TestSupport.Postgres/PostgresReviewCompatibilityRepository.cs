using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Preserves the legacy combined review fixture API while delegating to the current PostgreSQL
/// action/query repositories. Production composition remains on the provider-neutral contracts.
/// </summary>
public sealed class PostgresReviewCompatibilityRepository : IReviewActionRepository, IReviewFaceRepository
{
    private readonly PostgresReviewActionRepository _actions;
    private readonly PostgresReviewQueryRepository _queries;

    public PostgresReviewCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _actions = new PostgresReviewActionRepository(database.Database);
        _queries = new PostgresReviewQueryRepository(database.Database);
    }

    public Task<CatalogueReviewFacePage> GetFacesAsync(
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        CancellationToken cancellationToken = default) =>
        _queries.GetFacesAsync(offset, limit, state, cancellationToken: cancellationToken);

    public Task<CatalogueReviewFace?> GetFaceAsync(
        FaceOccurrenceId id,
        CancellationToken cancellationToken = default) =>
        _queries.GetFaceAsync(id, cancellationToken);

    public Task<IReadOnlyList<CatalogueReviewPerson>> GetPeopleAsync(
        CancellationToken cancellationToken = default) =>
        _queries.GetPeopleAsync(cancellationToken);

    public async Task<CatalogueReviewPerson> CreatePersonAsync(
        string displayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default) =>
        ToCatalogue(await _actions.CreatePersonAsync(displayName, createdAtUtc, cancellationToken));

    public async Task<CatalogueReviewAction> AssignAsync(
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        ToCatalogue(await _actions.AssignAsync(
            faceOccurrenceId,
            personId,
            actor,
            createdAtUtc,
            note,
            cancellationToken));

    public async Task<CatalogueReviewAction> MarkUnknownAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        ToCatalogue(await _actions.MarkUnknownAsync(
            faceOccurrenceId,
            actor,
            createdAtUtc,
            note,
            cancellationToken));

    public async Task<CatalogueReviewAction> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        ToCatalogue(await _actions.RejectAsync(
            faceOccurrenceId,
            actor,
            createdAtUtc,
            note,
            cancellationToken));

    public async Task<CatalogueReviewAction?> UndoLatestAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ReviewAction? action = await _actions.UndoLatestAsync(
            faceOccurrenceId,
            actor,
            createdAtUtc,
            note,
            cancellationToken);
        return action is null ? null : ToCatalogue(action);
    }

    public async Task<IReadOnlyList<CatalogueReviewAction>> GetActionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default) =>
        (await _actions.GetActionsAsync(faceOccurrenceId, cancellationToken))
            .Select(ToCatalogue)
            .ToArray();

    Task<ReviewPerson> IReviewActionRepository.CreatePersonAsync(
        string displayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken) =>
        _actions.CreatePersonAsync(displayName, createdAtUtc, cancellationToken);

    Task<ReviewAction> IReviewActionRepository.AssignAsync(
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note,
        CancellationToken cancellationToken) =>
        _actions.AssignAsync(faceOccurrenceId, personId, actor, createdAtUtc, note, cancellationToken);

    Task<ReviewAction> IReviewActionRepository.MarkUnknownAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note,
        CancellationToken cancellationToken) =>
        _actions.MarkUnknownAsync(faceOccurrenceId, actor, createdAtUtc, note, cancellationToken);

    Task<ReviewAction> IReviewActionRepository.RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note,
        CancellationToken cancellationToken) =>
        _actions.RejectAsync(faceOccurrenceId, actor, createdAtUtc, note, cancellationToken);

    Task<ReviewAction?> IReviewActionRepository.UndoLatestAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note,
        CancellationToken cancellationToken) =>
        _actions.UndoLatestAsync(faceOccurrenceId, actor, createdAtUtc, note, cancellationToken);

    Task<IReadOnlyList<ReviewAction>> IReviewActionRepository.GetActionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken) =>
        _actions.GetActionsAsync(faceOccurrenceId, cancellationToken);

    private static CatalogueReviewPerson ToCatalogue(ReviewPerson person) =>
        new(person.Id, person.DisplayName);

    private static CatalogueReviewAction ToCatalogue(ReviewAction action) =>
        new(
            action.Id,
            action.FaceOccurrenceId,
            action.Kind,
            action.PersonId,
            action.PersonDisplayName,
            action.PersonLabelId,
            action.Actor,
            action.Note,
            action.CreatedAtUtc,
            action.ReversedAtUtc,
            action.ReversesActionId);
}
