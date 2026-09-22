using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoListCollectionRepository : IPhotoListCollectionRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoListCollectionRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<PhotoListCollectionDefinition> CreateAsync(
        string name,
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        PhotoListCollectionName canonicalName = PhotoListCollectionName.Parse(name);
        AssetRevisionId[] canonicalRevisionIds =
            PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
        PhotoListCollectionId id = PhotoListCollectionId.New();
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await ValidateAvailableRevisionsAsync(
            connection,
            transaction,
            canonicalRevisionIds,
            cancellationToken);

        try
        {
            await using (NpgsqlCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_list_collections (
                        id,
                        normalized_name,
                        display_name,
                        created_at_utc,
                        updated_at_utc)
                    VALUES (
                        @id,
                        @normalized_name,
                        @display_name,
                        @created_at_utc,
                        @updated_at_utc);
                    """;
                AddDefinitionParameters(insert, id, canonicalName, now, now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await ReplaceItemsAsync(
                connection,
                transaction,
                id,
                canonicalRevisionIds,
                deleteExisting: false,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PhotoListCollectionNameConflictException(canonicalName.DisplayValue);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new PhotoListCollectionRevisionUnavailableException(canonicalRevisionIds);
        }

        return new PhotoListCollectionDefinition(
            id,
            canonicalName.DisplayValue,
            canonicalRevisionIds,
            now,
            now);
    }

    public async Task<IReadOnlyList<PhotoListCollectionDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = DefinitionSelect + """
            GROUP BY
                collection.id,
                collection.display_name,
                collection.created_at_utc,
                collection.updated_at_utc,
                collection.normalized_name
            ORDER BY collection.normalized_name, collection.id;
            """;

        List<PhotoListCollectionDefinition> definitions = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(ReadDefinition(reader));
        }

        return definitions;
    }

    public async Task<PhotoListCollectionDefinition?> GetAsync(
        PhotoListCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = DefinitionSelect + """
            WHERE collection.id = @id
            GROUP BY
                collection.id,
                collection.display_name,
                collection.created_at_utc,
                collection.updated_at_utc,
                collection.normalized_name;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDefinition(reader)
            : null;
    }

    public async Task<PhotoListCollectionDefinition?> UpdateAsync(
        PhotoListCollectionId id,
        string name,
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        PhotoListCollectionName canonicalName = PhotoListCollectionName.Parse(name);
        AssetRevisionId[] canonicalRevisionIds =
            PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await ValidateAvailableRevisionsAsync(
            connection,
            transaction,
            canonicalRevisionIds,
            cancellationToken);

        DateTimeOffset createdAtUtc;
        try
        {
            await using NpgsqlCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE photo_list_collections
                SET normalized_name = @normalized_name,
                    display_name = @display_name,
                    updated_at_utc = @updated_at_utc
                WHERE id = @id
                RETURNING created_at_utc;
                """;
            update.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
            update.Parameters.AddWithValue("normalized_name", canonicalName.NormalizedValue);
            update.Parameters.AddWithValue("display_name", canonicalName.DisplayValue);
            update.Parameters.AddWithValue("updated_at_utc", now);

            await using NpgsqlDataReader reader =
                await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            createdAtUtc = reader.GetFieldValue<DateTimeOffset>(0);
            await reader.CloseAsync();

            await ReplaceItemsAsync(
                connection,
                transaction,
                id,
                canonicalRevisionIds,
                deleteExisting: true,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PhotoListCollectionNameConflictException(canonicalName.DisplayValue);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new PhotoListCollectionRevisionUnavailableException(canonicalRevisionIds);
        }

        return new PhotoListCollectionDefinition(
            id,
            canonicalName.DisplayValue,
            canonicalRevisionIds,
            createdAtUtc,
            now);
    }

    public async Task<bool> DeleteAsync(
        PhotoListCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM photo_list_collections WHERE id = @id;";
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<PhotoListCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        PhotoListCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                collection.display_name,
                COALESCE(
                    ARRAY_AGG(item.asset_revision_id ORDER BY item.position)
                        FILTER (
                            WHERE item.asset_revision_id IS NOT NULL
                              AND asset.deleted_at_utc IS NULL),
                    ARRAY[]::uuid[])
            FROM photo_list_collections AS collection
            LEFT JOIN photo_list_collection_items AS item
                ON item.collection_id = collection.id
            LEFT JOIN asset_revisions AS revision
                ON revision.id = item.asset_revision_id
            LEFT JOIN assets AS asset
                ON asset.id = revision.asset_id
            WHERE collection.id = @id
            GROUP BY collection.id, collection.display_name;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        AssetRevisionId[] revisionIds = reader
            .GetFieldValue<Guid[]>(1)
            .Select(AssetRevisionId.From)
            .ToArray();

        return new PhotoListCollectionSlideshowSnapshot(
            id,
            reader.GetString(0),
            _timeProvider.GetUtcNow().ToUniversalTime(),
            revisionIds);
    }

    private const string DefinitionSelect = """
        SELECT
            collection.id,
            collection.display_name,
            collection.created_at_utc,
            collection.updated_at_utc,
            COALESCE(
                ARRAY_AGG(item.asset_revision_id ORDER BY item.position)
                    FILTER (WHERE item.asset_revision_id IS NOT NULL),
                ARRAY[]::uuid[])
        FROM photo_list_collections AS collection
        LEFT JOIN photo_list_collection_items AS item
            ON item.collection_id = collection.id
        """;

    private static PhotoListCollectionDefinition ReadDefinition(NpgsqlDataReader reader) =>
        new(
            PhotoListCollectionId.From(reader.GetGuid(0)),
            reader.GetString(1),
            reader.GetFieldValue<Guid[]>(4).Select(AssetRevisionId.From).ToArray(),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3));

    private static void AddDefinitionParameters(
        NpgsqlCommand command,
        PhotoListCollectionId id,
        PhotoListCollectionName name,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("normalized_name", name.NormalizedValue);
        command.Parameters.AddWithValue("display_name", name.DisplayValue);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", updatedAtUtc);
    }

    private static async Task ValidateAvailableRevisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken)
    {
        if (revisionIds.Count == 0)
        {
            return;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT requested.revision_id
            FROM unnest(@revision_ids::uuid[]) WITH ORDINALITY
                AS requested(revision_id, position)
            LEFT JOIN asset_revisions AS revision
                ON revision.id = requested.revision_id
            LEFT JOIN assets AS asset
                ON asset.id = revision.asset_id
            WHERE revision.id IS NULL
               OR asset.deleted_at_utc IS NOT NULL
            ORDER BY requested.position;
            """;
        AddRevisionIdsParameter(command, revisionIds);

        List<AssetRevisionId> unavailable = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            unavailable.Add(AssetRevisionId.From(reader.GetGuid(0)));
        }

        if (unavailable.Count > 0)
        {
            throw new PhotoListCollectionRevisionUnavailableException(unavailable);
        }
    }

    private static async Task ReplaceItemsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PhotoListCollectionId collectionId,
        IReadOnlyList<AssetRevisionId> revisionIds,
        bool deleteExisting,
        CancellationToken cancellationToken)
    {
        if (deleteExisting)
        {
            await using NpgsqlCommand delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM photo_list_collection_items WHERE collection_id = @collection_id;";
            delete.Parameters.AddWithValue(
                "collection_id",
                NpgsqlDbType.Uuid,
                collectionId.Value);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (revisionIds.Count == 0)
        {
            return;
        }

        await using NpgsqlCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO photo_list_collection_items (
                collection_id,
                position,
                asset_revision_id)
            SELECT
                @collection_id,
                (requested.position - 1)::integer,
                requested.revision_id
            FROM unnest(@revision_ids::uuid[]) WITH ORDINALITY
                AS requested(revision_id, position)
            ORDER BY requested.position;
            """;
        insert.Parameters.AddWithValue(
            "collection_id",
            NpgsqlDbType.Uuid,
            collectionId.Value);
        AddRevisionIdsParameter(insert, revisionIds);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRevisionIdsParameter(
        NpgsqlCommand command,
        IReadOnlyList<AssetRevisionId> revisionIds)
    {
        command.Parameters.AddWithValue(
            "revision_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            revisionIds.Select(item => item.Value).ToArray());
    }
}
