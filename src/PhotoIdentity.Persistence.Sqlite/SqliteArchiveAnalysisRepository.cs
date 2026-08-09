using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Tracks exact analysis profiles and successful immutable-revision completion.
/// A successful row is deliberately independent of face count so zero-face images are complete.
/// </summary>
public sealed class SqliteArchiveAnalysisRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveAnalysisRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task RegisterRunAsync(
        ProcessingRunId runId,
        AnalysisProfileDefinition profile,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await EnsureSchemaAsync(cancellationToken);
        Sha256Digest profileHash = profile.ComputeHash();
        DateTimeOffset recordedAt = recordedAtUtc.ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO archive_analysis_profiles (
                    profile_hash,
                    detector_pipeline_hash,
                    detector_model_id,
                    detector_model_hash,
                    embedder_model_id,
                    embedder_model_hash,
                    alignment_protocol,
                    canonical_definition,
                    recorded_at_utc)
                VALUES (
                    $profile_hash,
                    $detector_pipeline_hash,
                    $detector_model_id,
                    $detector_model_hash,
                    $embedder_model_id,
                    $embedder_model_hash,
                    $alignment_protocol,
                    $canonical_definition,
                    $recorded_at_utc)
                ON CONFLICT(profile_hash) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
            command.Parameters.AddWithValue("$detector_pipeline_hash", profile.DetectorPipelineHash.ToString());
            command.Parameters.AddWithValue("$detector_model_id", profile.DetectorModelId.ToString());
            command.Parameters.AddWithValue("$detector_model_hash", profile.DetectorModelHash.ToString());
            command.Parameters.AddWithValue("$embedder_model_id", profile.EmbedderModelId.ToString());
            command.Parameters.AddWithValue("$embedder_model_hash", profile.EmbedderModelHash.ToString());
            command.Parameters.AddWithValue("$alignment_protocol", profile.AlignmentProtocol.ToString());
            command.Parameters.AddWithValue("$canonical_definition", profile.ToCanonicalText());
            command.Parameters.AddWithValue("$recorded_at_utc", Format(recordedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO archive_analysis_runs (processing_run_id, profile_hash, registered_at_utc)
                VALUES ($processing_run_id, $profile_hash, $registered_at_utc)
                ON CONFLICT(processing_run_id) DO UPDATE SET
                    profile_hash = excluded.profile_hash;
                """;
            command.Parameters.AddWithValue("$processing_run_id", runId.ToString());
            command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
            command.Parameters.AddWithValue("$registered_at_utc", Format(recordedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task<Sha256Digest> GetRunProfileHashAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT profile_hash
            FROM archive_analysis_runs
            WHERE processing_run_id = $processing_run_id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", runId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string hash
            ? new Sha256Digest(hash)
            : throw new KeyNotFoundException($"Processing run {runId} is not a registered archive-analysis run.");
    }

    public Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default) =>
        GetPendingCurrentRevisionIdsAsync(sourceId, profileHash, includeHydratable: false, cancellationToken);

    public async Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        Sha256Digest profileHash,
        bool includeHydratable,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            LEFT JOIN archive_asset_availability AS availability
                ON availability.asset_id = asset.id
            LEFT JOIN archive_source_observations AS source_observation
                ON source_observation.asset_id = asset.id
            LEFT JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = revision.id
               AND analysis.profile_hash = $profile_hash
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND COALESCE(source_observation.verification_state, 'verified') = 'verified'
              AND (
                    COALESCE(availability.availability, 'local') = 'local'
                    OR ($include_hydratable = 1 AND availability.availability IN ('online-only', 'downloading'))
                  )
              AND analysis.asset_revision_id IS NULL
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
        command.Parameters.AddWithValue("$include_hydratable", includeHydratable ? 1 : 0);

        List<AssetRevisionId> revisions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(AssetRevisionId.From(Guid.Parse(reader.GetString(0))));
        }

        return revisions;
    }

    public async Task<bool> IsCompletedAsync(
        AssetRevisionId revisionId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM asset_revision_analysis
            WHERE asset_revision_id = $asset_revision_id
              AND profile_hash = $profile_hash;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<int> CountCompletedCurrentRevisionsAsync(
        SourceId sourceId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            INNER JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = revision.id
               AND analysis.profile_hash = $profile_hash
            LEFT JOIN archive_source_observations AS source_observation
                ON source_observation.asset_id = asset.id
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND COALESCE(source_observation.verification_state, 'verified') = 'verified';
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public async Task RecordCompletionAsync(
        ProcessingRunId runId,
        AssetRevisionId revisionId,
        Sha256Digest profileHash,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asset_revision_analysis (
                asset_revision_id,
                profile_hash,
                processing_run_id,
                completed_at_utc)
            SELECT $asset_revision_id, $profile_hash, $processing_run_id, $completed_at_utc
            WHERE EXISTS (
                SELECT 1
                FROM archive_analysis_runs
                WHERE processing_run_id = $processing_run_id
                  AND profile_hash = $profile_hash)
            ON CONFLICT(asset_revision_id, profile_hash) DO UPDATE SET
                processing_run_id = excluded.processing_run_id,
                completed_at_utc = excluded.completed_at_utc;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
        command.Parameters.AddWithValue("$processing_run_id", runId.ToString());
        command.Parameters.AddWithValue("$completed_at_utc", Format(completedAtUtc));
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Processing run {runId} is not registered for analysis profile {profileHash}.");
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await new SqliteArchiveSourceObservationRepository(_database).EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS archive_analysis_profiles (
                profile_hash TEXT NOT NULL PRIMARY KEY,
                detector_pipeline_hash TEXT NOT NULL,
                detector_model_id TEXT NOT NULL,
                detector_model_hash TEXT NOT NULL,
                embedder_model_id TEXT NOT NULL,
                embedder_model_hash TEXT NOT NULL,
                alignment_protocol TEXT NOT NULL,
                canonical_definition TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS archive_analysis_runs (
                processing_run_id TEXT NOT NULL PRIMARY KEY,
                profile_hash TEXT NOT NULL,
                registered_at_utc TEXT NOT NULL,
                FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE,
                FOREIGN KEY (profile_hash) REFERENCES archive_analysis_profiles (profile_hash) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS asset_revision_analysis (
                asset_revision_id TEXT NOT NULL,
                profile_hash TEXT NOT NULL,
                processing_run_id TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                PRIMARY KEY (asset_revision_id, profile_hash),
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                FOREIGN KEY (profile_hash) REFERENCES archive_analysis_profiles (profile_hash) ON DELETE RESTRICT,
                FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_asset_revision_analysis_profile
                ON asset_revision_analysis (profile_hash, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
