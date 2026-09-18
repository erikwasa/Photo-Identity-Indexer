using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoSlideshowExposureRepository : IPhotoSlideshowExposureRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoSlideshowExposureRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<bool> RecordPresentedAsync(
        Guid sessionId,
        SmartCollectionId collectionId,
        bool creative,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Slideshow session identifier cannot be empty.", nameof(sessionId));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_slideshow_exposures (
                session_id,
                asset_revision_id,
                collection_id,
                creative,
                shown_at_utc)
            VALUES (
                @session_id,
                @revision_id,
                @collection_id,
                @creative,
                @shown_at_utc)
            ON CONFLICT (session_id, asset_revision_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionId);
        command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        command.Parameters.AddWithValue("collection_id", NpgsqlDbType.Uuid, collectionId.Value);
        command.Parameters.AddWithValue("creative", NpgsqlDbType.Boolean, creative);
        command.Parameters.AddWithValue("shown_at_utc", NpgsqlDbType.TimestampTz, now);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary>> GetSummariesAsync(
        IEnumerable<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        Guid[] ids = revisionIds
            .Distinct()
            .Select(id => id.Value)
            .ToArray();
        Dictionary<AssetRevisionId, PhotoSlideshowExposureSummary> result = [];
        if (ids.Length == 0)
        {
            return result;
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id,
                   COUNT(*),
                   MAX(shown_at_utc)
            FROM photo_slideshow_exposures
            WHERE asset_revision_id = ANY(@revision_ids)
            GROUP BY asset_revision_id;
            """;
        command.Parameters.AddWithValue(
            "revision_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            ids);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(reader.GetGuid(0));
            result[revisionId] = new PhotoSlideshowExposureSummary(
                revisionId,
                checked((int)reader.GetInt64(1)),
                reader.GetFieldValue<DateTimeOffset>(2));
        }

        return result;
    }
}
