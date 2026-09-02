using Xunit;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresCatalogueDatabaseTests
{
    [Fact]
    public async Task TryInitializeAsync_ReportsUnavailable_ForUnreachableServer()
    {
        await using PostgresCatalogueDatabase database = new(
            "Host=127.0.0.1;Port=1;Database=photoidentity;Username=test;Password=test;Pooling=false;Timeout=1");

        PostgresInitializationResult result = await database.TryInitializeAsync();

        Assert.Equal("unavailable", result.Health.Status);
        Assert.True(result.Health.Configured);
        Assert.Null(result.Health.SchemaVersion);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InitializeAsync_IsVersionedAndIdempotent_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_test_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);

        NpgsqlConnectionStringBuilder adminBuilder =
            new(adminConnectionString)
            {
                Pooling = false,
            };

        await using NpgsqlConnection adminConnection =
            new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        await using (NpgsqlCommand createDatabase =
                     adminConnection.CreateCommand())
        {
            createDatabase.CommandText =
                $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder =
                new(adminConnectionString)
                {
                    Database = databaseName,
                    Pooling = false,
                };

            await using PostgresCatalogueDatabase database =
                new(testBuilder.ConnectionString);

            PostgresInitializationResult first =
                await database.TryInitializeAsync();
            PostgresInitializationResult second =
                await database.TryInitializeAsync();

            Assert.Null(first.Error);
            Assert.Equal("ready", first.Health.Status);
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                first.Health.SchemaVersion);

            Assert.Null(second.Error);
            Assert.Equal("ready", second.Health.Status);
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                second.Health.SchemaVersion);

            await using NpgsqlConnection verificationConnection =
                new(testBuilder.ConnectionString);
            await verificationConnection.OpenAsync();

            await using NpgsqlCommand readMigration =
                verificationConnection.CreateCommand();
            readMigration.CommandText =
                """
                SELECT COUNT(*)
                FROM photo_identity_schema_migrations
                WHERE version = @version;
                """;
            readMigration.Parameters.AddWithValue(
                "version",
                PostgresCatalogueDatabase.CurrentSchemaVersion);

            object? count = await readMigration.ExecuteScalarAsync();
            Assert.Equal(1L, Convert.ToInt64(count));

            await using (NpgsqlCommand readFoundationalTables =
                         verificationConnection.CreateCommand())
            {
                readFoundationalTables.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN (
                          'sources',
                          'assets',
                          'asset_revisions',
                          'face_occurrences',
                          'face_observations',
                          'face_crops',
                          'embeddings',
                          'processing_runs',
                          'processing_jobs');
                    """;

                object? tableCount =
                    await readFoundationalTables.ExecuteScalarAsync();
                Assert.Equal(9L, Convert.ToInt64(tableCount));
            }

            await using (NpgsqlCommand readArchiveAnalysisTables =
                         verificationConnection.CreateCommand())
            {
                readArchiveAnalysisTables.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN (
                          'archive_analysis_profiles',
                          'archive_analysis_runs',
                          'asset_revision_analysis');
                    """;

                object? archiveAnalysisTableCount =
                    await readArchiveAnalysisTables.ExecuteScalarAsync();
                Assert.Equal(3L, Convert.ToInt64(archiveAnalysisTableCount));
            }

            await using (NpgsqlCommand readColumnTypes =
                         verificationConnection.CreateCommand())
            {
                readColumnTypes.CommandText =
                    """
                    SELECT
                        (SELECT data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND table_name = 'sources'
                           AND column_name = 'id'),
                        (SELECT data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND table_name = 'processing_runs'
                           AND column_name = 'configuration_json'),
                        (SELECT data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND table_name = 'embeddings'
                           AND column_name = 'vector_blob');
                    """;

                await using NpgsqlDataReader typeReader =
                    await readColumnTypes.ExecuteReaderAsync();
                Assert.True(await typeReader.ReadAsync());
                Assert.Equal("uuid", typeReader.GetString(0));
                Assert.Equal("jsonb", typeReader.GetString(1));
                Assert.Equal("bytea", typeReader.GetString(2));
            }

            Guid sourceId = Guid.NewGuid();
            Guid assetId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();
            await using (NpgsqlCommand seedRevision =
                         verificationConnection.CreateCommand())
            {
                seedRevision.CommandText =
                    """
                    INSERT INTO sources (
                        id, kind, root_locator, created_at_utc)
                    VALUES (
                        @source_id, 'test', 'test-root', @now);

                    INSERT INTO assets (
                        id, source_id, source_key, created_at_utc)
                    VALUES (
                        @asset_id, @source_id, 'photo.jpg', @now);

                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc)
                    VALUES (
                        @revision_id,
                        @asset_id,
                        @content_sha256,
                        1,
                        @now);
                    """;
                seedRevision.Parameters.AddWithValue(
                    "source_id",
                    sourceId);
                seedRevision.Parameters.AddWithValue(
                    "asset_id",
                    assetId);
                seedRevision.Parameters.AddWithValue(
                    "revision_id",
                    revisionId);
                seedRevision.Parameters.AddWithValue(
                    "content_sha256",
                    new string('a', 64));
                seedRevision.Parameters.AddWithValue(
                    "now",
                    DateTimeOffset.UtcNow);
                await seedRevision.ExecuteNonQueryAsync();
            }

            Guid processingRunId = Guid.NewGuid();
            await using (NpgsqlCommand seedProcessingRun =
                         verificationConnection.CreateCommand())
            {
                seedProcessingRun.CommandText =
                    """
                    INSERT INTO processing_runs (
                        id,
                        status,
                        configuration_json,
                        started_at_utc)
                    VALUES (
                        @processing_run_id,
                        'pending',
                        '{}'::jsonb,
                        @now);
                    """;
                seedProcessingRun.Parameters.AddWithValue(
                    "processing_run_id",
                    processingRunId);
                seedProcessingRun.Parameters.AddWithValue(
                    "now",
                    DateTimeOffset.UtcNow);
                await seedProcessingRun.ExecuteNonQueryAsync();
            }

            AnalysisProfileDefinition profile = new(
                new Sha256Digest(new string('b', 64)),
                new ModelId("test-detector"),
                new Sha256Digest(new string('c', 64)),
                new ModelId("test-embedder"),
                new Sha256Digest(new string('d', 64)),
                new AlignmentProtocolId("test-alignment"));
            Sha256Digest profileHash = profile.ComputeHash();
            ProcessingRunId runId = ProcessingRunId.From(processingRunId);
            AssetRevisionId revision = AssetRevisionId.From(revisionId);
            IArchiveAnalysisStateRepository archiveAnalysis =
                new PostgresArchiveAnalysisStateRepository(database);

            await archiveAnalysis.RegisterRunAsync(
                runId,
                profile,
                DateTimeOffset.UtcNow);
            Assert.Equal(
                profileHash,
                await archiveAnalysis.GetRunProfileHashAsync(runId));
            Assert.False(
                await archiveAnalysis.IsCompletedAsync(revision, profileHash));

            await archiveAnalysis.RecordCompletionAsync(
                runId,
                revision,
                profileHash,
                DateTimeOffset.UtcNow);

            Assert.True(
                await archiveAnalysis.IsCompletedAsync(revision, profileHash));

            await using (NpgsqlCommand mutateRevision =
                         verificationConnection.CreateCommand())
            {
                mutateRevision.CommandText =
                    """
                    UPDATE asset_revisions
                    SET content_sha256 = @replacement_content_sha256
                    WHERE id = @revision_id;
                    """;
                mutateRevision.Parameters.AddWithValue(
                    "revision_id",
                    revisionId);
                mutateRevision.Parameters.AddWithValue(
                    "replacement_content_sha256",
                    new string('b', 64));

                PostgresException immutable =
                    await Assert.ThrowsAsync<PostgresException>(
                        () => mutateRevision.ExecuteNonQueryAsync());
                Assert.Contains(
                    "asset revision identity is immutable",
                    immutable.MessageText,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            await using (NpgsqlCommand terminateConnections =
                         adminConnection.CreateCommand())
            {
                terminateConnections.CommandText =
                    """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid();
                    """;
                terminateConnections.Parameters.AddWithValue(
                    "database_name",
                    databaseName);
                await terminateConnections.ExecuteNonQueryAsync();
            }

            await using NpgsqlCommand dropDatabase =
                adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName};";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        const char quote = (char)34;
        string quoteString = quote.ToString();
        string escaped = identifier.Replace(
            quoteString,
            quoteString + quoteString,
            StringComparison.Ordinal);
        return quoteString + escaped + quoteString;
    }
}
