using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Applies explicit verification-state transitions discovered outside a normal source scan, such
/// as an authoritative local byte/hash mismatch detected immediately before analysis or proxy use.
/// </summary>
public sealed class SqliteArchiveSourceVerificationStateRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveSourceVerificationStateRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task MarkNeedsVerificationAsync(
        AssetId assetId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await new SqliteArchiveSourceObservationRepository(_database).EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE archive_source_observations
            SET verification_state = 'needs-source-verification',
                observed_at_utc = $observed_at_utc
            WHERE asset_id = $asset_id;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue(
            "$observed_at_utc",
            observedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                "The archive source observation was unavailable when content verification failed.");
        }
    }
}
