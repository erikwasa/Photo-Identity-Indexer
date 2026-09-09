using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL archive status and item-paging read model.
/// </summary>
public sealed class PostgresArchiveStatusRepository : IArchiveStatusRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveStatusRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueArchiveFolderStatus> GetStatusAsync(
        SourceId sourceId,
        string relativeFolder,
        Sha256Digest? profileHash,
        CancellationToken cancellationToken = default)
    {
        string folder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH current_revision AS (
                SELECT
                    asset.id AS asset_id,
                    asset.source_key,
                    asset.deleted_at_utc,
                    COALESCE(availability.availability, 'local') AS availability,
                    revision.id AS revision_id,
                    COALESCE(
                        source_observation.verification_state,
                        CASE WHEN revision.id IS NULL THEN 'unverified' ELSE 'verified' END) AS verification_state
                FROM assets AS asset
                LEFT JOIN archive_asset_availability AS availability
                    ON availability.asset_id = asset.id
                LEFT JOIN asset_revisions AS revision
                    ON revision.id = (
                        SELECT candidate.id
                        FROM asset_revisions AS candidate
                        WHERE candidate.asset_id = asset.id
                        ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                        LIMIT 1)
                LEFT JOIN archive_source_observations AS source_observation
                    ON source_observation.asset_id = asset.id
                WHERE asset.source_id = @source_id
                  AND (
                      @folder = '' OR
                      asset.source_key = @folder OR
                      substr(asset.source_key, 1, length(@prefix)) = @prefix)
            ),
            latest_analysis_job AS (
                SELECT asset_revision_id, status
                FROM (
                    SELECT
                        job.asset_revision_id,
                        job.status,
                        ROW_NUMBER() OVER (
                            PARTITION BY job.asset_revision_id
                            ORDER BY run.started_at_utc DESC, job.id DESC) AS row_number
                    FROM processing_jobs AS job
                    INNER JOIN processing_runs AS run
                        ON run.id = job.processing_run_id
                    INNER JOIN archive_analysis_runs AS archive_run
                        ON archive_run.processing_run_id = run.id
                    WHERE archive_run.profile_hash = @profile_hash
                ) AS ranked
                WHERE row_number = 1
            )
            SELECT
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NULL)::integer,
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NULL AND current.availability = 'local')::integer,
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NULL AND current.availability = 'online-only')::integer,
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NULL AND current.availability = 'downloading')::integer,
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NULL AND current.availability = 'unavailable')::integer,
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NULL AND current.availability = 'error')::integer,
                COUNT(*) FILTER (
                    WHERE current.deleted_at_utc IS NULL
                      AND current.verification_state = 'verified'
                      AND analysis.asset_revision_id IS NOT NULL)::integer,
                COUNT(*) FILTER (
                    WHERE current.deleted_at_utc IS NULL
                      AND current.verification_state = 'verified'
                      AND current.revision_id IS NOT NULL
                      AND analysis.asset_revision_id IS NULL
                      AND COALESCE(latest_job.status, '') <> 'failed')::integer,
                COUNT(*) FILTER (
                    WHERE current.deleted_at_utc IS NULL
                      AND current.verification_state = 'verified'
                      AND current.revision_id IS NOT NULL
                      AND analysis.asset_revision_id IS NULL
                      AND latest_job.status = 'failed')::integer,
                COUNT(*) FILTER (
                    WHERE current.deleted_at_utc IS NULL
                      AND current.verification_state = 'needs-source-verification')::integer,
                COUNT(*) FILTER (
                    WHERE current.deleted_at_utc IS NULL
                      AND current.verification_state = 'unverified')::integer,
                COUNT(*) FILTER (WHERE current.deleted_at_utc IS NOT NULL)::integer
            FROM current_revision AS current
            LEFT JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = current.revision_id
               AND analysis.profile_hash = @profile_hash
            LEFT JOIN latest_analysis_job AS latest_job
                ON latest_job.asset_revision_id = current.revision_id;
            """;
        AddCommonParameters(command, sourceId, folder, prefix, profileHash);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);
        return new CatalogueArchiveFolderStatus(
            folder,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11));
    }

    public async Task<CatalogueArchiveItemPage> GetItemsAsync(
        SourceId sourceId,
        string relativeFolder,
        Sha256Digest? profileHash,
        string state,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        string folder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";
        string normalizedState = NormalizeState(state);
        ValidatePaging(offset, limit);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH asset_state AS (
                SELECT
                    asset.source_key,
                    asset.deleted_at_utc,
                    COALESCE(availability.availability, 'local') AS availability,
                    revision.id AS revision_id,
                    COALESCE(
                        source_observation.verification_state,
                        CASE WHEN revision.id IS NULL THEN 'unverified' ELSE 'verified' END) AS verification_state,
                    analysis.asset_revision_id AS analysed_revision_id,
                    latest_job.status AS latest_job_status,
                    latest_job.error AS latest_job_error
                FROM assets AS asset
                LEFT JOIN archive_asset_availability AS availability
                    ON availability.asset_id = asset.id
                LEFT JOIN asset_revisions AS revision
                    ON revision.id = (
                        SELECT candidate.id
                        FROM asset_revisions AS candidate
                        WHERE candidate.asset_id = asset.id
                        ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                        LIMIT 1)
                LEFT JOIN archive_source_observations AS source_observation
                    ON source_observation.asset_id = asset.id
                LEFT JOIN asset_revision_analysis AS analysis
                    ON analysis.asset_revision_id = revision.id
                   AND analysis.profile_hash = @profile_hash
                LEFT JOIN (
                    SELECT asset_revision_id, status, error
                    FROM (
                        SELECT
                            job.asset_revision_id,
                            job.status,
                            job.error,
                            ROW_NUMBER() OVER (
                                PARTITION BY job.asset_revision_id
                                ORDER BY run.started_at_utc DESC, job.id DESC) AS row_number
                        FROM processing_jobs AS job
                        INNER JOIN processing_runs AS run
                            ON run.id = job.processing_run_id
                        INNER JOIN archive_analysis_runs AS archive_run
                            ON archive_run.processing_run_id = run.id
                        WHERE archive_run.profile_hash = @profile_hash
                    ) AS ranked
                    WHERE row_number = 1
                ) AS latest_job
                    ON latest_job.asset_revision_id = revision.id
                WHERE asset.source_id = @source_id
                  AND (
                      @folder = '' OR
                      asset.source_key = @folder OR
                      substr(asset.source_key, 1, length(@prefix)) = @prefix)
            ),
            classified AS (
                SELECT
                    source_key,
                    revision_id,
                    availability,
                    verification_state,
                    CASE
                        WHEN deleted_at_utc IS NOT NULL THEN 'missing'
                        WHEN analysed_revision_id IS NOT NULL THEN 'analysed'
                        WHEN verification_state = 'needs-source-verification' THEN 'needs-source-verification'
                        WHEN verification_state = 'unverified' THEN 'unverified'
                        WHEN latest_job_status = 'failed' THEN 'failed'
                        WHEN availability <> 'local' THEN 'unavailable'
                        ELSE 'pending'
                    END AS analysis_state,
                    latest_job_error
                FROM asset_state
            )
            SELECT
                source_key,
                revision_id,
                availability,
                verification_state,
                analysis_state,
                latest_job_error,
                COUNT(*) OVER()::integer AS total_count
            FROM classified
            WHERE @state = 'all' OR analysis_state = @state
            ORDER BY source_key
            LIMIT @limit OFFSET @offset;
            """;
        AddCommonParameters(command, sourceId, folder, prefix, profileHash);
        command.Parameters.AddWithValue("state", normalizedState);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", limit);

        return await ReadItemPageAsync(
            command,
            offset,
            limit,
            cancellationToken);
    }

    public async Task<CatalogueArchiveItemPage> GetItemsAsync(
        SourceId sourceId,
        string relativeFolder,
        Sha256Digest? profileHash,
        string availability,
        string verification,
        string analysis,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        string folder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";
        string normalizedAvailability = NormalizeAvailability(availability);
        string normalizedVerification = NormalizeVerification(verification);
        string normalizedAnalysis = NormalizeAnalysis(analysis);
        ValidatePaging(offset, limit);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH asset_state AS (
                SELECT
                    asset.source_key,
                    asset.deleted_at_utc,
                    COALESCE(availability.availability, 'local') AS availability,
                    revision.id AS revision_id,
                    COALESCE(
                        source_observation.verification_state,
                        CASE WHEN revision.id IS NULL THEN 'unverified' ELSE 'verified' END) AS verification_state,
                    analysis.asset_revision_id AS analysed_revision_id,
                    latest_job.status AS latest_job_status,
                    latest_job.error AS latest_job_error
                FROM assets AS asset
                LEFT JOIN archive_asset_availability AS availability
                    ON availability.asset_id = asset.id
                LEFT JOIN asset_revisions AS revision
                    ON revision.id = (
                        SELECT candidate.id
                        FROM asset_revisions AS candidate
                        WHERE candidate.asset_id = asset.id
                        ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                        LIMIT 1)
                LEFT JOIN archive_source_observations AS source_observation
                    ON source_observation.asset_id = asset.id
                LEFT JOIN asset_revision_analysis AS analysis
                    ON analysis.asset_revision_id = revision.id
                   AND analysis.profile_hash = @profile_hash
                LEFT JOIN (
                    SELECT asset_revision_id, status, error
                    FROM (
                        SELECT
                            job.asset_revision_id,
                            job.status,
                            job.error,
                            ROW_NUMBER() OVER (
                                PARTITION BY job.asset_revision_id
                                ORDER BY run.started_at_utc DESC, job.id DESC) AS row_number
                        FROM processing_jobs AS job
                        INNER JOIN processing_runs AS run
                            ON run.id = job.processing_run_id
                        INNER JOIN archive_analysis_runs AS archive_run
                            ON archive_run.processing_run_id = run.id
                        WHERE archive_run.profile_hash = @profile_hash
                    ) AS ranked
                    WHERE row_number = 1
                ) AS latest_job
                    ON latest_job.asset_revision_id = revision.id
                WHERE asset.source_id = @source_id
                  AND (
                      @folder = '' OR
                      asset.source_key = @folder OR
                      substr(asset.source_key, 1, length(@prefix)) = @prefix)
            ),
            classified AS (
                SELECT
                    source_key,
                    revision_id,
                    availability,
                    verification_state,
                    CASE
                        WHEN deleted_at_utc IS NOT NULL THEN 'missing'
                        WHEN analysed_revision_id IS NOT NULL THEN 'analysed'
                        WHEN latest_job_status = 'failed' THEN 'failed'
                        WHEN revision_id IS NULL OR verification_state <> 'verified' THEN 'not-ready'
                        ELSE 'pending'
                    END AS analysis_state,
                    latest_job_error
                FROM asset_state
            )
            SELECT
                source_key,
                revision_id,
                availability,
                verification_state,
                analysis_state,
                latest_job_error,
                COUNT(*) OVER()::integer AS total_count
            FROM classified
            WHERE (@availability = 'all' OR availability = @availability)
              AND (@verification = 'all' OR verification_state = @verification)
              AND (@analysis = 'all' OR analysis_state = @analysis)
            ORDER BY source_key
            LIMIT @limit OFFSET @offset;
            """;
        AddCommonParameters(command, sourceId, folder, prefix, profileHash);
        command.Parameters.AddWithValue("availability", normalizedAvailability);
        command.Parameters.AddWithValue("verification", normalizedVerification);
        command.Parameters.AddWithValue("analysis", normalizedAnalysis);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", limit);

        return await ReadItemPageAsync(
            command,
            offset,
            limit,
            cancellationToken);
    }

    public async Task<CatalogueArchiveRunStatus?> GetLatestRunAsync(
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                run.id,
                run.status,
                run.started_at_utc,
                run.completed_at_utc,
                COUNT(job.id)::integer AS total_jobs,
                COUNT(job.id) FILTER (WHERE job.status = 'queued')::integer AS queued_jobs,
                COUNT(job.id) FILTER (WHERE job.status = 'running')::integer AS running_jobs,
                COUNT(job.id) FILTER (WHERE job.status = 'succeeded')::integer AS succeeded_jobs,
                COUNT(job.id) FILTER (WHERE job.status = 'failed')::integer AS failed_jobs,
                COUNT(job.id) FILTER (WHERE job.status = 'cancelled')::integer AS cancelled_jobs
            FROM archive_analysis_runs AS archive_run
            INNER JOIN processing_runs AS run
                ON run.id = archive_run.processing_run_id
            LEFT JOIN processing_jobs AS job
                ON job.processing_run_id = run.id
            WHERE archive_run.profile_hash = @profile_hash
            GROUP BY run.id, run.status, run.started_at_utc, run.completed_at_utc
            ORDER BY run.started_at_utc DESC, run.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "profile_hash",
            profileHash.ToString());

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueArchiveRunStatus(
            ProcessingRunId.From(reader.GetGuid(0)),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9));
    }

    private static void AddCommonParameters(
        NpgsqlCommand command,
        SourceId sourceId,
        string folder,
        string prefix,
        Sha256Digest? profileHash)
    {
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("folder", folder);
        command.Parameters.AddWithValue("prefix", prefix);
        command.Parameters.AddWithValue(
            "profile_hash",
            profileHash?.ToString() ?? string.Empty);
    }

    private static async Task<CatalogueArchiveItemPage> ReadItemPageAsync(
        NpgsqlCommand command,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        List<CatalogueArchiveItemStatus> items = [];
        int total = 0;
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                total = reader.GetInt32(6);
            }

            items.Add(new CatalogueArchiveItemStatus(
                reader.GetString(0),
                reader.IsDBNull(1)
                    ? null
                    : AssetRevisionId.From(reader.GetGuid(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return new CatalogueArchiveItemPage(offset, limit, total, items);
    }

    private static void ValidatePaging(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 200);
    }

    private static string NormalizeState(string state) => state.Trim().ToLowerInvariant() switch
    {
        "all" => "all",
        "analysed" or "analyzed" => "analysed",
        "pending" => "pending",
        "failed" => "failed",
        "unavailable" => "unavailable",
        "needs-source-verification" or "needs-verification" => "needs-source-verification",
        "unverified" => "unverified",
        "missing" => "missing",
        _ => throw new ArgumentException($"Unknown archive item state '{state}'.", nameof(state)),
    };

    private static string NormalizeAvailability(string value) => value.Trim().ToLowerInvariant() switch
    {
        "" or "all" => "all",
        "local" => "local",
        "online-only" => "online-only",
        "downloading" => "downloading",
        "unavailable" => "unavailable",
        "error" => "error",
        _ => throw new ArgumentException($"Unknown archive availability filter '{value}'.", nameof(value)),
    };

    private static string NormalizeVerification(string value) => value.Trim().ToLowerInvariant() switch
    {
        "" or "all" => "all",
        "verified" => "verified",
        "needs-source-verification" or "needs-verification" => "needs-source-verification",
        "unverified" => "unverified",
        _ => throw new ArgumentException($"Unknown archive verification filter '{value}'.", nameof(value)),
    };

    private static string NormalizeAnalysis(string value) => value.Trim().ToLowerInvariant() switch
    {
        "" or "all" => "all",
        "analysed" or "analyzed" => "analysed",
        "pending" => "pending",
        "failed" => "failed",
        "not-ready" => "not-ready",
        "missing" => "missing",
        _ => throw new ArgumentException($"Unknown archive analysis filter '{value}'.", nameof(value)),
    };
}
