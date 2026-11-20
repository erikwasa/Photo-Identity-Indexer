using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveCaseAliasPilotCommandTests
{
    [Fact]
    public void Options_require_explicit_and_non_destructive_operator_approval()
    {
        string root = Path.Combine(Path.GetTempPath(), $"photoidentity_pilot_options_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string manifest = Path.Combine(root, "private.csv");
            string metadata = Path.Combine(root, "metadata.json");
            File.WriteAllText(manifest, "header");
            File.WriteAllText(metadata, "{}");

            string[] preview = [
                "--manifest", manifest, "--metadata", metadata,
                "--postgres-connection-env", "PHOTOIDENTITY_PILOT_CLI_TEST",
                "--database", "photoidentity_main"
            ];
            ArchiveCaseAliasPilotOptions parsed = ArchiveCaseAliasPilotOptions.Parse(preview);
            Assert.Equal("preview", parsed.Operation);
            Assert.Equal(5, parsed.MaximumPairs);

            Assert.Throws<ArgumentException>(() =>
                ArchiveCaseAliasPilotOptions.Parse([.. preview, "--max-pairs", "6"]));
            Assert.Throws<ArgumentException>(() =>
                ArchiveCaseAliasPilotOptions.Parse([.. preview, "--operation", "apply"]));
            Assert.Throws<ArgumentException>(() =>
                ArchiveCaseAliasPilotOptions.Parse([.. preview, "--operation", "apply",
                    "--application-stopped", "--acknowledge-reversible-retirement"]));
            Assert.Throws<ArgumentException>(() =>
                ArchiveCaseAliasPilotOptions.Parse([.. preview, "--operation", "restore",
                    "--application-stopped", "--acknowledge-reversible-retirement",
                    "--restore-report", metadata]));
            Assert.Throws<ArgumentException>(() =>
                ArchiveCaseAliasPilotOptions.Parse([.. preview, "--database", "other_database"]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Disposable_preview_apply_and_restore_are_bounded_and_preserve_history()
    {
        string? adminString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
            return;

        string dbName = "photoidentity_alias_cli_" + Guid.NewGuid().ToString("N");
        string quoted = '"' + dbName.Replace("\"", "\"\"") + '"';
        NpgsqlConnectionStringBuilder adminBuilder = new(adminString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quoted};";
            await create.ExecuteNonQueryAsync();
        }

        string temp = Path.Combine(Path.GetTempPath(),
            "photoidentity_alias_cli_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        const string environment = "PHOTOIDENTITY_ARCHIVE_ALIAS_PILOT_CLI_TEST_CONNECTION";
        string? originalEnv = Environment.GetEnvironmentVariable(environment);
        try
        {
            NpgsqlConnectionStringBuilder builder = new(adminString)
            {
                Database = dbName, Pooling = false
            };
            Environment.SetEnvironmentVariable(environment, builder.ConnectionString);
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await database.InitializeAsync();

            Guid source = Guid.NewGuid(), older = Guid.NewGuid(), later = Guid.NewGuid();
            Guid oldRevision = Guid.NewGuid(), laterRevision = Guid.NewGuid();
            Guid face = Guid.NewGuid();
            DateTimeOffset first = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset second = first.AddHours(1);
            string hash = new('a', 64);
            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'test-root', @first);
                    INSERT INTO assets
                        (id, source_id, source_key, media_kind, created_at_utc, last_seen_at_utc)
                    VALUES (@older, @source, 'Family/portrait.jpg', 'photo', @first, @second),
                           (@later, @source, 'family/portrait.jpg', 'photo', @second, @second);
                    INSERT INTO asset_revisions
                        (id, asset_id, media_kind, content_sha256, size_bytes,
                         observed_at_utc, media_type)
                    VALUES (@old_revision, @older, 'photo', @hash, 1000, @first, 'image/jpeg'),
                           (@new_revision, @later, 'photo', @hash, 1000, @second, 'image/jpeg');
                    INSERT INTO archive_source_observations
                        (asset_id, observed_size_bytes, observed_last_write_utc,
                         observed_media_type, observed_at_utc, verification_state,
                         verified_revision_id, verified_size_bytes, verified_last_write_utc,
                         verified_media_type, verified_at_utc)
                    VALUES
                        (@older, 1000, @first, 'image/jpeg', @second, 'verified',
                         @old_revision, 1000, @first, 'image/jpeg', @second),
                        (@later, 1000, @second, 'image/jpeg', @second, 'verified',
                         @new_revision, 1000, @second, 'image/jpeg', @second);
                    INSERT INTO face_occurrences
                        (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@face, @new_revision, 0, @second);
                    INSERT INTO review_actions
                        (face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@face, 'unknown', 'identity-matcher:auto', @second);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("older", older);
                seed.Parameters.AddWithValue("later", later);
                seed.Parameters.AddWithValue("old_revision", oldRevision);
                seed.Parameters.AddWithValue("new_revision", laterRevision);
                seed.Parameters.AddWithValue("face", face);
                seed.Parameters.AddWithValue("hash", hash);
                seed.Parameters.AddWithValue("first", first);
                seed.Parameters.AddWithValue("second", second);
                await seed.ExecuteNonQueryAsync();
            }

            string manifestPath = Path.Combine(temp, "pilot-preview-private.csv");
            string metadataPath = Path.Combine(temp, "pilot-preview-metadata.json");
            string reportPath = Path.Combine(temp, "backup.dump.restore-verification.json");
            string backupPath = Path.Combine(temp, "backup.dump");
            string receiptDir = Path.Combine(temp, "receipts");
            Directory.CreateDirectory(receiptDir);

            string[] headings = [
                "SourceId", "NormalizedSourceKey", "SurvivorAssetId",
                "SurvivorSourceKey", "SurvivorCreatedAtUtc",
                "SurvivorVerifiedRevisionId", "SecondaryAssetId", "SecondarySourceKey",
                "SecondaryCreatedAtUtc", "SecondaryVerifiedRevisionId", "ExpectedSha256",
                "ExpectedSizeBytes", "SecondaryAutomaticReviewActions", "Action",
                "ApprovalRequired", "ApplyEnabled", "DeleteSourceOriginal", "DeleteDatabaseRows"
            ];
            string[] values = [
                source.ToString("D"), "family/portrait.jpg", older.ToString("D"),
                "Family/portrait.jpg", first.ToString("O"), oldRevision.ToString("D"),
                later.ToString("D"), "family/portrait.jpg", second.ToString("O"),
                laterRevision.ToString("D"), hash, "1000", "1",
                "proposed-reversible-retirement", "true", "false", "false", "false"
            ];
            static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            File.WriteAllText(manifestPath,
                string.Join(",", headings.Select(Csv)) + "\r\n" +
                string.Join(",", values.Select(Csv)) + "\r\n",
                new UTF8Encoding(false));
            string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath)))
                .ToLowerInvariant();
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, status = "preview-only",
                expectedDatabaseSchemaVersion = 44, generatedAtUtc = DateTimeOffset.UtcNow,
                pilotPairs = 1, privatePilotManifestSha256 = digest,
                applyEnabled = false, approvalRequired = true,
                sourceFilesToDelete = 0, databaseRowsToDelete = 0
            }));
            // Only a synthetic test receipt. Production operator must use the
            // independently checked verify-postgres-backup-restore.ps1 report.
            File.WriteAllText(backupPath, "synthetic-disposable-backup-marker");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                passed = true, sourceDatabase = dbName, cleanupStatus = "removed",
                cleanupRequired = false, backupPath,
                backupSha256 = new string('b', 64), capturedAtUtc = DateTimeOffset.UtcNow
            }));

            string[] common = [
                "--manifest", manifestPath, "--metadata", metadataPath,
                "--postgres-connection-env", environment, "--database", dbName,
                "--max-pairs", "1"
            ];
            StringWriter output = new();
            Assert.Equal(0, await ArchiveCaseAliasPilotCommandRunner.RunAsync(
                ArchiveCaseAliasPilotOptions.Parse(common), output));
            Assert.Contains("PILOT PREVIEW", output.ToString());

            string[] writeOptions = [
                "--approve-manifest-sha256", digest,
                "--receipt-directory", receiptDir, "--actor", "disposable-test",
                "--application-stopped", "--acknowledge-reversible-retirement"
            ];
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ArchiveCaseAliasPilotCommandRunner.RunAsync(
                    ArchiveCaseAliasPilotOptions.Parse([
                        .. common, "--operation", "apply", "--restore-report", reportPath,
                        "--approve-manifest-sha256", new string('f', 64),
                        "--receipt-directory", receiptDir, "--actor", "disposable-test",
                        "--application-stopped", "--acknowledge-reversible-retirement"
                    ]), new StringWriter()));

            Assert.Equal(0, await ArchiveCaseAliasPilotCommandRunner.RunAsync(
                ArchiveCaseAliasPilotOptions.Parse([
                    .. common, "--operation", "apply", "--restore-report", reportPath,
                    .. writeOptions]), new StringWriter()));

            async Task<long> CountAsync(string sql)
            {
                await using NpgsqlConnection con = new(builder.ConnectionString);
                await con.OpenAsync();
                using NpgsqlCommand query = con.CreateCommand();
                query.CommandText = sql;
                return (long)(await query.ExecuteScalarAsync())!;
            }

            Assert.Equal(1, await CountAsync(
                "SELECT count(*) FROM archive_case_alias_retirements WHERE restored_at_utc IS NULL;"));
            Assert.Equal(1, await CountAsync(
                "SELECT count(*) FROM assets WHERE source_key='family/portrait.jpg' AND deleted_at_utc IS NOT NULL;"));
            Assert.Equal(1, await CountAsync("SELECT count(*) FROM review_actions;"));

            Assert.Equal(0, await ArchiveCaseAliasPilotCommandRunner.RunAsync(
                ArchiveCaseAliasPilotOptions.Parse([
                    .. common, "--operation", "restore", .. writeOptions]), new StringWriter()));

            Assert.Equal(0, await CountAsync(
                "SELECT count(*) FROM archive_case_alias_retirements WHERE restored_at_utc IS NULL;"));
            Assert.Equal(2, await CountAsync("SELECT count(*) FROM assets WHERE deleted_at_utc IS NULL;"));
            Assert.Equal(1, await CountAsync("SELECT count(*) FROM review_actions;"));
            Assert.Equal(2, Directory.GetFiles(receiptDir, "*.jsonl").Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environment, originalEnv);
            Directory.Delete(temp, true);
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
