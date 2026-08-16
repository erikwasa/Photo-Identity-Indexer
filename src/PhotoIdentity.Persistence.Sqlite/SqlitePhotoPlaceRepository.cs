using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePlaceDefinition(
    long TagId,
    string Value,
    string Name,
    long? ParentTagId,
    string? ParentValue);

public sealed record CataloguePhotoPlace(
    long TagId,
    string Value,
    string Name,
    string SourceKind,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record CataloguePlaceMigrationConflict(
    AssetRevisionId RevisionId,
    IReadOnlyList<string> CandidateValues,
    DateTimeOffset DetectedAtUtc);

public sealed record CataloguePhotoPlaceState(
    AssetRevisionId RevisionId,
    CataloguePhotoPlace? Place,
    CataloguePlaceMigrationConflict? MigrationConflict);

/// <summary>
/// Stores one effective hierarchical place per immutable photo revision. Place actions are
/// append-only and reuse canonical photo_tags vocabulary under the reserved Places/ root.
/// </summary>
public sealed class SqlitePhotoPlaceRepository
{
    private const string ManualSource = "manual";

    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqlitePhotoPlaceRepository(SqliteCatalogueDatabase database, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CataloguePlaceDefinition>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        IReadOnlyList<CanonicalPlaceRow> rows = await ReadCanonicalPlaceRowsAsync(connection, cancellationToken);
        Dictionary<string, CanonicalPlaceRow> byNormalized = rows.ToDictionary(
            row => row.NormalizedValue,
            StringComparer.Ordinal);

        return rows
            .Where(row => !string.Equals(
                row.NormalizedValue,
                PhotoPlacePath.RootNormalizedName,
                StringComparison.Ordinal))
            .Select(row => ToDefinition(row, byNormalized))
            .ToArray();
    }

    public async Task<CataloguePhotoPlaceState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return new CataloguePhotoPlaceState(
            revisionId,
            await ReadEffectivePlaceAsync(connection, revisionId, cancellationToken),
            await ReadConflictAsync(connection, revisionId, cancellationToken));
    }

    public async Task<IReadOnlyList<CataloguePlaceMigrationConflict>> GetMigrationConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id, candidate_values, detected_at_utc
            FROM photo_place_migration_conflicts
            WHERE resolved_at_utc IS NULL
            ORDER BY detected_at_utc, asset_revision_id;
            """;

        List<CataloguePlaceMigrationConflict> conflicts = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            conflicts.Add(ReadConflict(reader));
        }

        return conflicts;
    }

    public async Task<CataloguePhotoPlaceState> SetManualPlaceAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoPlacePath requestedPlace = PhotoPlacePath.Parse(placeValue);
        string normalizedActor = NormalizeActor(actor);
        string now = Format(_timeProvider.GetUtcNow());

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        CanonicalPlaceRow finalTag = await EnsureCanonicalPlacePathAsync(
            connection,
            transaction,
            requestedPlace,
            normalizedActor,
            now,
            cancellationToken);
        LatestPlaceAction? latest = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            cancellationToken);

        if (latest is null ||
            latest.ActionKind != "set" ||
            latest.TagId != finalTag.Id ||
            !string.Equals(latest.SourceKind, ManualSource, StringComparison.Ordinal))
        {
            await InsertSetAsync(
                connection,
                transaction,
                revisionId,
                finalTag.Id,
                ManualSource,
                normalizedActor,
                now,
                cancellationToken);
        }

        await ResolveConflictAsync(
            connection,
            transaction,
            revisionId,
            normalizedActor,
            now,
            "Explicit manual place selection.",
            cancellationToken);
        transaction.Commit();
        return await GetStateAsync(revisionId, cancellationToken);
    }

    public async Task<CataloguePhotoPlaceState> ClearManualPlaceAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        string now = Format(_timeProvider.GetUtcNow());

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        LatestPlaceAction? latest = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            cancellationToken);
        bool hasConflict = await HasUnresolvedConflictAsync(
            connection,
            transaction,
            revisionId,
            cancellationToken);

        if (latest?.ActionKind == "set" || hasConflict)
        {
            using SqliteCommand clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = """
                INSERT INTO photo_place_actions (
                    asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
                VALUES ($revision_id, NULL, 'clear', 'manual', NULL, $actor, $created_at_utc);
                """;
            clear.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            clear.Parameters.AddWithValue("$actor", normalizedActor);
            clear.Parameters.AddWithValue("$created_at_utc", now);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await ResolveConflictAsync(
            connection,
            transaction,
            revisionId,
            normalizedActor,
            now,
            "Explicit manual place clear.",
            cancellationToken);
        transaction.Commit();
        return await GetStateAsync(revisionId, cancellationToken);
    }

    private static async Task<CanonicalPlaceRow> EnsureCanonicalPlacePathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PhotoPlacePath requestedPlace,
        string actor,
        string createdAtUtc,
        CancellationToken cancellationToken)
    {
        PhotoTagPath requestedPath = requestedPlace.CanonicalTagPath;
        CanonicalPlaceRow? existingFinal = await ReadPlaceRowAsync(
            connection,
            transaction,
            requestedPath.NormalizedValue,
            cancellationToken);
        PhotoTagPath path = existingFinal is null
            ? requestedPath
            : PhotoTagPath.Parse(existingFinal.DisplayValue);

        string? normalizedParent = null;
        string? displayParent = null;
        CanonicalPlaceRow? current = null;

        foreach (PhotoTagName segment in path.Segments)
        {
            string normalizedValue = normalizedParent is null
                ? segment.NormalizedName
                : $"{normalizedParent}{PhotoTagPath.Separator}{segment.NormalizedName}";
            current = await ReadPlaceRowAsync(
                connection,
                transaction,
                normalizedValue,
                cancellationToken);
            if (current is null)
            {
                string displayValue = displayParent is null
                    ? segment.DisplayName
                    : $"{displayParent}{PhotoTagPath.Separator}{segment.DisplayName}";
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_tags (
                        normalized_name, display_name, created_by, created_at_utc)
                    VALUES ($normalized_name, $display_name, $actor, $created_at_utc);
                    """;
                insert.Parameters.AddWithValue("$normalized_name", normalizedValue);
                insert.Parameters.AddWithValue("$display_name", displayValue);
                insert.Parameters.AddWithValue("$actor", actor);
                insert.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                current = await ReadPlaceRowAsync(
                    connection,
                    transaction,
                    normalizedValue,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The canonical place node could not be read after insertion.");
            }

            normalizedParent = current.NormalizedValue;
            displayParent = current.DisplayValue;
        }

        return current ?? throw new InvalidOperationException("The canonical place path was empty.");
    }

    private static async Task<IReadOnlyList<CanonicalPlaceRow>> ReadCanonicalPlaceRowsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            WHERE normalized_name = 'places'
               OR normalized_name LIKE 'places/%'
            ORDER BY normalized_name;
            """;
        List<CanonicalPlaceRow> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CanonicalPlaceRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }
        return rows;
    }

    private static CataloguePlaceDefinition ToDefinition(
        CanonicalPlaceRow row,
        IReadOnlyDictionary<string, CanonicalPlaceRow> byNormalized)
    {
        PhotoTagPath tagPath = PhotoTagPath.Parse(row.DisplayValue);
        PhotoPlacePath place = PhotoPlacePath.FromCanonicalTagPath(tagPath);
        CanonicalPlaceRow? parent = null;
        string? normalizedParent = tagPath.ParentNormalizedValue;
        if (normalizedParent is not null &&
            !string.Equals(normalizedParent, PhotoPlacePath.RootNormalizedName, StringComparison.Ordinal))
        {
            byNormalized.TryGetValue(normalizedParent, out parent);
        }

        return new CataloguePlaceDefinition(
            row.Id,
            place.DisplayValue,
            place.Name,
            parent?.Id,
            place.ParentDisplayValue);
    }

    private static async Task<CataloguePhotoPlace?> ReadEffectivePlaceAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                action.action_kind,
                tag.id,
                tag.display_name,
                action.source_kind,
                action.actor,
                action.created_at_utc
            FROM photo_place_actions AS action
            LEFT JOIN photo_tags AS tag ON tag.id = action.tag_id
            WHERE action.asset_revision_id = $revision_id
            ORDER BY action.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetString(0) != "set")
        {
            return null;
        }

        PhotoPlacePath place = PhotoPlacePath.FromCanonicalTagPath(PhotoTagPath.Parse(reader.GetString(2)));
        return new CataloguePhotoPlace(
            reader.GetInt64(1),
            place.DisplayValue,
            place.Name,
            reader.GetString(3),
            reader.GetString(4),
            Parse(reader.GetString(5)));
    }

    private static async Task<CataloguePlaceMigrationConflict?> ReadConflictAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id, candidate_values, detected_at_utc
            FROM photo_place_migration_conflicts
            WHERE asset_revision_id = $revision_id
              AND resolved_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadConflict(reader) : null;
    }

    private static CataloguePlaceMigrationConflict ReadConflict(SqliteDataReader reader)
    {
        string[] values = reader.GetString(1)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DisplayLegacyCandidate)
            .ToArray();
        return new CataloguePlaceMigrationConflict(
            AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
            values,
            Parse(reader.GetString(2)));
    }

    private static string DisplayLegacyCandidate(string value)
    {
        PhotoTagPath path = PhotoTagPath.Parse(value);
        if (!PhotoPlacePath.IsReservedTagPath(path))
        {
            return path.DisplayValue;
        }

        return path.Segments.Count == 1
            ? "(Places root)"
            : string.Join(
                PhotoTagPath.Separator,
                path.Segments.Skip(1).Select(segment => segment.DisplayName));
    }

    private static async Task<LatestPlaceAction?> ReadLatestActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind, tag_id, source_kind
            FROM photo_place_actions
            WHERE asset_revision_id = $revision_id
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LatestPlaceAction(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.GetString(2));
    }

    private static async Task InsertSetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        long tagId,
        string sourceKind,
        string actor,
        string createdAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_place_actions (
                asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
            VALUES ($revision_id, $tag_id, 'set', $source_kind, NULL, $actor, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$tag_id", tagId);
        command.Parameters.AddWithValue("$source_kind", sourceKind);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CanonicalPlaceRow?> ReadPlaceRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            WHERE normalized_name = $normalized_name;
            """;
        command.Parameters.AddWithValue("$normalized_name", normalizedValue);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CanonicalPlaceRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<bool> HasUnresolvedConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM photo_place_migration_conflicts
            WHERE asset_revision_id = $revision_id
              AND resolved_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task ResolveConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        string actor,
        string resolvedAtUtc,
        string note,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE photo_place_migration_conflicts
            SET resolved_at_utc = $resolved_at_utc,
                resolved_by = $resolved_by,
                resolution_note = $resolution_note
            WHERE asset_revision_id = $revision_id
              AND resolved_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$resolved_at_utc", resolvedAtUtc);
        command.Parameters.AddWithValue("$resolved_by", actor);
        command.Parameters.AddWithValue("$resolution_note", note);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureRevisionExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM asset_revisions WHERE id = $revision_id;";
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        long count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (count == 0)
        {
            throw new KeyNotFoundException($"Asset revision '{revisionId}' was not found.");
        }
    }

    private static string NormalizeActor(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string normalized = actor.Trim();
        if (normalized.Length > 120)
        {
            throw new ArgumentException("Place actor cannot exceed 120 characters.", nameof(actor));
        }
        return normalized;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);

    private sealed record CanonicalPlaceRow(long Id, string NormalizedValue, string DisplayValue);

    private sealed record LatestPlaceAction(string ActionKind, long? TagId, string SourceKind);
}
