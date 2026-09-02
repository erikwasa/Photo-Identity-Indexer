using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL persistence for the capture-time/GPS subset used by archive background workers.
/// </summary>
public sealed class PostgresPhotoCaptureMetadataRepository :
    IPhotoCaptureMetadataRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPhotoCaptureMetadataRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<PhotoCaptureMetadata?> GetPhotoMetadataAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                taken_at_local,
                utc_offset_minutes,
                latitude,
                longitude
            FROM photo_capture_metadata
            WHERE asset_revision_id = @asset_revision_id;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PhotoCaptureMetadata(
            reader.IsDBNull(0)
                ? null
                : DateTime.SpecifyKind(
                    reader.GetDateTime(0),
                    DateTimeKind.Unspecified),
            reader.IsDBNull(1)
                ? null
                : TimeSpan.FromMinutes(reader.GetInt16(1)),
            reader.IsDBNull(2)
                ? null
                : reader.GetDouble(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetDouble(3));
    }

    public async Task SavePhotoMetadataAsync(
        AssetRevisionId revisionId,
        PhotoCaptureMetadata metadata,
        DateTimeOffset extractedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO photo_capture_metadata (
                asset_revision_id,
                taken_at_local,
                utc_offset_minutes,
                latitude,
                longitude,
                extracted_at_utc)
            VALUES (
                @asset_revision_id,
                @taken_at_local,
                @utc_offset_minutes,
                @latitude,
                @longitude,
                @extracted_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                taken_at_local = excluded.taken_at_local,
                utc_offset_minutes = excluded.utc_offset_minutes,
                latitude = excluded.latitude,
                longitude = excluded.longitude,
                extracted_at_utc = excluded.extracted_at_utc;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));

        NpgsqlParameter takenAt =
            command.Parameters.Add(
                "taken_at_local",
                NpgsqlDbType.Timestamp);
        takenAt.Value = metadata.TakenAtLocal is null
            ? DBNull.Value
            : DateTime.SpecifyKind(
                metadata.TakenAtLocal.Value,
                DateTimeKind.Unspecified);

        NpgsqlParameter offset =
            command.Parameters.Add(
                "utc_offset_minutes",
                NpgsqlDbType.Smallint);
        offset.Value = metadata.UtcOffset is null
            ? DBNull.Value
            : checked((short)metadata.UtcOffset.Value.TotalMinutes);

        NpgsqlParameter latitude =
            command.Parameters.Add(
                "latitude",
                NpgsqlDbType.Double);
        latitude.Value = metadata.Latitude is null
            ? DBNull.Value
            : metadata.Latitude.Value;

        NpgsqlParameter longitude =
            command.Parameters.Add(
                "longitude",
                NpgsqlDbType.Double);
        longitude.Value = metadata.Longitude is null
            ? DBNull.Value
            : metadata.Longitude.Value;

        command.Parameters.AddWithValue(
            "extracted_at_utc",
            extractedAtUtc.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
