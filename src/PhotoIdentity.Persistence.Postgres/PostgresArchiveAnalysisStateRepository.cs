using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Persists exact archive-analysis profile registration and successful immutable-revision completion in PostgreSQL.
/// </summary>
public sealed class PostgresArchiveAnalysisStateRepository : IArchiveAnalysisStateRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveAnalysisStateRepository(PostgresCatalogueDatabase database)
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

        Sha256Digest profileHash = profile.ComputeHash();
        DateTimeOffset recordedAt = recordedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand profileCommand = connection.CreateCommand())
        {
            profileCommand.Transaction = transaction;
            profileCommand.CommandText =
                """
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
                    @profile_hash,
                    @detector_pipeline_hash,
                    @detector_model_id,
                    @detector_model_hash,
                    @embedder_model_id,
                    @embedder_model_hash,
                    @alignment_protocol,
                    @canonical_definition,
                    @recorded_at_utc)
                ON CONFLICT(profile_hash) DO NOTHING;
                """;
            profileCommand.Parameters.AddWithValue(
                "profile_hash",
                profileHash.ToString());
            profileCommand.Parameters.AddWithValue(
                "detector_pipeline_hash",
                profile.DetectorPipelineHash.ToString());
            profileCommand.Parameters.AddWithValue(
                "detector_model_id",
                profile.DetectorModelId.ToString());
            profileCommand.Parameters.AddWithValue(
                "detector_model_hash",
                profile.DetectorModelHash.ToString());
            profileCommand.Parameters.AddWithValue(
                "embedder_model_id",
                profile.EmbedderModelId.ToString());
            profileCommand.Parameters.AddWithValue(
                "embedder_model_hash",
                profile.EmbedderModelHash.ToString());
            profileCommand.Parameters.AddWithValue(
                "alignment_protocol",
                profile.AlignmentProtocol.ToString());
            profileCommand.Parameters.AddWithValue(
                "canonical_definition",
                profile.ToCanonicalText());
            profileCommand.Parameters.AddWithValue(
                "recorded_at_utc",
                recordedAt);
            await profileCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand runCommand = connection.CreateCommand())
        {
            runCommand.Transaction = transaction;
            runCommand.CommandText =
                """
                INSERT INTO archive_analysis_runs (
                    processing_run_id,
                    profile_hash,
                    registered_at_utc)
                VALUES (
                    @processing_run_id,
                    @profile_hash,
                    @registered_at_utc)
                ON CONFLICT(processing_run_id) DO UPDATE SET
                    profile_hash = excluded.profile_hash;
                """;
            runCommand.Parameters.AddWithValue(
                "processing_run_id",
                Guid.Parse(runId.ToString()));
            runCommand.Parameters.AddWithValue(
                "profile_hash",
                profileHash.ToString());
            runCommand.Parameters.AddWithValue(
                "registered_at_utc",
                recordedAt);
            await runCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Sha256Digest> GetRunProfileHashAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT profile_hash
            FROM archive_analysis_runs
            WHERE processing_run_id = @processing_run_id;
            """;
        command.Parameters.AddWithValue(
            "processing_run_id",
            Guid.Parse(runId.ToString()));

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string hash
            ? new Sha256Digest(hash)
            : throw new KeyNotFoundException(
                $"Processing run {runId} is not a registered archive-analysis run.");
    }

    public async Task<bool> IsCompletedAsync(
        AssetRevisionId revisionId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM asset_revision_analysis
                WHERE asset_revision_id = @asset_revision_id
                  AND profile_hash = @profile_hash);
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "profile_hash",
            profileHash.ToString());

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is bool completed && completed;
    }

    public async Task RecordCompletionAsync(
        ProcessingRunId runId,
        AssetRevisionId revisionId,
        Sha256Digest profileHash,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO asset_revision_analysis (
                asset_revision_id,
                profile_hash,
                processing_run_id,
                completed_at_utc)
            SELECT
                @asset_revision_id,
                @profile_hash,
                @processing_run_id,
                @completed_at_utc
            WHERE EXISTS (
                SELECT 1
                FROM archive_analysis_runs
                WHERE processing_run_id = @processing_run_id
                  AND profile_hash = @profile_hash)
            ON CONFLICT(asset_revision_id, profile_hash) DO UPDATE SET
                processing_run_id = excluded.processing_run_id,
                completed_at_utc = excluded.completed_at_utc;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "profile_hash",
            profileHash.ToString());
        command.Parameters.AddWithValue(
            "processing_run_id",
            Guid.Parse(runId.ToString()));
        command.Parameters.AddWithValue(
            "completed_at_utc",
            completedAtUtc.ToUniversalTime());

        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Processing run {runId} is not registered for analysis profile {profileHash}.");
        }
    }
}
