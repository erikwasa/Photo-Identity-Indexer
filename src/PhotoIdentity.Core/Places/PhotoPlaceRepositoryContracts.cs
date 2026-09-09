using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Places;

public sealed record PhotoPlaceDefinition(
    long TagId,
    string Value,
    string Name,
    long? ParentTagId,
    string? ParentValue);

public sealed record PhotoPlaceAssignment(
    long TagId,
    string Value,
    string Name,
    string SourceKind,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoPlaceMigrationConflict(
    AssetRevisionId RevisionId,
    IReadOnlyList<string> CandidateValues,
    DateTimeOffset DetectedAtUtc);

public sealed record PhotoPlaceState(
    AssetRevisionId RevisionId,
    PhotoPlaceAssignment? Place,
    PhotoPlaceMigrationConflict? MigrationConflict);

public sealed record AutomaticPhotoPlaceEligibility(
    bool Allowed,
    bool BlockedByManual,
    bool BlockedByConflict);

public sealed record AutomaticPhotoPlaceWriteResult(
    PhotoPlaceState State,
    bool Applied,
    bool BlockedByManual,
    bool BlockedByConflict);

public interface IPhotoPlaceRepository
{
    Task<IReadOnlyList<PhotoPlaceDefinition>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default);

    Task<PhotoPlaceState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoPlaceMigrationConflict>> GetMigrationConflictsAsync(
        CancellationToken cancellationToken = default);

    Task<PhotoPlaceState> SetManualPlaceAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PhotoPlaceState> ClearManualPlaceAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default);
}

public interface IAutomaticPhotoPlaceRepository
{
    Task<AutomaticPhotoPlaceEligibility> GetEligibilityAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<AutomaticPhotoPlaceWriteResult> TrySetAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string provider,
        string actor,
        CancellationToken cancellationToken = default);
}
