using Microsoft.Data.Sqlite;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Creates and opens the local catalogue database owned by the SQLite adapter.
/// </summary>
public sealed class SqliteCatalogueDatabase
{
    public const int CurrentSchemaVersion = 12;

    private const string VersionOneSchema = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER NOT NULL PRIMARY KEY,
            applied_at_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS sources (
            id TEXT NOT NULL PRIMARY KEY,
            kind TEXT NOT NULL,
            root_locator TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            UNIQUE (kind, root_locator)
        );

        CREATE TABLE IF NOT EXISTS assets (
            id TEXT NOT NULL PRIMARY KEY,
            source_id TEXT NOT NULL,
            source_key TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE RESTRICT,
            UNIQUE (source_id, source_key)
        );

        CREATE TABLE IF NOT EXISTS asset_revisions (
            id TEXT NOT NULL PRIMARY KEY,
            asset_id TEXT NOT NULL,
            content_sha256 TEXT NOT NULL,
            size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
            observed_at_utc TEXT NOT NULL,
            media_type TEXT NULL,
            width INTEGER NULL CHECK (width IS NULL OR width > 0),
            height INTEGER NULL CHECK (height IS NULL OR height > 0),
            FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE,
            UNIQUE (asset_id, content_sha256)
        );

        CREATE TABLE IF NOT EXISTS face_occurrences (
            id TEXT NOT NULL PRIMARY KEY,
            asset_revision_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
            UNIQUE (asset_revision_id, ordinal)
        );

        CREATE TABLE IF NOT EXISTS face_observations (
            face_occurrence_id TEXT NOT NULL,
            detector_model_id TEXT NOT NULL,
            detector_model_hash TEXT NOT NULL,
            confidence REAL NOT NULL CHECK (confidence >= 0 AND confidence <= 1),
            bounding_box_json TEXT NOT NULL,
            landmarks_json TEXT NOT NULL,
            observed_at_utc TEXT NOT NULL,
            PRIMARY KEY (face_occurrence_id, detector_model_id, detector_model_hash),
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS face_crops (
            id TEXT NOT NULL PRIMARY KEY,
            face_occurrence_id TEXT NOT NULL,
            crop_protocol TEXT NOT NULL,
            content_sha256 TEXT NOT NULL,
            storage_path TEXT NOT NULL,
            width INTEGER NOT NULL CHECK (width > 0),
            height INTEGER NOT NULL CHECK (height > 0),
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
            UNIQUE (face_occurrence_id, crop_protocol, content_sha256)
        );

        CREATE TABLE IF NOT EXISTS embeddings (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            face_crop_id TEXT NOT NULL,
            model_id TEXT NOT NULL,
            model_hash TEXT NOT NULL,
            dimensions INTEGER NOT NULL CHECK (dimensions > 0),
            l2_norm REAL NOT NULL CHECK (l2_norm > 0),
            vector_blob BLOB NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (face_crop_id) REFERENCES face_crops (id) ON DELETE CASCADE,
            UNIQUE (face_crop_id, model_id, model_hash)
        );

        CREATE TABLE IF NOT EXISTS people (
            id TEXT NOT NULL PRIMARY KEY,
            display_name TEXT NULL,
            created_at_utc TEXT NOT NULL,
            merged_into_person_id TEXT NULL,
            FOREIGN KEY (merged_into_person_id) REFERENCES people (id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS person_labels (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            person_id TEXT NOT NULL,
            face_occurrence_id TEXT NOT NULL,
            label_kind TEXT NOT NULL,
            assigned_by TEXT NOT NULL,
            assigned_at_utc TEXT NOT NULL,
            note TEXT NULL,
            FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
            UNIQUE (person_id, face_occurrence_id, label_kind)
        );

        CREATE TABLE IF NOT EXISTS identity_suggestions (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            face_occurrence_id TEXT NOT NULL,
            suggested_person_id TEXT NOT NULL,
            model_id TEXT NOT NULL,
            model_hash TEXT NOT NULL,
            score REAL NOT NULL,
            status TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
            FOREIGN KEY (suggested_person_id) REFERENCES people (id) ON DELETE CASCADE,
            UNIQUE (face_occurrence_id, suggested_person_id, model_id, model_hash)
        );

        CREATE TABLE IF NOT EXISTS processing_runs (
            id TEXT NOT NULL PRIMARY KEY,
            status TEXT NOT NULL,
            configuration_json TEXT NOT NULL,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            error TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS processing_jobs (
            id TEXT NOT NULL PRIMARY KEY,
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            status TEXT NOT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
            available_at_utc TEXT NOT NULL,
            started_at_utc TEXT NULL,
            completed_at_utc TEXT NULL,
            error TEXT NULL,
            FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE,
            FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
            UNIQUE (processing_run_id, asset_revision_id)
        );

        CREATE INDEX IF NOT EXISTS ix_asset_revisions_asset_observed
            ON asset_revisions (asset_id, observed_at_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_face_occurrences_revision
            ON face_occurrences (asset_revision_id, ordinal);
        CREATE INDEX IF NOT EXISTS ix_embeddings_crop
            ON embeddings (face_crop_id);
        CREATE INDEX IF NOT EXISTS ix_person_labels_occurrence
            ON person_labels (face_occurrence_id);
        CREATE INDEX IF NOT EXISTS ix_identity_suggestions_occurrence
            ON identity_suggestions (face_occurrence_id, status);
        CREATE INDEX IF NOT EXISTS ix_processing_jobs_ready
            ON processing_jobs (status, available_at_utc);

        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 1;
        """;

    private const string VersionTwoMigration = """
        ALTER TABLE assets ADD COLUMN last_seen_at_utc TEXT NULL;
        ALTER TABLE assets ADD COLUMN deleted_at_utc TEXT NULL;
        UPDATE assets
        SET last_seen_at_utc = created_at_utc
        WHERE last_seen_at_utc IS NULL;
        CREATE INDEX IF NOT EXISTS ix_assets_source_presence
            ON assets (source_id, deleted_at_utc, source_key);
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 2;
        """;

    private const string VersionThreeMigration = """
        ALTER TABLE processing_runs ADD COLUMN cancellation_requested_at_utc TEXT NULL;
        ALTER TABLE processing_jobs ADD COLUMN idempotency_key TEXT NULL;
        ALTER TABLE processing_jobs ADD COLUMN lease_token TEXT NULL;
        ALTER TABLE processing_jobs ADD COLUMN leased_until_utc TEXT NULL;
        ALTER TABLE processing_jobs ADD COLUMN checkpoint_json TEXT NULL;
        ALTER TABLE processing_jobs ADD COLUMN last_failure_kind TEXT NULL;
        UPDATE processing_jobs
        SET idempotency_key = processing_run_id || ':' || asset_revision_id
        WHERE idempotency_key IS NULL;
        UPDATE processing_jobs
        SET status = 'queued',
            started_at_utc = NULL,
            completed_at_utc = NULL,
            lease_token = NULL,
            leased_until_utc = NULL,
            last_failure_kind = 'transient',
            error = COALESCE(error, 'Recovered active job during schema version 3 upgrade.')
        WHERE status = 'running';
        CREATE UNIQUE INDEX IF NOT EXISTS ux_processing_jobs_idempotency
            ON processing_jobs (idempotency_key);
        CREATE INDEX IF NOT EXISTS ix_processing_jobs_claimable
            ON processing_jobs (processing_run_id, status, available_at_utc, leased_until_utc);
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 3;
        """;

    private const string VersionFourMigration = """
        CREATE TABLE review_actions (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            face_occurrence_id TEXT NOT NULL,
            action_kind TEXT NOT NULL CHECK (action_kind IN ('assign', 'reject', 'undo')),
            person_id TEXT NULL,
            person_label_id INTEGER NULL,
            actor TEXT NOT NULL,
            note TEXT NULL,
            created_at_utc TEXT NOT NULL,
            reversed_at_utc TEXT NULL,
            reverses_action_id INTEGER NULL,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
            FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE RESTRICT,
            FOREIGN KEY (person_label_id) REFERENCES person_labels (id) ON DELETE RESTRICT,
            FOREIGN KEY (reverses_action_id) REFERENCES review_actions (id) ON DELETE RESTRICT,
            CHECK (
                (action_kind = 'assign' AND person_id IS NOT NULL AND person_label_id IS NOT NULL AND reverses_action_id IS NULL)
                OR (action_kind = 'reject' AND person_id IS NULL AND person_label_id IS NULL AND reverses_action_id IS NULL)
                OR (action_kind = 'undo' AND reverses_action_id IS NOT NULL)
            )
        );
        CREATE INDEX ix_review_actions_face_history
            ON review_actions (face_occurrence_id, id DESC);
        CREATE INDEX ix_review_actions_face_active
            ON review_actions (face_occurrence_id, action_kind, reversed_at_utc, id DESC);
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (4, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 4;
        """;

    private const string VersionFiveMigration = """
        CREATE TABLE IF NOT EXISTS identity_suggestion_rankings (
            face_occurrence_id TEXT NOT NULL,
            model_id TEXT NOT NULL,
            model_hash TEXT NOT NULL,
            rank INTEGER NOT NULL CHECK (rank IN (1, 2)),
            suggestion_id INTEGER NOT NULL,
            score_margin REAL NULL CHECK (score_margin IS NULL OR score_margin >= 0),
            generated_at_utc TEXT NOT NULL,
            PRIMARY KEY (face_occurrence_id, model_id, model_hash, rank),
            UNIQUE (suggestion_id),
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
            FOREIGN KEY (suggestion_id) REFERENCES identity_suggestions (id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_identity_suggestion_rankings_model
            ON identity_suggestion_rankings (model_id, model_hash);
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (5, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 5;
        """;

    private const string VersionSixMigration = """
        CREATE TABLE IF NOT EXISTS identity_suggestion_review_actions (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            suggestion_id INTEGER NOT NULL,
            action_kind TEXT NOT NULL CHECK (action_kind IN ('accept', 'reject')),
            review_action_id INTEGER NULL,
            actor TEXT NOT NULL,
            note TEXT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (suggestion_id) REFERENCES identity_suggestions (id) ON DELETE CASCADE,
            FOREIGN KEY (review_action_id) REFERENCES review_actions (id) ON DELETE RESTRICT,
            CHECK (
                (action_kind = 'accept' AND review_action_id IS NOT NULL)
                OR (action_kind = 'reject' AND review_action_id IS NULL)
            )
        );
        CREATE INDEX IF NOT EXISTS ix_identity_suggestion_review_history
            ON identity_suggestion_review_actions (suggestion_id, id DESC);
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (6, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 6;
        """;

    private const string VersionSevenMigration = """
        CREATE TABLE IF NOT EXISTS person_maintenance_actions (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            action_kind TEXT NOT NULL CHECK (action_kind IN ('rename', 'merge')),
            person_id TEXT NOT NULL,
            previous_display_name TEXT NOT NULL,
            target_person_id TEXT NULL,
            new_display_name TEXT NOT NULL,
            actor TEXT NOT NULL,
            note TEXT NULL,
            created_at_utc TEXT NOT NULL,
            reversible INTEGER NOT NULL CHECK (reversible IN (0, 1)),
            FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE RESTRICT,
            FOREIGN KEY (target_person_id) REFERENCES people (id) ON DELETE RESTRICT,
            CHECK (
                (action_kind = 'rename' AND target_person_id IS NULL AND reversible = 1)
                OR (action_kind = 'merge' AND target_person_id IS NOT NULL AND reversible = 0)
            )
        );
        CREATE INDEX IF NOT EXISTS ix_person_maintenance_history
            ON person_maintenance_actions (id DESC);
        CREATE INDEX IF NOT EXISTS ix_person_maintenance_person
            ON person_maintenance_actions (person_id, id DESC);
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (7, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 7;
        """;

    private const string VersionEightMigration = """
        ALTER TABLE face_observations ADD COLUMN detector_pipeline_hash TEXT NULL;

        CREATE TABLE detector_pipelines (
            pipeline_hash TEXT NOT NULL PRIMARY KEY,
            detector_model_id TEXT NOT NULL,
            detector_model_hash TEXT NOT NULL,
            canonical_definition TEXT NOT NULL,
            recorded_at_utc TEXT NOT NULL
        );

        CREATE TABLE processing_run_detector_pipelines (
            processing_run_id TEXT NOT NULL PRIMARY KEY,
            pipeline_hash TEXT NOT NULL,
            recorded_at_utc TEXT NOT NULL,
            FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE,
            FOREIGN KEY (pipeline_hash) REFERENCES detector_pipelines (pipeline_hash) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_plans (
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            pipeline_hash TEXT NOT NULL,
            planned_at_utc TEXT NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id),
            FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE,
            FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
            FOREIGN KEY (pipeline_hash) REFERENCES detector_pipelines (pipeline_hash) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_candidates (
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            candidate_index INTEGER NOT NULL CHECK (candidate_index >= 0),
            disposition TEXT NOT NULL CHECK (disposition IN ('existing', 'new', 'ambiguous')),
            proposed_face_occurrence_id TEXT NULL,
            bounding_box_json TEXT NOT NULL,
            landmarks_json TEXT NOT NULL,
            applied_face_occurrence_id TEXT NULL,
            applied_at_utc TEXT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, candidate_index),
            FOREIGN KEY (processing_run_id, asset_revision_id)
                REFERENCES detector_reconciliation_plans (processing_run_id, asset_revision_id) ON DELETE CASCADE,
            FOREIGN KEY (proposed_face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT,
            FOREIGN KEY (applied_face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT,
            CHECK (
                (disposition = 'existing' AND proposed_face_occurrence_id IS NOT NULL)
                OR (disposition IN ('new', 'ambiguous') AND proposed_face_occurrence_id IS NULL)
            ),
            CHECK (
                (applied_face_occurrence_id IS NULL AND applied_at_utc IS NULL)
                OR (applied_face_occurrence_id IS NOT NULL AND applied_at_utc IS NOT NULL)
            )
        );

        CREATE TABLE detector_reconciliation_candidate_options (
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            candidate_index INTEGER NOT NULL,
            face_occurrence_id TEXT NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, candidate_index, face_occurrence_id),
            FOREIGN KEY (processing_run_id, asset_revision_id, candidate_index)
                REFERENCES detector_reconciliation_candidates (
                    processing_run_id, asset_revision_id, candidate_index) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_unmatched_existing (
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            face_occurrence_id TEXT NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, face_occurrence_id),
            FOREIGN KEY (processing_run_id, asset_revision_id)
                REFERENCES detector_reconciliation_plans (processing_run_id, asset_revision_id) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT
        );

        CREATE UNIQUE INDEX ux_face_observations_pipeline
            ON face_observations (face_occurrence_id, detector_pipeline_hash)
            WHERE detector_pipeline_hash IS NOT NULL;
        CREATE INDEX ix_detector_reconciliation_pending
            ON detector_reconciliation_candidates (disposition, applied_face_occurrence_id);
        CREATE INDEX ix_detector_reconciliation_revision
            ON detector_reconciliation_plans (asset_revision_id, pipeline_hash);

        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (8, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 8;
        """;

    private const string VersionNineMigration = """
        CREATE TABLE detector_reconciliation_candidate_inspections (
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            candidate_index INTEGER NOT NULL CHECK (candidate_index >= 0),
            detector_model_id TEXT NOT NULL,
            detector_model_hash TEXT NOT NULL,
            confidence REAL NOT NULL CHECK (confidence >= 0 AND confidence <= 1),
            crop_id TEXT NOT NULL,
            crop_protocol TEXT NOT NULL,
            crop_content_sha256 TEXT NOT NULL,
            crop_storage_path TEXT NOT NULL,
            crop_width INTEGER NOT NULL CHECK (crop_width > 0),
            crop_height INTEGER NOT NULL CHECK (crop_height > 0),
            embedder_model_id TEXT NOT NULL,
            embedder_model_hash TEXT NOT NULL,
            embedding_dimensions INTEGER NOT NULL CHECK (embedding_dimensions > 0),
            embedding_l2_norm REAL NOT NULL CHECK (embedding_l2_norm > 0),
            embedding_vector_blob BLOB NOT NULL,
            observed_at_utc TEXT NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, candidate_index),
            UNIQUE (crop_id),
            FOREIGN KEY (processing_run_id, asset_revision_id, candidate_index)
                REFERENCES detector_reconciliation_candidates (
                    processing_run_id, asset_revision_id, candidate_index) ON DELETE CASCADE
        );

        CREATE TABLE detector_reconciliation_resolution_actions (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            processing_run_id TEXT NOT NULL,
            asset_revision_id TEXT NOT NULL,
            candidate_index INTEGER NOT NULL CHECK (candidate_index >= 0),
            action_kind TEXT NOT NULL CHECK (action_kind IN ('existing', 'new', 'defer')),
            face_occurrence_id TEXT NULL,
            actor TEXT NOT NULL,
            note TEXT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (processing_run_id, asset_revision_id, candidate_index)
                REFERENCES detector_reconciliation_candidates (
                    processing_run_id, asset_revision_id, candidate_index) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT,
            CHECK (
                (action_kind = 'existing' AND face_occurrence_id IS NOT NULL)
                OR (action_kind IN ('new', 'defer') AND face_occurrence_id IS NULL)
            )
        );

        CREATE INDEX ix_detector_reconciliation_resolution_history
            ON detector_reconciliation_resolution_actions (
                processing_run_id, asset_revision_id, candidate_index, id DESC);
        CREATE INDEX ix_detector_reconciliation_inspection_model
            ON detector_reconciliation_candidate_inspections (
                detector_model_id, detector_model_hash, embedder_model_id, embedder_model_hash);

        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (9, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 9;
        """;

    private const string VersionTenMigration = """
        CREATE TABLE archive_configuration (
            id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
            source_id TEXT NOT NULL UNIQUE,
            configured_at_utc TEXT NOT NULL,
            FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE RESTRICT
        );

        CREATE TABLE archive_included_folders (
            source_id TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            included_at_utc TEXT NOT NULL,
            PRIMARY KEY (source_id, relative_path),
            FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE CASCADE
        );

        CREATE INDEX ix_archive_included_folders_path
            ON archive_included_folders (source_id, relative_path);

        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (10, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 10;
        """;

    private const string VersionElevenMigration = """
        CREATE TABLE identity_suggestion_policies (
            model_id TEXT NOT NULL,
            model_hash TEXT NOT NULL,
            policy_version INTEGER NOT NULL CHECK (policy_version >= 1),
            auto_assign_enabled INTEGER NOT NULL CHECK (auto_assign_enabled IN (0, 1)),
            high_score_threshold REAL NOT NULL CHECK (high_score_threshold >= 0 AND high_score_threshold <= 1),
            high_margin_threshold REAL NOT NULL CHECK (high_margin_threshold >= 0 AND high_margin_threshold <= 2),
            medium_score_threshold REAL NOT NULL CHECK (medium_score_threshold >= 0 AND medium_score_threshold <= 1),
            updated_by TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (model_id, model_hash),
            CHECK (medium_score_threshold <= high_score_threshold)
        );

        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (11, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 11;
        """;

    private const string VersionTwelveMigration = """
        CREATE TABLE review_actions_v12 (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            face_occurrence_id TEXT NOT NULL,
            action_kind TEXT NOT NULL CHECK (action_kind IN ('assign', 'unknown', 'reject', 'undo')),
            person_id TEXT NULL,
            person_label_id INTEGER NULL,
            actor TEXT NOT NULL,
            note TEXT NULL,
            created_at_utc TEXT NOT NULL,
            reversed_at_utc TEXT NULL,
            reverses_action_id INTEGER NULL,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
            FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE RESTRICT,
            FOREIGN KEY (person_label_id) REFERENCES person_labels (id) ON DELETE RESTRICT,
            FOREIGN KEY (reverses_action_id) REFERENCES review_actions_v12 (id) ON DELETE RESTRICT,
            CHECK (
                (action_kind = 'assign' AND person_id IS NOT NULL AND person_label_id IS NOT NULL AND reverses_action_id IS NULL)
                OR (action_kind IN ('unknown', 'reject') AND person_id IS NULL AND person_label_id IS NULL AND reverses_action_id IS NULL)
                OR (action_kind = 'undo' AND reverses_action_id IS NOT NULL)
            )
        );

        INSERT INTO review_actions_v12 (
            id,
            face_occurrence_id,
            action_kind,
            person_id,
            person_label_id,
            actor,
            note,
            created_at_utc,
            reversed_at_utc,
            reverses_action_id)
        SELECT
            id,
            face_occurrence_id,
            action_kind,
            person_id,
            person_label_id,
            actor,
            note,
            created_at_utc,
            reversed_at_utc,
            reverses_action_id
        FROM review_actions
        ORDER BY id;

        DROP TABLE review_actions;
        ALTER TABLE review_actions_v12 RENAME TO review_actions;

        CREATE INDEX ix_review_actions_face_history
            ON review_actions (face_occurrence_id, id DESC);
        CREATE INDEX ix_review_actions_face_active
            ON review_actions (face_occurrence_id, action_kind, reversed_at_utc, id DESC);

        CREATE TABLE IF NOT EXISTS person_favorites (
            person_id TEXT NOT NULL PRIMARY KEY,
            favorited_at_utc TEXT NOT NULL,
            FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE CASCADE
        );

        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (12, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        PRAGMA user_version = 12;
        """;

    private readonly string _connectionString;

    public SqliteCatalogueDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        int version = await ReadSchemaVersionAsync(connection, cancellationToken);

        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (version < 1)
        {
            await ApplyMigrationAsync(connection, VersionOneSchema, cancellationToken);
            version = 1;
        }

        if (version < 2)
        {
            await ApplyMigrationAsync(connection, VersionTwoMigration, cancellationToken);
            version = 2;
        }

        if (version < 3)
        {
            await ApplyMigrationAsync(connection, VersionThreeMigration, cancellationToken);
            version = 3;
        }

        if (version < 4)
        {
            await ApplyMigrationAsync(connection, VersionFourMigration, cancellationToken);
            version = 4;
        }

        if (version < 5)
        {
            await ApplyMigrationAsync(connection, VersionFiveMigration, cancellationToken);
            version = 5;
        }

        if (version < 6)
        {
            await ApplyMigrationAsync(connection, VersionSixMigration, cancellationToken);
            version = 6;
        }

        if (version < 7)
        {
            await ApplyMigrationAsync(connection, VersionSevenMigration, cancellationToken);
            version = 7;
        }

        if (version < 8)
        {
            await ApplyMigrationAsync(connection, VersionEightMigration, cancellationToken);
            version = 8;
        }

        if (version < 9)
        {
            await ApplyMigrationAsync(connection, VersionNineMigration, cancellationToken);
            version = 9;
        }

        if (version < 10)
        {
            await ApplyMigrationAsync(connection, VersionTenMigration, cancellationToken);
            version = 10;
        }

        if (version < 11)
        {
            await ApplyMigrationAsync(connection, VersionElevenMigration, cancellationToken);
            version = 11;
        }

        if (version < 12)
        {
            await ApplyMigrationWithForeignKeysDisabledAsync(
                connection,
                VersionTwelveMigration,
                cancellationToken);
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        SqliteConnection connection = new(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    private static async Task ApplyMigrationWithForeignKeysDisabledAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand disable = connection.CreateCommand())
        {
            disable.CommandText = "PRAGMA foreign_keys = OFF;";
            await disable.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            using (SqliteCommand check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "PRAGMA foreign_key_check;";
                await using SqliteDataReader reader = await check.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Schema migration created a foreign-key violation in table '{reader.GetString(0)}'.");
                }
            }

            transaction.Commit();
        }
        finally
        {
            using SqliteCommand enable = connection.CreateCommand();
            enable.CommandText = "PRAGMA foreign_keys = ON;";
            await enable.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
