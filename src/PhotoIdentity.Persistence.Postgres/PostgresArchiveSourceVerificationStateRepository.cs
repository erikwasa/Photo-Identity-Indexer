using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Applies explicit verification-state transitions discovered outside a normal source scan, such
/// as an authoritative local byte/hash mismatch detected immediately before analysis or proxy use.
/// If any revision for the source asset owns managed hydration, ownership is first moved back to
/// the source asset so re-verification can transfer it to whichever revision SHA-256 establishes.
/// </summary>
public sealed class PostgresArchiveSourceVerificationStateRepository : IArchiveSourceVerificationStateRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveSourceVerificationStateRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task MarkNeedsVerificationAsync(
        AssetId assetId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {

        await using (NpgsqlConnection readConnection = await _database.OpenConnectionAsync(cancellationToken))
        {
            using NpgsqlCommand read = readConnection.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM archive_source_observations WHERE asset_id = @asset_id;";
            read.Parameters.AddWithValue("@asset_id", Guid.Parse(assetId.ToString()));
            long count = (long)(await read.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (count == 0)
            {
                throw new InvalidOperationException(
                    "The archive source observation was unavailable when content verification failed.");
            }
        }

        _ = await new PostgresArchiveHydrationIdentityTransferRepository(_database)
            .MoveActiveRevisionLeaseToSourceAsync(
                assetId,
                observedAtUtc,
                cancellationToken);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE archive_source_observations
            SET verification_state = 'needs-source-verification',
                observed_at_utc = @observed_at_utc
            WHERE asset_id = @asset_id;
            """;
        command.Parameters.AddWithValue("@asset_id", Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "@observed_at_utc",
            observedAtUtc.ToUniversalTime());
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                "The archive source observation was unavailable when content verification failed.");
        }
    }
}
