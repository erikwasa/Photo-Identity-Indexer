using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteSourceCopyPurgeRepository : ISourceCopyPurgeRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly Lazy<Task> _schema;

    public SqliteSourceCopyPurgeRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _schema = new Lazy<Task>(() => EnsureSchemaCoreAsync(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<SourceCopyPurgeManifest> PrepareManifestAsync(
        SourceId sourceId,
        string sourceKey,
        SourceCopyPurgeRoots roots,
        DateTimeOffset preparedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        using (SqliteCommand exclusion = connection.CreateCommand())
        {
            exclusion.Transaction = transaction;
            exclusion.CommandText = """
                SELECT 1
                FROM source_copy_exclusions
                WHERE source_id = $source_id AND source_key = $source_key;
                """;
            exclusion.Parameters.AddWithValue("$source_id", sourceId.ToString());
            exclusion.Parameters.AddWithValue("$source_key", key);
            if (await exclusion.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new InvalidOperationException("Source-copy exclusion does not exist.");
            }
        }

        SourceCopyPurgeManifest? existing = await ReadManifestAsync(
            connection,
            transaction,
            sourceId,
            key,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        HashSet<SourceCopyPurgeArtifact> artifacts = [];
        await AddReviewProxyArtifactsAsync(
            connection,
            transaction,
            sourceId,
            key,
            roots.NormalizedReviewProxyRoot,
            artifacts,
            cancellationToken);
        await AddAnalysisArtifactsAsync(
            connection,
            transaction,
            sourceId,
            key,
            roots.NormalizedArchiveAnalysisRoot,
            artifacts,
            cancellationToken);
        await AddDetectorArtifactsAsync(
            connection,
            transaction,
            sourceId,
            key,
            roots.NormalizedDetectorEvaluationRoot,
            artifacts,
            cancellationToken);

        DateTimeOffset prepared = preparedAtUtc.ToUniversalTime();
        using (SqliteCommand header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText = """
                INSERT INTO source_copy_purge_manifests (source_id, source_key, prepared_at_utc)
                VALUES ($source_id, $source_key, $prepared_at_utc);
                """;
            header.Parameters.AddWithValue("$source_id", sourceId.ToString());
            header.Parameters.AddWithValue("$source_key", key);
            header.Parameters.AddWithValue("$prepared_at_utc", Format(prepared));
            await header.ExecuteNonQueryAsync(cancellationToken);
        }

        SourceCopyPurgeArtifact[] ordered = artifacts
            .OrderBy(value => value.AbsolutePath, StringComparer.Ordinal)
            .ToArray();
        foreach (SourceCopyPurgeArtifact artifact in ordered)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO source_copy_purge_manifest_artifacts (
                    source_id, source_key, absolute_path, is_directory)
                VALUES ($source_id, $source_key, $absolute_path, $is_directory);
                """;
            insert.Parameters.AddWithValue("$source_id", sourceId.ToString());
            insert.Parameters.AddWithValue("$source_key", key);
            insert.Parameters.AddWithValue("$absolute_path", artifact.AbsolutePath);
            insert.Parameters.AddWithValue("$is_directory", artifact.IsDirectory ? 1 : 0);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SourceCopyPurgeManifest(sourceId, key, prepared, ordered);
    }

    public async Task PurgeCatalogueAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        using (SqliteCommand guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = """
                SELECT 1
                FROM source_copy_exclusions
                WHERE source_id = $source_id AND source_key = $source_key;
                """;
            guard.Parameters.AddWithValue("$source_id", sourceId.ToString());
            guard.Parameters.AddWithValue("$source_key", key);
            if (await guard.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new InvalidOperationException("Source-copy exclusion does not exist.");
            }
        }

        if (await TableExistsAsync(connection, transaction, "detector_reconciliation_plans", cancellationToken))
        {
            await ExecuteForLocatorAsync(
                connection,
                transaction,
                """
                DELETE FROM detector_reconciliation_plans
                WHERE asset_revision_id IN (
                    SELECT revision.id
                    FROM asset_revisions AS revision
                    INNER JOIN assets AS asset ON asset.id = revision.asset_id
                    WHERE asset.source_id = $source_id AND asset.source_key = $source_key);
                """,
                sourceId,
                key,
                cancellationToken);
        }

        if (await TableExistsAsync(connection, transaction, "review_actions", cancellationToken))
        {
            if (await TableExistsAsync(connection, transaction, "identity_suggestion_review_actions", cancellationToken))
            {
                await ExecuteForLocatorAsync(
                    connection,
                    transaction,
                    """
                    DELETE FROM identity_suggestion_review_actions
                    WHERE review_action_id IN (
                        SELECT action.id
                        FROM review_actions AS action
                        INNER JOIN face_occurrences AS face ON face.id = action.face_occurrence_id
                        INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                        INNER JOIN assets AS asset ON asset.id = revision.asset_id
                        WHERE asset.source_id = $source_id AND asset.source_key = $source_key)
                       OR review_action_id IN (
                        SELECT undo_action.id
                        FROM review_actions AS undo_action
                        WHERE undo_action.action_kind = 'undo'
                          AND undo_action.reverses_action_id IN (
                            SELECT action.id
                            FROM review_actions AS action
                            INNER JOIN face_occurrences AS face ON face.id = action.face_occurrence_id
                            INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                            INNER JOIN assets AS asset ON asset.id = revision.asset_id
                            WHERE asset.source_id = $source_id AND asset.source_key = $source_key));
                    """,
                    sourceId,
                    key,
                    cancellationToken);
            }

            await ExecuteForLocatorAsync(
                connection,
                transaction,
                """
                DELETE FROM review_actions
                WHERE action_kind = 'undo'
                  AND (
                    face_occurrence_id IN (
                        SELECT face.id
                        FROM face_occurrences AS face
                        INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                        INNER JOIN assets AS asset ON asset.id = revision.asset_id
                        WHERE asset.source_id = $source_id AND asset.source_key = $source_key)
                    OR reverses_action_id IN (
                        SELECT action.id
                        FROM review_actions AS action
                        INNER JOIN face_occurrences AS face ON face.id = action.face_occurrence_id
                        INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                        INNER JOIN assets AS asset ON asset.id = revision.asset_id
                        WHERE asset.source_id = $source_id AND asset.source_key = $source_key));
                """,
                sourceId,
                key,
                cancellationToken);

            await ExecuteForLocatorAsync(
                connection,
                transaction,
                """
                DELETE FROM review_actions
                WHERE face_occurrence_id IN (
                    SELECT face.id
                    FROM face_occurrences AS face
                    INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                    INNER JOIN assets AS asset ON asset.id = revision.asset_id
                    WHERE asset.source_id = $source_id AND asset.source_key = $source_key);
                """,
                sourceId,
                key,
                cancellationToken);
        }

        await ExecuteForLocatorAsync(
            connection,
            transaction,
            """
            DELETE FROM assets
            WHERE source_id = $source_id AND source_key = $source_key;
            """,
            sourceId,
            key,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearManifestAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM source_copy_purge_manifests
            WHERE source_id = $source_id AND source_key = $source_key;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$source_key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SourceCopyPurgeManifest?> ReadManifestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        string? preparedAt;
        using (SqliteCommand header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText = """
                SELECT prepared_at_utc
                FROM source_copy_purge_manifests
                WHERE source_id = $source_id AND source_key = $source_key;
                """;
            header.Parameters.AddWithValue("$source_id", sourceId.ToString());
            header.Parameters.AddWithValue("$source_key", sourceKey);
            preparedAt = (string?)await header.ExecuteScalarAsync(cancellationToken);
        }

        if (preparedAt is null)
        {
            return null;
        }

        List<SourceCopyPurgeArtifact> artifacts = [];
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT absolute_path, is_directory
            FROM source_copy_purge_manifest_artifacts
            WHERE source_id = $source_id AND source_key = $source_key
            ORDER BY absolute_path;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$source_key", sourceKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            artifacts.Add(new SourceCopyPurgeArtifact(reader.GetString(0), reader.GetInt64(1) != 0));
        }

        return new SourceCopyPurgeManifest(sourceId, sourceKey, Parse(preparedAt), artifacts);
    }

    private static async Task AddReviewProxyArtifactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        string sourceKey,
        string? reviewProxyRoot,
        HashSet<SourceCopyPurgeArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        List<string> relativePaths = [];
        if (await TableExistsAsync(connection, transaction, "asset_revision_review_proxies", cancellationToken))
        {
            relativePaths.AddRange(await QueryPathsAsync(
                connection,
                transaction,
                """
                SELECT proxy.relative_path
                FROM asset_revision_review_proxies AS proxy
                INNER JOIN asset_revisions AS revision ON revision.id = proxy.asset_revision_id
                INNER JOIN assets AS asset ON asset.id = revision.asset_id
                WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
                """,
                sourceId,
                sourceKey,
                cancellationToken));
        }

        if (await TableExistsAsync(connection, transaction, "face_review_derivatives", cancellationToken))
        {
            relativePaths.AddRange(await QueryPathsAsync(
                connection,
                transaction,
                """
                SELECT derivative.relative_path
                FROM face_review_derivatives AS derivative
                INNER JOIN face_occurrences AS face ON face.id = derivative.face_occurrence_id
                INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                INNER JOIN assets AS asset ON asset.id = revision.asset_id
                WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
                """,
                sourceId,
                sourceKey,
                cancellationToken));
        }

        if (relativePaths.Count == 0)
        {
            return;
        }
        if (reviewProxyRoot is null)
        {
            throw new InvalidOperationException("Review-proxy storage is not configured for purge cleanup.");
        }

        foreach (string relativePath in relativePaths)
        {
            artifacts.Add(new SourceCopyPurgeArtifact(ResolveUnderRoot(reviewProxyRoot, relativePath), false));
        }
    }

    private static async Task AddAnalysisArtifactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        string sourceKey,
        string fallbackRoot,
        HashSet<SourceCopyPurgeArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> runRoots = [];
        HashSet<(Guid RunId, Guid RevisionId)> directories = [];

        if (await TableExistsAsync(connection, transaction, "asset_revision_analysis", cancellationToken))
        {
            using SqliteCommand runs = connection.CreateCommand();
            runs.Transaction = transaction;
            runs.CommandText = """
                SELECT analysis.processing_run_id, revision.id, run.configuration_json
                FROM asset_revision_analysis AS analysis
                INNER JOIN asset_revisions AS revision ON revision.id = analysis.asset_revision_id
                INNER JOIN assets AS asset ON asset.id = revision.asset_id
                INNER JOIN processing_runs AS run ON run.id = analysis.processing_run_id
                WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
                """;
            runs.Parameters.AddWithValue("$source_id", sourceId.ToString());
            runs.Parameters.AddWithValue("$source_key", sourceKey);
            await using SqliteDataReader reader = await runs.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid runId = Guid.Parse(reader.GetString(0));
                Guid revisionId = Guid.Parse(reader.GetString(1));
                string root = ReadOutputRoot(reader.GetString(2), fallbackRoot);
                runRoots[runId] = root;
                directories.Add((runId, revisionId));
            }
        }

        if (await TableExistsAsync(connection, transaction, "archive_analysis_runs", cancellationToken) &&
            await TableExistsAsync(connection, transaction, "processing_jobs", cancellationToken))
        {
            using SqliteCommand jobs = connection.CreateCommand();
            jobs.Transaction = transaction;
            jobs.CommandText = """
                SELECT job.processing_run_id, revision.id, run.configuration_json
                FROM processing_jobs AS job
                INNER JOIN asset_revisions AS revision ON revision.id = job.asset_revision_id
                INNER JOIN assets AS asset ON asset.id = revision.asset_id
                INNER JOIN archive_analysis_runs AS archive_run
                    ON archive_run.processing_run_id = job.processing_run_id
                INNER JOIN processing_runs AS run ON run.id = job.processing_run_id
                WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
                """;
            jobs.Parameters.AddWithValue("$source_id", sourceId.ToString());
            jobs.Parameters.AddWithValue("$source_key", sourceKey);
            await using SqliteDataReader reader = await jobs.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid runId = Guid.Parse(reader.GetString(0));
                Guid revisionId = Guid.Parse(reader.GetString(1));
                string root = ReadOutputRoot(reader.GetString(2), fallbackRoot);
                runRoots[runId] = root;
                directories.Add((runId, revisionId));
            }
        }

        foreach ((Guid runId, Guid revisionId) in directories)
        {
            artifacts.Add(new SourceCopyPurgeArtifact(
                ResolveUnderRoot(runRoots[runId], $"runs/{runId:D}/assets/{revisionId:D}"),
                true));
        }

        if (!await TableExistsAsync(connection, transaction, "face_crops", cancellationToken))
        {
            return;
        }

        List<string> cropPaths = [];
        using (SqliteCommand crops = connection.CreateCommand())
        {
            crops.Transaction = transaction;
            crops.CommandText = """
                SELECT crop.storage_path
                FROM face_crops AS crop
                INNER JOIN face_occurrences AS face ON face.id = crop.face_occurrence_id
                INNER JOIN asset_revisions AS revision ON revision.id = face.asset_revision_id
                INNER JOIN assets AS asset ON asset.id = revision.asset_id
                WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
                """;
            crops.Parameters.AddWithValue("$source_id", sourceId.ToString());
            crops.Parameters.AddWithValue("$source_key", sourceKey);
            await using SqliteDataReader cropReader = await crops.ExecuteReaderAsync(cancellationToken);
            while (await cropReader.ReadAsync(cancellationToken))
            {
                cropPaths.Add(cropReader.GetString(0));
            }
        }

        foreach (string relative in cropPaths)
        {
            string root = fallbackRoot;
            if (TryReadRunId(relative, "runs", out Guid runId))
            {
                if (!runRoots.TryGetValue(runId, out string? configured))
                {
                    configured = await ReadProcessingRunRootAsync(
                        connection,
                        transaction,
                        runId,
                        fallbackRoot,
                        cancellationToken);
                    runRoots[runId] = configured;
                }
                root = configured;
            }
            artifacts.Add(new SourceCopyPurgeArtifact(ResolveUnderRoot(root, relative), false));
        }
    }

    private static async Task AddDetectorArtifactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        string sourceKey,
        string fallbackRoot,
        HashSet<SourceCopyPurgeArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "detector_reconciliation_plans", cancellationToken))
        {
            return;
        }

        using SqliteCommand runs = connection.CreateCommand();
        runs.Transaction = transaction;
        runs.CommandText = """
            SELECT plan.processing_run_id, revision.id, run.configuration_json
            FROM detector_reconciliation_plans AS plan
            INNER JOIN asset_revisions AS revision ON revision.id = plan.asset_revision_id
            INNER JOIN assets AS asset ON asset.id = revision.asset_id
            INNER JOIN processing_runs AS run ON run.id = plan.processing_run_id
            WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
            """;
        runs.Parameters.AddWithValue("$source_id", sourceId.ToString());
        runs.Parameters.AddWithValue("$source_key", sourceKey);
        await using SqliteDataReader reader = await runs.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid runId = Guid.Parse(reader.GetString(0));
            Guid revisionId = Guid.Parse(reader.GetString(1));
            string root = ReadOutputRoot(reader.GetString(2), fallbackRoot);
            artifacts.Add(new SourceCopyPurgeArtifact(
                ResolveUnderRoot(root, $"rollouts/{runId:D}/assets/{revisionId:D}"),
                true));
        }
    }

    private static async Task<string> ReadProcessingRunRootAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        string fallbackRoot,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT configuration_json
            FROM processing_runs
            WHERE id = $run_id;
            """;
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        object? configuration = await command.ExecuteScalarAsync(cancellationToken);
        return configuration is string json
            ? ReadOutputRoot(json, fallbackRoot)
            : Path.GetFullPath(fallbackRoot);
    }

    private static async Task<IReadOnlyList<string>> QueryPathsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        List<string> values = [];
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$source_key", sourceKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }

    private static async Task ExecuteForLocatorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$source_key", sourceKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM sqlite_master
                WHERE type = 'table' AND name = $table_name);
            """;
        command.Parameters.AddWithValue("$table_name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken) =>
        await _schema.Value.WaitAsync(cancellationToken);

    private async Task EnsureSchemaCoreAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS source_copy_purge_manifests (
                source_id TEXT NOT NULL,
                source_key TEXT NOT NULL,
                prepared_at_utc TEXT NOT NULL,
                PRIMARY KEY (source_id, source_key),
                FOREIGN KEY (source_id, source_key)
                    REFERENCES source_copy_exclusions (source_id, source_key) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS source_copy_purge_manifest_artifacts (
                source_id TEXT NOT NULL,
                source_key TEXT NOT NULL,
                absolute_path TEXT NOT NULL,
                is_directory INTEGER NOT NULL CHECK (is_directory IN (0, 1)),
                PRIMARY KEY (source_id, source_key, absolute_path),
                FOREIGN KEY (source_id, source_key)
                    REFERENCES source_copy_purge_manifests (source_id, source_key) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ReadOutputRoot(string configurationJson, string fallbackRoot)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationJson);
            if (document.RootElement.TryGetProperty("outputRoot", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return Path.GetFullPath(value.GetString()!);
            }
        }
        catch (JsonException)
        {
            // The fallback root remains authoritative for legacy configuration payloads.
        }
        return Path.GetFullPath(fallbackRoot);
    }

    private static bool TryReadRunId(string relativePath, string prefix, out Guid runId)
    {
        runId = default;
        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 &&
               string.Equals(segments[0], prefix, StringComparison.Ordinal) &&
               Guid.TryParse(segments[1], out runId);
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string platformRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(platformRelative))
        {
            throw new InvalidOperationException("A purge artifact path must be relative to its configured derivative root.");
        }

        string resolved = Path.GetFullPath(Path.Combine(fullRoot, platformRelative));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("A purge artifact path resolves outside its configured derivative root.");
        }
        return resolved;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}