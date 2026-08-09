using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Applies explicit verification-state transitions discovered outside a normal source scan, such
/// as an authoritative local byte/hash mismatch detected immediately before analysis or proxy use.
/// If the current revision owns managed hydration, ownership is first moved back to the source
/// asset so re-verification can transfer it to whichever revision SHA-256 establishes next.
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
        AssetRevisionId? latestRevisionId;
        await using (SqliteConnection readConnection = await _database.OpenConnectionAsync(cancellationToken))
        {
            using SqliteCommand read = readConnection.CreateCommand();
            read.CommandText = """
                SELECT revision.id
                FROM archive_source_observations AS observation
                LEFT JOIN asset_revisions AS revision
                    ON revision.id = (
                        SELECT candidate.id
                        FROM asset_revisions AS candidate
                        WHERE candidate.asset_id = observation.asset_id
                        ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                        LIMIT 1)
                WHERE observation.asset_id = $asset_id;
                """;
            read.Parameters.AddWithValue("$asset_id", assetId.ToString());
            object? value = await read.ExecuteScalarAsync(cancellationToken);
            if (value is null || value is DBNull)
            {
                throw new InvalidOperationException(
                    "The archive source observation was unavailable when content verification failed.");
            }

            latestRevisionId = value is string id
                ? AssetRevisionId.From(Guid.Parse(id))
                : null;
        }

        if (latestRevisionId is AssetRevisionId revisionId)
        {
            _ = await new SqliteArchiveHydrationIdentityTransferRepository(_database)
                .MoveRevisionLeaseToSourceAsync(
                    revisionId,
                    assetId,
                    observedAtUtc,
                    cancellationToken);
        }

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
