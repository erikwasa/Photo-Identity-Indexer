using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqlitePhotoPresentationPreferenceRepository : IPhotoPresentationPreferenceRepository
{
    private const int QueryChunkSize = 500;
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqlitePhotoPresentationPreferenceRepository(
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<PhotoPresentationPreferenceState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<AssetRevisionId, string>> GetEffectiveAsync(
        IEnumerable<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        AssetRevisionId[] ids = revisionIds.Distinct().ToArray();
        Dictionary<AssetRevisionId, string> result = [];
        if (ids.Length == 0)
        {
            return result;
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        foreach (AssetRevisionId[] chunk in ids.Chunk(QueryChunkSize))
        {
            using SqliteCommand command = connection.CreateCommand();
            string[] parameters = chunk
                .Select((_, index) => $"$revision_{index}")
                .ToArray();
            command.CommandText = $"""
                WITH ranked AS (
                    SELECT
                        asset_revision_id,
                        action_kind,
                        preference_kind,
                        ROW_NUMBER() OVER (
                            PARTITION BY asset_revision_id
                            ORDER BY id DESC) AS row_number
                    FROM photo_presentation_preference_actions
                    WHERE asset_revision_id IN ({string.Join(", ", parameters)})
                )
                SELECT asset_revision_id, preference_kind
                FROM ranked
                WHERE row_number = 1
                  AND action_kind = 'set';
                """;
            for (int index = 0; index < chunk.Length; index++)
            {
                command.Parameters.AddWithValue(parameters[index], chunk[index].ToString());
            }

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[AssetRevisionId.From(Guid.Parse(reader.GetString(0)))] = reader.GetString(1);
            }
        }

        return result;
    }

    public async Task<PhotoPresentationPreferenceState> SetAsync(
        AssetRevisionId revisionId,
        string preference,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedPreference = PhotoPresentationPreferenceKinds.Normalize(preference);
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        LatestAction? latest = await ReadLatestAsync(connection, transaction, revisionId, cancellationToken);
        if (latest is null ||
            latest.ActionKind != PhotoPresentationPreferenceActionKinds.Set ||
            !string.Equals(latest.Preference, normalizedPreference, StringComparison.Ordinal))
        {
            await InsertAsync(
                connection,
                transaction,
                revisionId,
                PhotoPresentationPreferenceActionKinds.Set,
                normalizedPreference,
                normalizedActor,
                now,
                cancellationToken);
        }

        transaction.Commit();
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    public async Task<PhotoPresentationPreferenceState> ClearAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        LatestAction? latest = await ReadLatestAsync(connection, transaction, revisionId, cancellationToken);
        if (latest?.ActionKind == PhotoPresentationPreferenceActionKinds.Set)
        {
            await InsertAsync(
                connection,
                transaction,
                revisionId,
                PhotoPresentationPreferenceActionKinds.Clear,
                preference: null,
                normalizedActor,
                now,
                cancellationToken);
        }

        transaction.Commit();
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<PhotoPresentationPreferenceState> ReadStateAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, action_kind, preference_kind, actor, created_at_utc
            FROM photo_presentation_preference_actions
            WHERE asset_revision_id = $revision_id
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());

        List<PhotoPresentationPreferenceAction> history = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new PhotoPresentationPreferenceAction(
                reader.GetInt64(0),
                revisionId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                Parse(reader.GetString(4))));
        }

        string? effective = history.FirstOrDefault() is { ActionKind: PhotoPresentationPreferenceActionKinds.Set } latest
            ? latest.Preference
            : null;
        return new PhotoPresentationPreferenceState(revisionId, effective, history);
    }

    private static async Task<LatestAction?> ReadLatestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind, preference_kind
            FROM photo_presentation_preference_actions
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
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        string actionKind,
        string? preference,
        string actor,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_presentation_preference_actions (
                asset_revision_id,
                action_kind,
                preference_kind,
                actor,
                created_at_utc)
            VALUES (
                $revision_id,
                $action_kind,
                $preference_kind,
                $actor,
                $created_at_utc);
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$action_kind", actionKind);
        command.Parameters.AddWithValue(
            "$preference_kind",
            preference is null ? DBNull.Value : preference);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue(
            "$created_at_utc",
            createdAtUtc.ToString("O", CultureInfo.InvariantCulture));
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
        long count = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
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
            throw new ArgumentException(
                "Presentation preference actor cannot exceed 120 characters.",
                nameof(actor));
        }

        return normalized;
    }

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);

    private sealed record LatestAction(string ActionKind, string? Preference);
}
