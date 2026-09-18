using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqlitePhotoSlideshowExposureRepository : IPhotoSlideshowExposureRepository
{
    private const int QueryChunkSize = 500;
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqlitePhotoSlideshowExposureRepository(
        SqliteCatalogueDatabase database,
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
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO photo_slideshow_exposures (
                session_id,
                asset_revision_id,
                collection_id,
                creative,
                shown_at_utc)
            VALUES (
                $session_id,
                $revision_id,
                $collection_id,
                $creative,
                $shown_at_utc);
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$collection_id", collectionId.ToString());
        command.Parameters.AddWithValue("$creative", creative ? 1 : 0);
        command.Parameters.AddWithValue(
            "$shown_at_utc",
            now.ToString("O", CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary>> GetSummariesAsync(
        IEnumerable<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        AssetRevisionId[] ids = revisionIds.Distinct().ToArray();
        Dictionary<AssetRevisionId, PhotoSlideshowExposureSummary> result = [];
        if (ids.Length == 0)
        {
            return result;
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        foreach (AssetRevisionId[] chunk in ids.Chunk(QueryChunkSize))
        {
            using SqliteCommand command = connection.CreateCommand();
            string[] parameters = chunk.Select((_, index) => $"$revision_{index}").ToArray();
            command.CommandText = $"""
                SELECT asset_revision_id,
                       COUNT(*),
                       MAX(shown_at_utc)
                FROM photo_slideshow_exposures
                WHERE asset_revision_id IN ({string.Join(", ", parameters)})
                GROUP BY asset_revision_id;
                """;
            for (int index = 0; index < chunk.Length; index++)
            {
                command.Parameters.AddWithValue(parameters[index], chunk[index].ToString());
            }

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                AssetRevisionId revisionId = AssetRevisionId.From(Guid.Parse(reader.GetString(0)));
                DateTimeOffset lastShown = DateTimeOffset.Parse(
                    reader.GetString(2),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
                result[revisionId] = new PhotoSlideshowExposureSummary(
                    revisionId,
                    checked((int)reader.GetInt64(1)),
                    lastShown);
            }
        }

        return result;
    }
}
