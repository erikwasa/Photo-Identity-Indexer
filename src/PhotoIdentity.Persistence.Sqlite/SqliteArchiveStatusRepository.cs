using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueArchiveFolderStatus(
    string RelativeFolder,
    int CurrentImages,
    int LocalImages,
    int OnlineOnlyImages,
    int DownloadingImages,
    int UnavailableImages,
    int AvailabilityErrorImages,
    int AnalysedImages,
    int PendingImages,
    int FailedImages,
    int NeedsSourceVerificationImages,
    int UnverifiedSourceImages,
    int MissingImages);

public sealed record CatalogueArchiveItemStatus(
    string RelativePath,
    AssetRevisionId? RevisionId,
    string Availability,
    string SourceVerificationState,
    string AnalysisState,
    string? LastError);

public sealed record CatalogueArchiveItemPage(
    int Offset,
    int Limit,
    int Total,
    IReadOnlyList<CatalogueArchiveItemStatus> Items);

public sealed record CatalogueArchiveRunStatus(
    ProcessingRunId RunId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalJobs,
    int QueuedJobs,
    int RunningJobs,
    int SucceededJobs,
    int FailedJobs,
    int CancelledJobs);

/// <summary>
/// Reads permanent-archive coverage, OneDrive availability, source-verification state and
/// exact-profile analysis state without exposing the configured source root.
/// </summary>
public sealed class SqliteArchiveStatusRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveStatusRepository(SqliteCatalogueDatabase database)
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
        await EnsureSchemasAsync(cancellationToken);
        string folder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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
                WHERE asset.source_id = $source_id
                  AND (
                      $folder = '' OR
                      asset.source_key = $folder OR
                      substr(asset.source_key, 1, length($prefix)) = $prefix)
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
                    WHERE archive_run.profile_hash = $profile_hash
                ) AS ranked
                WHERE row_number = 1
            )
            SELECT
                SUM(CASE WHEN current.deleted_at_utc IS NULL THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL AND current.availability = 'local' THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL AND current.availability = 'online-only' THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL AND current.availability = 'downloading' THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL AND current.availability = 'unavailable' THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL AND current.availability = 'error' THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL
                              AND current.verification_state = 'verified'
                              AND analysis.asset_revision_id IS NOT NULL THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL
                              AND current.verification_state = 'verified'
                              AND current.availability = 'local'
                              AND current.revision_id IS NOT NULL
                              AND analysis.asset_revision_id IS NULL
                              AND COALESCE(latest_job.status, '') <> 'failed'
                         THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL
                              AND current.verification_state = 'verified'
                              AND current.availability = 'local'
                              AND current.revision_id IS NOT NULL
                              AND analysis.asset_revision_id IS NULL
                              AND latest_job.status = 'failed'
                         THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL
                              AND current.verification_state = 'needs-source-verification'
                         THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NULL
                              AND current.verification_state = 'unverified'
                         THEN 1 ELSE 0 END),
                SUM(CASE WHEN current.deleted_at_utc IS NOT NULL THEN 1 ELSE 0 END)
            FROM current_revision AS current
            LEFT JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = current.revision_id
               AND analysis.profile_hash = $profile_hash
            LEFT JOIN latest_analysis_job AS latest_job
                ON latest_job.asset_revision_id = current.revision_id;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$profile_hash", profileHash?.ToString() ?? string.Empty);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);
        return new CatalogueArchiveFolderStatus(
            folder,
            ReadCount(reader, 0),
            ReadCount(reader, 1),
            ReadCount(reader, 2),
            ReadCount(reader, 3),
            ReadCount(reader, 4),
            ReadCount(reader, 5),
            ReadCount(reader, 6),
            ReadCount(reader, 7),
            ReadCount(reader, 8),
            ReadCount(reader, 9),
            ReadCount(reader, 10),
            ReadCount(reader, 11));
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
        await EnsureSchemasAsync(cancellationToken);
        string folder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";
        string normalizedState = NormalizeState(state);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 200);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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
                   AND analysis.profile_hash = $profile_hash
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
                        WHERE archive_run.profile_hash = $profile_hash
                    ) AS ranked
                    WHERE row_number = 1
                ) AS latest_job
                    ON latest_job.asset_revision_id = revision.id
                WHERE asset.source_id = $source_id
                  AND (
                      $folder = '' OR
                      asset.source_key = $folder OR
                      substr(asset.source_key, 1, length($prefix)) = $prefix)
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
                COUNT(*) OVER() AS total_count
            FROM classified
            WHERE $state = 'all' OR analysis_state = $state
            ORDER BY source_key
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$profile_hash", profileHash?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("$state", normalizedState);
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$limit", limit);

        List<CatalogueArchiveItemStatus> items = [];
        int total = 0;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                total = reader.GetInt32(6);
            }

            items.Add(new CatalogueArchiveItemStatus(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return new CatalogueArchiveItemPage(offset, limit, total, items);
    }

    public async Task<CatalogueArchiveRunStatus?> GetLatestRunAsync(
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemasAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                run.id,
                run.status,
                run.started_at_utc,
                run.completed_at_utc,
                COUNT(job.id) AS total_jobs,
                SUM(CASE WHEN job.status = 'queued' THEN 1 ELSE 0 END) AS queued_jobs,
                SUM(CASE WHEN job.status = 'running' THEN 1 ELSE 0 END) AS running_jobs,
                SUM(CASE WHEN job.status = 'succeeded' THEN 1 ELSE 0 END) AS succeeded_jobs,
                SUM(CASE WHEN job.status = 'failed' THEN 1 ELSE 0 END) AS failed_jobs,
                SUM(CASE WHEN job.status = 'cancelled' THEN 1 ELSE 0 END) AS cancelled_jobs
            FROM archive_analysis_runs AS archive_run
            INNER JOIN processing_runs AS run
                ON run.id = archive_run.processing_run_id
            LEFT JOIN processing_jobs AS job
                ON job.processing_run_id = run.id
            WHERE archive_run.profile_hash = $profile_hash
            GROUP BY run.id, run.status, run.started_at_utc, run.completed_at_utc
            ORDER BY run.started_at_utc DESC, run.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile_hash", profileHash.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueArchiveRunStatus(
            ProcessingRunId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            ParseTimestamp(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
            ReadCount(reader, 4),
            ReadCount(reader, 5),
            ReadCount(reader, 6),
            ReadCount(reader, 7),
            ReadCount(reader, 8),
            ReadCount(reader, 9));
    }

    private async Task EnsureSchemasAsync(CancellationToken cancellationToken)
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

    private static int ReadCount(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetInt64(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
