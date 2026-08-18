using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePersonRepresentativeFace(
    PersonId PersonId,
    FaceOccurrenceId FaceId,
    bool IsExplicit);
