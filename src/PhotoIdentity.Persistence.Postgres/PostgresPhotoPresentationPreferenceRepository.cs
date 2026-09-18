using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoPresentationPreferenceRepository : IPhotoPresentationPreferenceRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoPresentationPreferenceRepository(
        PostgresCatalogueDatabase database,
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<AssetRevisionId, string>> GetEffectiveAsync(
        IEnumerable<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        Guid[] ids = revisionIds
            .Distinct()
            .Select(id => Guid.Parse(id.ToString()))
            .ToArray();
        Dictionary<AssetRevisionId, string> result = [];
        if (ids.Length == 0)
        {
            return result;
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT ON (asset_revision_id)
                asset_revision_id,
                action_kind,
                preference_kind
            FROM photo_presentation_preference_actions
            WHERE asset_revision_id = ANY(@revision_ids)
            ORDER BY asset_revision_id, id DESC;
            """;
        command.Parameters.AddWithValue(
            "revision_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            ids);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1) != PhotoPresentationPreferenceActionKinds.Set)
            {
                continue;
            }

            result[AssetRevisionId.From(reader.GetGuid(0))] = reader.GetString(2);
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

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
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

        await transaction.CommitAsync(cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    public async Task<PhotoPresentationPreferenceState> ClearAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
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

        await transaction.CommitAsync(cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<PhotoPresentationPreferenceState> ReadStateAsync(
        NpgsqlConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, action_kind, preference_kind, actor, created_at_utc
            FROM photo_presentation_preference_actions
            WHERE asset_revision_id = @revision_id
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(revisionId.ToString()));

        List<PhotoPresentationPreferenceAction> history = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new PhotoPresentationPreferenceAction(
                reader.GetInt64(0),
                revisionId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        string? effective = history.FirstOrDefault() is { ActionKind: PhotoPresentationPreferenceActionKinds.Set } latest
            ? latest.Preference
            : null;
        return new PhotoPresentationPreferenceState(revisionId, effective, history);
    }

    private static async Task<LatestAction?> ReadLatestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind, preference_kind
            FROM photo_presentation_preference_actions
            WHERE asset_revision_id = @revision_id
            ORDER BY id DESC
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LatestAction(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        string actionKind,
        string? preference,
        string actor,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_presentation_preference_actions (
                asset_revision_id,
                action_kind,
                preference_kind,
                actor,
                created_at_utc)
            VALUES (
                @revision_id,
                @action_kind,
                @preference_kind,
                @actor,
                @created_at_utc);
            """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("action_kind", actionKind);
        command.Parameters.Add("preference_kind", NpgsqlDbType.Text).Value =
            preference is null ? DBNull.Value : preference;
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
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
        command.CommandText = transaction is null
            ? """
              SELECT 1
              FROM asset_revisions
              WHERE id = @revision_id;
              """
            : """
              SELECT 1
              FROM asset_revisions
              WHERE id = @revision_id
              FOR UPDATE;
              """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(revisionId.ToString()));
        object? exists = await command.ExecuteScalarAsync(cancellationToken);
        if (exists is null)
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

    private sealed record LatestAction(string ActionKind, string? Preference);
}
