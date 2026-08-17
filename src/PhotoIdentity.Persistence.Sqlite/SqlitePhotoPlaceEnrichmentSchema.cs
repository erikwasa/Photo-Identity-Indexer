using Microsoft.Data.Sqlite;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Idempotent persistence guard for reverse-geocoding cache and per-revision attempts.
/// The structures are catalogue-only and reference immutable revision IDs and persisted GPS.
/// </summary>
public static class SqlitePhotoPlaceEnrichmentSchema
{
    public static async Task EnsureAsync(
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await EnsureAsync(connection, cancellationToken);
    }

    internal static async Task EnsureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_place_reverse_geocode_cache (
                provider TEXT NOT NULL,
                contract_key TEXT NOT NULL,
                latitude REAL NOT NULL CHECK (latitude BETWEEN -90 AND 90),
                longitude REAL NOT NULL CHECK (longitude BETWEEN -180 AND 180),
                place_value TEXT NOT NULL,
                provider_result_id TEXT NULL,
                country_code TEXT NULL,
                resolved_at_utc TEXT NOT NULL,
                PRIMARY KEY (provider, contract_key, latitude, longitude),
                CHECK (length(provider) BETWEEN 1 AND 80),
                CHECK (length(contract_key) BETWEEN 1 AND 500),
                CHECK (length(place_value) BETWEEN 1 AND 80)
            );

            CREATE TABLE IF NOT EXISTS photo_place_enrichment_attempts (
                asset_revision_id TEXT NOT NULL,
                provider TEXT NOT NULL,
                contract_key TEXT NOT NULL,
                latitude REAL NOT NULL CHECK (latitude BETWEEN -90 AND 90),
                longitude REAL NOT NULL CHECK (longitude BETWEEN -180 AND 180),
                status TEXT NOT NULL CHECK (status IN ('succeeded', 'deferred', 'failed')),
                attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
                place_value TEXT NULL,
                provider_result_id TEXT NULL,
                country_code TEXT NULL,
                last_error_code TEXT NULL,
                last_error_message TEXT NULL,
                last_attempted_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                PRIMARY KEY (asset_revision_id, provider, contract_key),
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CHECK (length(provider) BETWEEN 1 AND 80),
                CHECK (length(contract_key) BETWEEN 1 AND 500),
                CHECK (place_value IS NULL OR length(place_value) BETWEEN 1 AND 80),
                CHECK ((status = 'succeeded' AND completed_at_utc IS NOT NULL AND place_value IS NOT NULL)
                    OR status IN ('deferred', 'failed'))
            );

            CREATE INDEX IF NOT EXISTS ix_photo_place_enrichment_attempts_resume
                ON photo_place_enrichment_attempts (
                    provider, contract_key, status, last_attempted_at_utc, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
