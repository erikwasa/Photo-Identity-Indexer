using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Owns the PostgreSQL connection pool and versioned migration bootstrap while
/// PostgreSQL is introduced alongside the still-authoritative SQLite catalogue.
/// </summary>
public sealed class PostgresCatalogueDatabase : IAsyncDisposable
{
    public const int CurrentSchemaVersion = 2;

    private const long MigrationAdvisoryLockKey = 504091701;

    private static readonly Migration[] Migrations =
    [
        new(
            1,
            "postgres-runtime-foundation",
            """
            SELECT 1;
            """),
        new(
            2,
            "foundational-catalogue-and-processing-schema",
            """
            CREATE TABLE sources (
                id uuid NOT NULL PRIMARY KEY,
                kind text NOT NULL CHECK (btrim(kind) <> ''),
                root_locator text NOT NULL CHECK (btrim(root_locator) <> ''),
                created_at_utc timestamp with time zone NOT NULL,
                UNIQUE (kind, root_locator)
            );

            CREATE TABLE assets (
                id uuid NOT NULL PRIMARY KEY,
                source_id uuid NOT NULL,
                source_key text NOT NULL CHECK (btrim(source_key) <> ''),
                created_at_utc timestamp with time zone NOT NULL,
                last_seen_at_utc timestamp with time zone NULL,
                deleted_at_utc timestamp with time zone NULL,
                CONSTRAINT fk_assets_source
                    FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE RESTRICT,
                UNIQUE (source_id, source_key),
                CHECK (last_seen_at_utc IS NULL OR last_seen_at_utc >= created_at_utc),
                CHECK (deleted_at_utc IS NULL OR deleted_at_utc >= created_at_utc)
            );

            CREATE TABLE asset_revisions (
                id uuid NOT NULL PRIMARY KEY,
                asset_id uuid NOT NULL,
                content_sha256 text NOT NULL
                    CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                size_bytes bigint NOT NULL CHECK (size_bytes >= 0),
                observed_at_utc timestamp with time zone NOT NULL,
                media_type text NULL,
                width integer NULL CHECK (width IS NULL OR width > 0),
                height integer NULL CHECK (height IS NULL OR height > 0),
                CONSTRAINT fk_asset_revisions_asset
                    FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE,
                UNIQUE (asset_id, content_sha256)
            );

            CREATE OR REPLACE FUNCTION photo_identity_guard_asset_revision_identity()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $
            BEGIN
                IF NEW.id <> OLD.id
                   OR NEW.asset_id <> OLD.asset_id
                   OR NEW.content_sha256 <> OLD.content_sha256 THEN
                    RAISE EXCEPTION 'asset revision identity is immutable';
                END IF;
                RETURN NEW;
            END;
            $;

            CREATE TRIGGER trg_asset_revision_identity_immutable
                BEFORE UPDATE ON asset_revisions
                FOR EACH ROW
                EXECUTE FUNCTION photo_identity_guard_asset_revision_identity();

            CREATE TABLE face_occurrences (
                id uuid NOT NULL PRIMARY KEY,
                asset_revision_id uuid NOT NULL,
                ordinal integer NOT NULL CHECK (ordinal >= 0),
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_face_occurrences_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                UNIQUE (asset_revision_id, ordinal)
            );

            CREATE TABLE face_observations (
                face_occurrence_id uuid NOT NULL,
                detector_model_id text NOT NULL CHECK (btrim(detector_model_id) <> ''),
                detector_model_hash text NOT NULL
                    CHECK (detector_model_hash ~ '^[0-9a-f]{64}$'),
                confidence double precision NOT NULL
                    CHECK (confidence >= 0 AND confidence <= 1),
                bounding_box_json jsonb NOT NULL,
                landmarks_json jsonb NOT NULL,
                observed_at_utc timestamp with time zone NOT NULL,
                detector_pipeline_hash text NULL
                    CHECK (
                        detector_pipeline_hash IS NULL
                        OR detector_pipeline_hash ~ '^[0-9a-f]{64}$'),
                PRIMARY KEY (
                    face_occurrence_id,
                    detector_model_id,
                    detector_model_hash),
                CONSTRAINT fk_face_observations_occurrence
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE
            );

            CREATE TABLE face_crops (
                id uuid NOT NULL PRIMARY KEY,
                face_occurrence_id uuid NOT NULL,
                crop_protocol text NOT NULL CHECK (btrim(crop_protocol) <> ''),
                content_sha256 text NOT NULL
                    CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                storage_path text NOT NULL CHECK (btrim(storage_path) <> ''),
                width integer NOT NULL CHECK (width > 0),
                height integer NOT NULL CHECK (height > 0),
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_face_crops_occurrence
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                UNIQUE (face_occurrence_id, crop_protocol, content_sha256)
            );

            CREATE TABLE embeddings (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                face_crop_id uuid NOT NULL,
                model_id text NOT NULL CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                dimensions integer NOT NULL CHECK (dimensions > 0),
                l2_norm double precision NOT NULL CHECK (l2_norm > 0),
                vector_blob bytea NOT NULL,
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_embeddings_crop
                    FOREIGN KEY (face_crop_id)
                    REFERENCES face_crops (id) ON DELETE CASCADE,
                UNIQUE (face_crop_id, model_id, model_hash)
            );

            CREATE TABLE processing_runs (
                id uuid NOT NULL PRIMARY KEY,
                status text NOT NULL
                    CHECK (status IN (
                        'pending',
                        'running',
                        'completed',
                        'failed',
                        'cancelled')),
                configuration_json jsonb NOT NULL,
                started_at_utc timestamp with time zone NOT NULL,
                completed_at_utc timestamp with time zone NULL,
                error text NULL,
                cancellation_requested_at_utc timestamp with time zone NULL,
                CHECK (
                    completed_at_utc IS NULL
                    OR completed_at_utc >= started_at_utc),
                CHECK (
                    cancellation_requested_at_utc IS NULL
                    OR cancellation_requested_at_utc >= started_at_utc),
                CHECK (
                    (status IN ('completed', 'failed', 'cancelled'))
                    = (completed_at_utc IS NOT NULL)),
                CHECK (status <> 'failed' OR error IS NOT NULL)
            );

            CREATE TABLE processing_jobs (
                id uuid NOT NULL PRIMARY KEY,
                processing_run_id uuid NOT NULL,
                asset_revision_id uuid NOT NULL,
                status text NOT NULL
                    CHECK (status IN (
                        'queued',
                        'running',
                        'succeeded',
                        'failed',
                        'cancelled')),
                attempt_count integer NOT NULL DEFAULT 0
                    CHECK (attempt_count >= 0),
                available_at_utc timestamp with time zone NOT NULL,
                started_at_utc timestamp with time zone NULL,
                completed_at_utc timestamp with time zone NULL,
                error text NULL,
                idempotency_key text NOT NULL CHECK (btrim(idempotency_key) <> ''),
                lease_token uuid NULL,
                leased_until_utc timestamp with time zone NULL,
                checkpoint_json jsonb NULL,
                last_failure_kind text NULL
                    CHECK (
                        last_failure_kind IS NULL
                        OR last_failure_kind IN ('transient', 'permanent')),
                CONSTRAINT fk_processing_jobs_run
                    FOREIGN KEY (processing_run_id)
                    REFERENCES processing_runs (id) ON DELETE CASCADE,
                CONSTRAINT fk_processing_jobs_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                UNIQUE (processing_run_id, asset_revision_id),
                UNIQUE (idempotency_key),
                CHECK (
                    (lease_token IS NULL) = (leased_until_utc IS NULL)),
                CHECK (
                    status = 'running'
                    OR (lease_token IS NULL AND leased_until_utc IS NULL)),
                CHECK (
                    status <> 'running'
                    OR (started_at_utc IS NOT NULL
                        AND attempt_count > 0
                        AND lease_token IS NOT NULL
                        AND leased_until_utc IS NOT NULL)),
                CHECK (
                    status NOT IN ('succeeded', 'failed', 'cancelled')
                    OR completed_at_utc IS NOT NULL),
                CHECK (status <> 'failed' OR error IS NOT NULL)
            );

            CREATE INDEX ix_assets_source_presence
                ON assets (source_id, deleted_at_utc, source_key);
            CREATE INDEX ix_asset_revisions_asset_observed
                ON asset_revisions (asset_id, observed_at_utc DESC);
            CREATE INDEX ix_face_occurrences_revision
                ON face_occurrences (asset_revision_id, ordinal);
            CREATE UNIQUE INDEX ux_face_observations_pipeline
                ON face_observations (face_occurrence_id, detector_pipeline_hash)
                WHERE detector_pipeline_hash IS NOT NULL;
            CREATE INDEX ix_embeddings_crop
                ON embeddings (face_crop_id);
            CREATE INDEX ix_processing_jobs_ready
                ON processing_jobs (status, available_at_utc);
            CREATE INDEX ix_processing_jobs_claimable
                ON processing_jobs (
                    processing_run_id,
                    status,
                    available_at_utc,
                    leased_until_utc);
            """),
    ];

    private readonly NpgsqlDataSource _dataSource;

    public PostgresCatalogueDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlDataSourceBuilder builder = new(connectionString);
        builder.ConnectionStringBuilder.ApplicationName = "PhotoIdentity";
        _dataSource = builder.Build();
    }

    public async Task<PostgresInitializationResult> TryInitializeAsync(
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection;
        try
        {
            connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsAuthenticationFailure(exception))
        {
            return new(
                PostgresCatalogueHealth.AuthenticationFailed,
                exception);
        }
        catch (Exception exception) when (IsConnectionUnavailable(exception))
        {
            return new(
                PostgresCatalogueHealth.Unavailable,
                exception);
        }

        await using (connection)
        {
            try
            {
                int schemaVersion = await InitializeAsync(connection, cancellationToken);
                return new(
                    PostgresCatalogueHealth.Ready(schemaVersion),
                    null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(
                    PostgresCatalogueHealth.MigrationFailed,
                    exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    private static async Task<int> InitializeAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand migrationLock = connection.CreateCommand())
        {
            migrationLock.Transaction = transaction;
            migrationLock.CommandText =
                "SELECT pg_advisory_xact_lock(@migration_lock_key);";
            migrationLock.Parameters.AddWithValue(
                "migration_lock_key",
                MigrationAdvisoryLockKey);
            await migrationLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand ensureHistory = connection.CreateCommand())
        {
            ensureHistory.Transaction = transaction;
            ensureHistory.CommandText =
                """
                CREATE TABLE IF NOT EXISTS photo_identity_schema_migrations (
                    version integer NOT NULL PRIMARY KEY,
                    name text NOT NULL,
                    applied_at_utc timestamp with time zone NOT NULL
                );
                """;
            await ensureHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        HashSet<int> appliedVersions = [];
        await using (NpgsqlCommand readHistory = connection.CreateCommand())
        {
            readHistory.Transaction = transaction;
            readHistory.CommandText =
                """
                SELECT version
                FROM photo_identity_schema_migrations
                ORDER BY version;
                """;

            await using NpgsqlDataReader reader =
                await readHistory.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int version = reader.GetInt32(0);
                if (version > CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL catalogue schema version {version} is newer than supported version {CurrentSchemaVersion}.");
                }

                appliedVersions.Add(version);
            }
        }

        foreach (Migration migration in Migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            await using (NpgsqlCommand applyMigration = connection.CreateCommand())
            {
                applyMigration.Transaction = transaction;
                applyMigration.CommandText = migration.Sql;
                await applyMigration.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (NpgsqlCommand recordMigration = connection.CreateCommand())
            {
                recordMigration.Transaction = transaction;
                recordMigration.CommandText =
                    """
                    INSERT INTO photo_identity_schema_migrations (
                        version,
                        name,
                        applied_at_utc)
                    VALUES (
                        @version,
                        @name,
                        @applied_at_utc);
                    """;
                recordMigration.Parameters.AddWithValue(
                    "version",
                    migration.Version);
                recordMigration.Parameters.AddWithValue(
                    "name",
                    migration.Name);
                recordMigration.Parameters.AddWithValue(
                    "applied_at_utc",
                    DateTimeOffset.UtcNow);
                await recordMigration.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return CurrentSchemaVersion;
    }

    private static bool IsAuthenticationFailure(PostgresException exception) =>
        exception.SqlState.StartsWith("28", StringComparison.Ordinal);

    private static bool IsConnectionUnavailable(Exception exception) =>
        exception is NpgsqlException or TimeoutException;

    private sealed record Migration(int Version, string Name, string Sql);
}
