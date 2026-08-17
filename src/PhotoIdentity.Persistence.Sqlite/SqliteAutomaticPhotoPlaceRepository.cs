using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueAutomaticPlaceEligibility(
    bool Allowed,
    bool BlockedByManual,
    bool BlockedByConflict);

public sealed record CatalogueAutomaticPlaceWriteResult(
    CataloguePhotoPlaceState State,
    bool Applied,
    bool BlockedByManual,
    bool BlockedByConflict);

/// <summary>
/// Automatic place writes are isolated from manual editing so manual set/clear actions remain
/// authoritative. The write transaction re-checks manual precedence and migration conflicts before
/// appending an automatic set action.
/// </summary>
public sealed class SqliteAutomaticPhotoPlaceRepository
{
    private const string AutomaticSource = "automatic";
    private const string ManualSource = "manual";

    private readonly SqliteCatalogueDatabase _database;
    private readonly SqlitePhotoPlaceRepository _places;
    private readonly TimeProvider _timeProvider;

    public SqliteAutomaticPhotoPlaceRepository(
        SqliteCatalogueDatabase database,
        SqlitePhotoPlaceRepository places,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _places = places;
        _timeProvider = timeProvider;
    }

    public async Task<CatalogueAutomaticPlaceEligibility> GetEligibilityAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        LatestAction? latest = await ReadLatestActionAsync(connection, transaction: null, revisionId, cancellationToken);
        bool conflict = await HasUnresolvedConflictAsync(connection, transaction: null, revisionId, cancellationToken);
        bool manual = string.Equals(latest?.SourceKind, ManualSource, StringComparison.Ordinal);
        return new CatalogueAutomaticPlaceEligibility(!manual && !conflict, manual, conflict);
    }

    public async Task<CatalogueAutomaticPlaceWriteResult> TrySetAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string provider,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoPlacePath requestedPlace = PhotoPlacePath.Parse(placeValue);
        string normalizedProvider = Normalize(provider, 80, nameof(provider)).ToLowerInvariant();
        string normalizedActor = Normalize(actor, 120, nameof(actor));
        string now = Format(_timeProvider.GetUtcNow());

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        LatestAction? latest = await ReadLatestActionAsync(connection, transaction, revisionId, cancellationToken);
        if (string.Equals(latest?.SourceKind, ManualSource, StringComparison.Ordinal))
        {
            transaction.Commit();
            return new CatalogueAutomaticPlaceWriteResult(
                await _places.GetStateAsync(revisionId, cancellationToken),
                Applied: false,
                BlockedByManual: true,
                BlockedByConflict: false);
        }

        if (await HasUnresolvedConflictAsync(connection, transaction, revisionId, cancellationToken))
        {
            transaction.Commit();
            return new CatalogueAutomaticPlaceWriteResult(
                await _places.GetStateAsync(revisionId, cancellationToken),
                Applied: false,
                BlockedByManual: false,
                BlockedByConflict: true);
        }

        CanonicalPlaceRow finalTag = await EnsureCanonicalPlacePathAsync(
            connection,
            transaction,
            requestedPlace,
            normalizedActor,
            now,
            cancellationToken);

        bool alreadyCurrent = latest is not null &&
            latest.ActionKind == "set" &&
            latest.TagId == finalTag.Id &&
            string.Equals(latest.SourceKind, AutomaticSource, StringComparison.Ordinal) &&
            string.Equals(latest.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase);
        if (!alreadyCurrent)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO photo_place_actions (
                    asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
                VALUES ($revision_id, $tag_id, 'set', 'automatic', $provider, $actor, $created_at_utc);
                """;
            insert.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            insert.Parameters.AddWithValue("$tag_id", finalTag.Id);
            insert.Parameters.AddWithValue("$provider", normalizedProvider);
            insert.Parameters.AddWithValue("$actor", normalizedActor);
            insert.Parameters.AddWithValue("$created_at_utc", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return new CatalogueAutomaticPlaceWriteResult(
            await _places.GetStateAsync(revisionId, cancellationToken),
            Applied: !alreadyCurrent,
            BlockedByManual: false,
            BlockedByConflict: false);
    }

    private static async Task<CanonicalPlaceRow> EnsureCanonicalPlacePathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PhotoPlacePath requestedPlace,
        string actor,
        string createdAtUtc,
        CancellationToken cancellationToken)
    {
        PhotoTagPath path = requestedPlace.CanonicalTagPath;
        string? normalizedParent = null;
        string? displayParent = null;
        CanonicalPlaceRow? current = null;

        foreach (PhotoTagName segment in path.Segments)
        {
            string normalizedValue = normalizedParent is null
                ? segment.NormalizedName
                : $"{normalizedParent}{PhotoTagPath.Separator}{segment.NormalizedName}";
            current = await ReadPlaceRowAsync(connection, transaction, normalizedValue, cancellationToken);
            if (current is null)
            {
                string displayValue = displayParent is null
                    ? segment.DisplayName
                    : $"{displayParent}{PhotoTagPath.Separator}{segment.DisplayName}";
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_tags (normalized_name, display_name, created_by, created_at_utc)
                    VALUES ($normalized_name, $display_name, $actor, $created_at_utc);
                    """;
                insert.Parameters.AddWithValue("$normalized_name", normalizedValue);
                insert.Parameters.AddWithValue("$display_name", displayValue);
                insert.Parameters.AddWithValue("$actor", actor);
                insert.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                current = await ReadPlaceRowAsync(connection, transaction, normalizedValue, cancellationToken)
                    ?? throw new InvalidOperationException("The automatic canonical place node could not be read after insertion.");
            }

            normalizedParent = current.NormalizedValue;
            displayParent = current.DisplayValue;
        }

        return current ?? throw new InvalidOperationException("The automatic place path was empty.");
    }

    private static async Task<CanonicalPlaceRow?> ReadPlaceRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, normalized_name, display_name FROM photo_tags WHERE normalized_name = $normalized_name;";
        command.Parameters.AddWithValue("$normalized_name", normalizedValue);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CanonicalPlaceRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<LatestAction?> ReadLatestActionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind, tag_id, source_kind, provider
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

        return new LatestAction(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<bool> HasUnresolvedConflictAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
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

    private static async Task EnsureRevisionExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM asset_revisions WHERE id = $revision_id;";
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
        {
            throw new KeyNotFoundException($"Asset revision '{revisionId}' was not found.");
        }
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

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record CanonicalPlaceRow(long Id, string NormalizedValue, string DisplayValue);

    private sealed record LatestAction(string ActionKind, long? TagId, string SourceKind, string? Provider);
}
