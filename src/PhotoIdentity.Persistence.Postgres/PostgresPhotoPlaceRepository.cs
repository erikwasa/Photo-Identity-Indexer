using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoPlaceRepository :
    IPhotoPlaceRepository,
    IAutomaticPhotoPlaceRepository
{
    private const string AutomaticSource = "automatic";
    private const string ManualSource = "manual";

    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoPlaceRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PhotoPlaceDefinition>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        IReadOnlyList<PlaceTagRow> rows =
            await ReadCanonicalPlaceRowsAsync(connection, cancellationToken);
        Dictionary<string, PlaceTagRow> byNormalized = rows.ToDictionary(
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

    public async Task<PhotoPlaceState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return new PhotoPlaceState(
            revisionId,
            await ReadEffectivePlaceAsync(connection, revisionId, cancellationToken),
            await ReadConflictAsync(connection, revisionId, cancellationToken));
    }

    public async Task<IReadOnlyList<PhotoPlaceMigrationConflict>> GetMigrationConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id, candidate_values, detected_at_utc
            FROM photo_place_migration_conflicts
            WHERE resolved_at_utc IS NULL
            ORDER BY detected_at_utc, asset_revision_id;
            """;

        List<PhotoPlaceMigrationConflict> conflicts = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            conflicts.Add(ReadConflict(reader));
        }

        return conflicts;
    }

    public async Task<PhotoPlaceState> SetManualPlaceAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoPlacePath requestedPlace = PhotoPlacePath.Parse(placeValue);
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        PlaceTagRow finalTag = await EnsureCanonicalPlacePathAsync(
            connection,
            transaction,
            requestedPlace,
            normalizedActor,
            now,
            preserveExistingFinalDisplay: true,
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
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                finalTag.Id,
                "set",
                ManualSource,
                provider: null,
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
        await transaction.CommitAsync(cancellationToken);
        return await GetStateAsync(revisionId, cancellationToken);
    }

    public async Task<PhotoPlaceState> ClearManualPlaceAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
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
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                tagId: null,
                "clear",
                ManualSource,
                provider: null,
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
            "Explicit manual place clear.",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetStateAsync(revisionId, cancellationToken);
    }

    public async Task<AutomaticPhotoPlaceEligibility> GetEligibilityAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        LatestPlaceAction? latest = await ReadLatestActionAsync(
            connection,
            transaction: null,
            revisionId,
            cancellationToken);
        bool conflict = await HasUnresolvedConflictAsync(
            connection,
            transaction: null,
            revisionId,
            cancellationToken);
        bool manual = string.Equals(latest?.SourceKind, ManualSource, StringComparison.Ordinal);
        return new AutomaticPhotoPlaceEligibility(!manual && !conflict, manual, conflict);
    }

    public async Task<AutomaticPhotoPlaceWriteResult> TrySetAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string provider,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoPlacePath requestedPlace = PhotoPlacePath.Parse(placeValue);
        string normalizedProvider = Normalize(provider, 80, nameof(provider)).ToLowerInvariant();
        string normalizedActor = Normalize(actor, 120, nameof(actor));
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        LatestPlaceAction? latest = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            cancellationToken);
        if (string.Equals(latest?.SourceKind, ManualSource, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AutomaticPhotoPlaceWriteResult(
                await GetStateAsync(revisionId, cancellationToken),
                Applied: false,
                BlockedByManual: true,
                BlockedByConflict: false);
        }

        if (await HasUnresolvedConflictAsync(connection, transaction, revisionId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AutomaticPhotoPlaceWriteResult(
                await GetStateAsync(revisionId, cancellationToken),
                Applied: false,
                BlockedByManual: false,
                BlockedByConflict: true);
        }

        PlaceTagRow finalTag = await EnsureCanonicalPlacePathAsync(
            connection,
            transaction,
            requestedPlace,
            normalizedActor,
            now,
            preserveExistingFinalDisplay: false,
            cancellationToken);

        bool alreadyCurrent = latest is not null &&
            latest.ActionKind == "set" &&
            latest.TagId == finalTag.Id &&
            string.Equals(latest.SourceKind, AutomaticSource, StringComparison.Ordinal) &&
            string.Equals(latest.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase);
        if (!alreadyCurrent)
        {
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                finalTag.Id,
                "set",
                AutomaticSource,
                normalizedProvider,
                normalizedActor,
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AutomaticPhotoPlaceWriteResult(
            await GetStateAsync(revisionId, cancellationToken),
            Applied: !alreadyCurrent,
            BlockedByManual: false,
            BlockedByConflict: false);
    }

    private static async Task<PlaceTagRow> EnsureCanonicalPlacePathAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PhotoPlacePath requestedPlace,
        string actor,
        DateTimeOffset createdAtUtc,
        bool preserveExistingFinalDisplay,
        CancellationToken cancellationToken)
    {
        PhotoTagPath requestedPath = requestedPlace.CanonicalTagPath;
        PhotoTagPath path = requestedPath;
        if (preserveExistingFinalDisplay)
        {
            PlaceTagRow? existingFinal = await ReadPlaceRowAsync(
                connection,
                transaction,
                requestedPath.NormalizedValue,
                cancellationToken);
            if (existingFinal is not null)
            {
                path = PhotoTagPath.Parse(existingFinal.DisplayValue);
            }
        }

        string? normalizedParent = null;
        string? displayParent = null;
        PlaceTagRow? current = null;
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
                await using NpgsqlCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_tags (
                        normalized_name, display_name, created_by, created_at_utc)
                    VALUES (@normalized_name, @display_name, @actor, @created_at_utc)
                    RETURNING id;
                    """;
                insert.Parameters.AddWithValue("normalized_name", normalizedValue);
                insert.Parameters.AddWithValue("display_name", displayValue);
                insert.Parameters.AddWithValue("actor", actor);
                insert.Parameters.AddWithValue("created_at_utc", createdAtUtc);
                long id = (long)(await insert.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("The canonical place node could not be inserted."));
                current = new PlaceTagRow(id, normalizedValue, displayValue);
            }

            normalizedParent = current.NormalizedValue;
            displayParent = current.DisplayValue;
        }

        return current ?? throw new InvalidOperationException("The canonical place path was empty.");
    }

    private static async Task<IReadOnlyList<PlaceTagRow>> ReadCanonicalPlaceRowsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            WHERE normalized_name = 'places'
               OR normalized_name LIKE 'places/%'
            ORDER BY normalized_name;
            """;
        List<PlaceTagRow> rows = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PlaceTagRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static PhotoPlaceDefinition ToDefinition(
        PlaceTagRow row,
        IReadOnlyDictionary<string, PlaceTagRow> byNormalized)
    {
        PhotoTagPath tagPath = PhotoTagPath.Parse(row.DisplayValue);
        PhotoPlacePath place = PhotoPlacePath.FromCanonicalTagPath(tagPath);
        PlaceTagRow? parent = null;
        string? normalizedParent = tagPath.ParentNormalizedValue;
        if (normalizedParent is not null &&
            !string.Equals(normalizedParent, PhotoPlacePath.RootNormalizedName, StringComparison.Ordinal))
        {
            byNormalized.TryGetValue(normalizedParent, out parent);
        }

        return new PhotoPlaceDefinition(
            row.Id,
            place.DisplayValue,
            place.Name,
            parent?.Id,
            place.ParentDisplayValue);
    }

    private static async Task<PhotoPlaceAssignment?> ReadEffectivePlaceAsync(
        NpgsqlConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
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
            WHERE action.asset_revision_id = @revision_id
            ORDER BY action.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetString(0) != "set")
        {
            return null;
        }

        PhotoPlacePath place =
            PhotoPlacePath.FromCanonicalTagPath(PhotoTagPath.Parse(reader.GetString(2)));
        return new PhotoPlaceAssignment(
            reader.GetInt64(1),
            place.DisplayValue,
            place.Name,
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    private static async Task<PhotoPlaceMigrationConflict?> ReadConflictAsync(
        NpgsqlConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id, candidate_values, detected_at_utc
            FROM photo_place_migration_conflicts
            WHERE asset_revision_id = @revision_id
              AND resolved_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadConflict(reader) : null;
    }

    private static PhotoPlaceMigrationConflict ReadConflict(NpgsqlDataReader reader)
    {
        string[] values = reader.GetString(1)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DisplayLegacyCandidate)
            .ToArray();
        return new PhotoPlaceMigrationConflict(
            AssetRevisionId.From(reader.GetGuid(0)),
            values,
            reader.GetFieldValue<DateTimeOffset>(2));
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
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind, tag_id, source_kind, provider
            FROM photo_place_actions
            WHERE asset_revision_id = @revision_id
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LatestPlaceAction(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task InsertActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        long? tagId,
        string actionKind,
        string sourceKind,
        string? provider,
        string actor,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_place_actions (
                asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
            VALUES (
                @revision_id, @tag_id, @action_kind, @source_kind, @provider, @actor, @created_at_utc);
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("tag_id", tagId is null ? DBNull.Value : tagId.Value);
        command.Parameters.AddWithValue("action_kind", actionKind);
        command.Parameters.AddWithValue("source_kind", sourceKind);
        command.Parameters.AddWithValue("provider", provider is null ? DBNull.Value : provider);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PlaceTagRow?> ReadPlaceRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            WHERE normalized_name = @normalized_name;
            """;
        command.Parameters.AddWithValue("normalized_name", normalizedValue);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PlaceTagRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<bool> HasUnresolvedConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM photo_place_migration_conflicts
                WHERE asset_revision_id = @revision_id
                  AND resolved_at_utc IS NULL);
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ResolveConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        string actor,
        DateTimeOffset resolvedAtUtc,
        string note,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE photo_place_migration_conflicts
            SET resolved_at_utc = @resolved_at_utc,
                resolved_by = @resolved_by,
                resolution_note = @resolution_note
            WHERE asset_revision_id = @revision_id
              AND resolved_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("resolved_at_utc", resolvedAtUtc);
        command.Parameters.AddWithValue("resolved_by", actor);
        command.Parameters.AddWithValue("resolution_note", note);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureRevisionExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM asset_revisions WHERE id = @revision_id);";
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        if (!((bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            throw new KeyNotFoundException($"Asset revision '{revisionId}' was not found.");
        }
    }

    private static string NormalizeActor(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string normalized = actor.Trim();
        return normalized.Length <= 120
            ? normalized
            : throw new ArgumentException("Place actor cannot exceed 120 characters.", nameof(actor));
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{parameterName} cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private sealed record PlaceTagRow(long Id, string NormalizedValue, string DisplayValue);

    private sealed record LatestPlaceAction(
        string ActionKind,
        long? TagId,
        string SourceKind,
        string? Provider);
}
