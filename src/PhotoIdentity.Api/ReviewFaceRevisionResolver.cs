using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

/// <summary>
/// Resolves a face occurrence to the opaque immutable asset revision that contains it.
/// Browser-facing callers receive only the revision identifier, never a source path.
/// </summary>
public sealed class ReviewFaceRevisionResolver
{
    private readonly SqliteCatalogueDatabase _database;

    public ReviewFaceRevisionResolver(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<AssetRevisionId?> ResolveAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id
            FROM face_occurrences
            WHERE id = $face_occurrence_id;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string revisionId &&
               Guid.TryParse(revisionId, out Guid parsed) &&
               parsed != Guid.Empty
            ? AssetRevisionId.From(parsed)
            : null;
    }
}
