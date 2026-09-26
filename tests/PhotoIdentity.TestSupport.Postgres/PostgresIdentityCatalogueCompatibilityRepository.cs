using Npgsql;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Read-only compatibility view over identity labels used by mature integration assertions.
/// Canonical writes continue through the provider-neutral review contracts.
/// </summary>
public sealed class PostgresIdentityCatalogueCompatibilityRepository
{
    private readonly PostgresTestCatalogueDatabase _database;

    public PostgresIdentityCatalogueCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<CatalogueHumanLabel>> GetHumanLabelsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note
            FROM person_labels
            WHERE face_occurrence_id = @face_occurrence_id
            ORDER BY assigned_at_utc DESC, id DESC;
            """;
        command.Parameters.AddWithValue("face_occurrence_id", Guid.Parse(faceOccurrenceId.ToString()));

        List<CatalogueHumanLabel> labels = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            labels.Add(new CatalogueHumanLabel(
                reader.GetInt64(0),
                PersonId.From(reader.GetGuid(1)),
                FaceOccurrenceId.From(reader.GetGuid(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5).ToUniversalTime(),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return labels;
    }
}
