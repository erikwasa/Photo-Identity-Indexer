using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Review;

public static class PersonMaintenanceActionKinds
{
    public const string Rename = "rename";
    public const string Merge = "merge";
}

public sealed record PersonMaintenancePerson(
    PersonId Id,
    string DisplayName,
    int LabelCount,
    int SuggestionCount);

public sealed record PersonMaintenanceAction(
    long Id,
    string Kind,
    PersonId PersonId,
    string PreviousDisplayName,
    PersonId? TargetPersonId,
    string NewDisplayName,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    bool Reversible);

public interface IPersonMaintenanceRepository
{
    Task<IReadOnlyList<PersonMaintenancePerson>> GetPeopleAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonMaintenanceAction>> GetHistoryAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<PersonMaintenanceAction> RenameAsync(
        PersonId personId,
        string displayName,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<PersonMaintenanceAction> MergeAsync(
        PersonId sourcePersonId,
        PersonId targetPersonId,
        bool confirmIrreversible,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);
}
