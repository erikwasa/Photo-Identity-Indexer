using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoPosterPurgeRepositoryTests
{
    [Fact]
    public async Task Exclusion_manifest_captures_owned_video_JPEG_and_orphan_before_cascade()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string databaseName = "photoidentity_video_purge_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection adminConnection =
            new(new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync();
        }
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-purge-" + Guid.NewGuid().ToString("N"));
        try
        {
            string archive = Path.Combine(root, "archive");
            string derivatives = Path.Combine(root, "derivatives");
            string profile = Path.Combine(derivatives, "video-posters", "wi0151-sdr-still-v1");
            Directory.CreateDirectory(archive);
            Directory.CreateDirectory(profile);

            Guid sourceId = Guid.NewGuid(), videoId = Guid.NewGuid(),
                revisionId = Guid.NewGuid(), otherId = Guid.NewGuid(),
                otherRevision = Guid.NewGuid();
            string tool = new('d', 64);
            string relative = $"video-posters/wi0151-sdr-still-v1/{revisionId:D}/{tool}.jpg";
            string poster = Path.Combine(derivatives,
                relative.Replace('/', Path.DirectorySeparatorChar));
            string orphan = Path.Combine(Path.GetDirectoryName(poster)!,
                new string('a', 32) + ".tmp.jpg");
            string unrelated = Path.Combine(profile, otherRevision.ToString("D"),
                new string('e', 64) + ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(poster)!);
            Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
            File.WriteAllBytes(poster, [1, 2, 3]);
            File.WriteAllBytes(orphan, [4, 5]);
            File.WriteAllBytes(unrelated, [6, 7, 8]);

            NpgsqlConnectionStringBuilder settings = new(admin)
            {
                Database = databaseName, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', @root, @now);
                    INSERT INTO assets(id, source_id, source_key, media_kind, created_at_utc)
                    VALUES (@video, @source, 'private/clip.mov', 'video', @now),
                           (@other, @source, 'public/clip.mov', 'video', @now);
                    INSERT INTO asset_revisions(id, asset_id, content_sha256,
                        size_bytes, observed_at_utc, media_type, media_kind)
                    VALUES (@revision, @video, repeat('a',64), 8000, @now, 'video/quicktime', 'video'),
                           (@other_revision, @other, repeat('b',64), 8000, @now, 'video/quicktime', 'video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status, inspected_at_utc, tool_sha256, inspection_profile_id)
                    VALUES (@revision, 'supported', @now, repeat('c',64), 'wi0151-metadata-v2');
                    INSERT INTO video_still_posters(asset_revision_id, poster_profile_id,
                        tool_sha256, poster_sha256, poster_relative_path,
                        encoded_bytes, width, height, generated_at_utc)
                    VALUES (@revision, 'wi0151-sdr-still-v1',
                        @tool, repeat('e',64), @relative, 3, 100, 100, @now);
                    """;
                seed.Parameters.AddWithValue("source", sourceId);
                seed.Parameters.AddWithValue("video", videoId);
                seed.Parameters.AddWithValue("revision", revisionId);
                seed.Parameters.AddWithValue("other", otherId);
                seed.Parameters.AddWithValue("other_revision", otherRevision);
                seed.Parameters.AddWithValue("root", archive);
                seed.Parameters.AddWithValue("tool", tool);
                seed.Parameters.AddWithValue("relative", relative);
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            SourceId owner = SourceId.From(sourceId);
            PostgresSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(owner, "private/clip.mov", now.AddMinutes(1));
            PostgresSourceCopyPurgeRepository purges = new(database);
            SourceCopyPurgeRoots noVideoRoot = new(
                Path.Combine(root, "analysis"), Path.Combine(root, "review"),
                Path.Combine(root, "detector"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purges.PrepareManifestAsync(owner, "private/clip.mov",
                    noVideoRoot, now.AddMinutes(2)));

            SourceCopyPurgeRoots roots = noVideoRoot with { VideoPosterRoot = derivatives };
            SourceCopyPurgeManifest manifest = await purges.PrepareManifestAsync(
                owner, "private/clip.mov", roots, now.AddMinutes(2));
            Assert.Equal(2, manifest.Artifacts.Count);
            Assert.Contains(manifest.Artifacts, item => item.AbsolutePath == poster && !item.IsDirectory);
            Assert.Contains(manifest.Artifacts, item => item.AbsolutePath == orphan && !item.IsDirectory);
            Assert.DoesNotContain(manifest.Artifacts, item => item.AbsolutePath == unrelated);

            SourceCopyPurgeManifest resumed = await purges.PrepareManifestAsync(
                owner, "private/clip.mov", roots, now.AddMinutes(3));
            Assert.Equal(manifest.Artifacts, resumed.Artifacts);

            // Mimic the order enforced by SourceCopyPurgeService: delete files,
            // then cascade catalogue state, then clear the durable manifest.
            foreach (SourceCopyPurgeArtifact artifact in resumed.Artifacts)
                File.Delete(artifact.AbsolutePath);
            await purges.PurgeCatalogueAsync(owner, "private/clip.mov");
            Assert.True(File.Exists(unrelated));
            Assert.False(File.Exists(poster));
            Assert.False(File.Exists(orphan));
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand count = connection.CreateCommand())
            {
                count.CommandText = """
                    SELECT (SELECT count(*) FROM assets WHERE media_kind='video'),
                           (SELECT count(*) FROM asset_revisions WHERE media_kind='video'),
                           (SELECT count(*) FROM video_still_posters);
                    """;
                await using NpgsqlDataReader reader = await count.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
            }
            await purges.ClearManifestAsync(owner, "private/clip.mov");
            Assert.NotNull(await exclusions.GetAsync(owner, "private/clip.mov"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
