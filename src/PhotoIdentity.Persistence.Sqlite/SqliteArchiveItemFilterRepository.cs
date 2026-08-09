using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteArchiveItemFilterRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveItemFilterRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
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
        // Reuse the status repository's lazy analysis schema initialization before issuing the
        // orthogonal item query.
        _ = await new SqliteArchiveStatusRepository(_database).GetStatusAsync(
            sourceId,
            relativeFolder,
            profileHash,
            cancellationToken);

        string folder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";
        string normalizedAvailability = NormalizeAvailability(availability);
        string normalizedVerification = NormalizeVerification(verification);
        string normalizedAnalysis = NormalizeAnalysis(analysis);
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
                COUNT(*) OVER() AS total_count
            FROM classified
            WHERE ($availability = 'all' OR availability = $availability)
              AND ($verification = 'all' OR verification_state = $verification)
              AND ($analysis = 'all' OR analysis_state = $analysis)
            ORDER BY source_key
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$profile_hash", profileHash?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("$availability", normalizedAvailability);
        command.Parameters.AddWithValue("$verification", normalizedVerification);
        command.Parameters.AddWithValue("$analysis", normalizedAnalysis);
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
