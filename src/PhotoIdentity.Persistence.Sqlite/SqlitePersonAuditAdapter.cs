using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Compatibility adapter that preserves the existing SQLite person-audit query while exposing the provider-neutral contract.
/// </summary>
public sealed class SqlitePersonAuditAdapter : IPersonAuditRepository
{
    private readonly SqlitePersonAuditRepository _repository;

    public SqlitePersonAuditAdapter(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _repository = new SqlitePersonAuditRepository(database);
    }

    public async Task<PersonAuditPage?> GetFacesAsync(
        PersonId personId,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        int offset = 0,
        int limit = 40,
        bool disagreementsOnly = false,
        string sort = PersonAuditSorts.AssignedDescending,
        CancellationToken cancellationToken = default)
    {
        CataloguePersonAuditPage? page = await _repository.GetFacesAsync(
            personId,
            modelId,
            modelHash,
            offset,
            limit,
            disagreementsOnly,
            sort,
            cancellationToken);
        return page is null ? null : ToCorePage(page);
    }

    private static PersonAuditPage ToCorePage(CataloguePersonAuditPage page) => new(
        new ReviewPerson(page.Person.Id, page.Person.DisplayName),
        page.Items.Select(ToCoreFace).ToArray(),
        page.Offset,
        page.Limit,
        page.Total,
        page.DisagreementCount,
        page.Sort);

    private static PersonAuditFace ToCoreFace(CataloguePersonAuditFace face) => new(
        face.Id,
        face.Ordinal,
        face.FaceCreatedAtUtc,
        face.AssignedAtUtc,
        face.PhotoName,
        face.MediaType,
        face.PhotoWidth,
        face.PhotoHeight,
        face.RevisionHash,
        face.CropStoragePath,
        face.Confidence,
        face.AssignmentActionId,
        new ReviewPerson(face.AssignedPerson.Id, face.AssignedPerson.DisplayName),
        face.TopSuggestion is null
            ? null
            : new PersonAuditTopSuggestion(
                face.TopSuggestion.Id,
                new ReviewPerson(
                    face.TopSuggestion.Person.Id,
                    face.TopSuggestion.Person.DisplayName),
                face.TopSuggestion.ModelId,
                face.TopSuggestion.ModelHash,
                face.TopSuggestion.Rank,
                face.TopSuggestion.Score,
                face.TopSuggestion.ScoreMargin,
                face.TopSuggestion.Status,
                face.TopSuggestion.GeneratedAtUtc),
        face.SuggestionDisagrees);
}
