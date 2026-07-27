using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePersonMaintenancePerson(
    PersonId Id,
    string DisplayName,
    int LabelCount,
    int SuggestionCount);

public sealed record CataloguePersonMaintenanceAction(
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
