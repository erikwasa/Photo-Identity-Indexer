using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoEvidenceRepositoryTests
{
    [Fact]
    public async Task Evidence_is_revision_bound_idempotent_and_hidden_by_source_exclusion()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_video_evidence_" + Guid.NewGuid().ToString("N");
        string quoted = "\"" + name + "\"";
        await using NpgsqlConnection adminConnection = new(new NpgsqlConnectionStringBuilder(admin)
        {
            Pooling = false,
        }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quoted};";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder settings = new(admin)
            {
                Database = name,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            Guid sourceId = Guid.NewGuid();
            Guid assetId = Guid.NewGuid(), revisionId = Guid.NewGuid();
            Guid photoId = Guid.NewGuid(), photoRevisionId = Guid.NewGuid();
            DateTimeOffset now = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'test-video-evidence-source', @now);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc, media_kind)
                    VALUES (@video, @source, 'sample.mov', @now, 'video'),
                           (@photo, @source, 'still.jpg', @now, 'photo');
                    INSERT INTO asset_revisions (id, asset_id, content_sha256,
                        size_bytes, observed_at_utc, media_type, media_kind)
                    VALUES (@video_revision, @video, repeat('a',64),
                            8000, @now, 'video/quicktime', 'video'),
                           (@photo_revision, @photo, repeat('b',64),
                            800, @now, 'image/jpeg', 'photo');
                    """;
                seed.Parameters.AddWithValue("source", sourceId);
                seed.Parameters.AddWithValue("video", assetId);
                seed.Parameters.AddWithValue("photo", photoId);
                seed.Parameters.AddWithValue("video_revision", revisionId);
                seed.Parameters.AddWithValue("photo_revision", photoRevisionId);
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            // Source-copy tombstones have their own lazy schema in this repository.
            PostgresSourceCopyExclusionRepository exclusions = new(database);
            Assert.Null(await exclusions.GetAsync(SourceId.From(sourceId), "sample.mov"));

            PostgresVideoEvidenceRepository repository = new(database);
            VideoRevisionInspection inspection = new(
                AssetRevisionId.From(revisionId), "supported", null,
                "mov,mp4", "h264", "High", "aac", "yuv420p", "bt709",
                1920, 1080, -90, 41.250000m, "30/1",
                null, null, null, null, null, null,
                now, new string('c', 64), "wi0151-inspection-v1");
            await repository.SaveInspectionAsync(inspection);
            Assert.Equal(inspection, await repository.GetInspectionAsync(inspection.RevisionId));

            await repository.SaveInspectionAsync(inspection with
            {
                InspectedAtUtc = now.AddMinutes(1)
            });
            Assert.Equal(inspection, await repository.GetInspectionAsync(inspection.RevisionId));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveInspectionAsync(inspection with { VideoCodec = "hevc" }));

            VideoStillPoster poster = new(
                inspection.RevisionId, "bt709-poster-v1", new string('d', 64),
                new string('e', 64), "video-posters/bt709-poster-v1/sample.jpg",
                4096, 1280, 720, now);
            await repository.SavePosterAsync(poster);
            await repository.SavePosterAsync(poster with { GeneratedAtUtc = now.AddMinutes(1) });
            Assert.Equal(poster, await repository.GetPosterAsync(
                poster.RevisionId, poster.PosterProfileId, poster.ToolSha256));

            // Cleanup is source-scoped and cannot retire a current video poster.
            PostgresVideoPosterCleanupRepository cleanup = new(database);
            IReadOnlyList<VideoPosterCleanupRow> current =
                await cleanup.SnapshotAsync(SourceId.From(sourceId));
            VideoPosterCleanupRow row = Assert.Single(current, value => value.RelativePath == poster.PosterRelativePath);
            Assert.True(row.IsCurrent);
            Assert.False(await cleanup.RetireStalePosterAsync(SourceId.From(sourceId), row));
            Assert.False(await cleanup.MayRemoveUnreferencedAsync(
                SourceId.From(sourceId), poster.RevisionId, poster.PosterProfileId, poster.ToolSha256));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cleanup.RetireStalePosterAsync(SourceId.New(), row));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SavePosterAsync(poster with { PosterSha256 = new string('f', 64) }));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.SavePosterAsync(poster with { PosterRelativePath = "video-posters/../private.jpg" }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveInspectionAsync(inspection with
                {
                    RevisionId = AssetRevisionId.From(photoRevisionId)
                }));

            // Durable source-copy exclusion prevents further writes and suppresses reads.
            await exclusions.ExcludeAsync(SourceId.From(sourceId), "sample.mov", now.AddMinutes(2));
            Assert.Null(await repository.GetInspectionAsync(inspection.RevisionId));
            Assert.Null(await repository.GetPosterAsync(
                poster.RevisionId, poster.PosterProfileId, poster.ToolSha256));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveInspectionAsync(inspection));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SavePosterAsync(poster));

            // An excluded poster is no longer visible and may be retired with
            // an exact row match. No other revision metadata or photo rows change.
            IReadOnlyList<VideoPosterCleanupRow> excluded =
                await cleanup.SnapshotAsync(SourceId.From(sourceId));
            row = Assert.Single(excluded, value => value.RelativePath == poster.PosterRelativePath);
            Assert.False(row.IsCurrent);
            Assert.True(await cleanup.RetireStalePosterAsync(SourceId.From(sourceId), row));
            Assert.False(await cleanup.RetireStalePosterAsync(SourceId.From(sourceId), row));
            Assert.True(await cleanup.MayRemoveUnreferencedAsync(
                SourceId.From(sourceId), poster.RevisionId, poster.PosterProfileId, poster.ToolSha256));

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand inspect = connection.CreateCommand())
            {
                inspect.CommandText = """
                    SELECT
                      (SELECT count(*) FROM video_revision_metadata),
                      (SELECT count(*) FROM video_still_posters),
                      (SELECT count(*) FROM asset_revisions WHERE media_kind='photo'),
                      (SELECT count(*) FROM assets WHERE media_kind='photo');
                    """;
                await using NpgsqlDataReader row = await inspect.ExecuteReaderAsync();
                Assert.True(await row.ReadAsync());
                Assert.Equal(1L, row.GetInt64(0));
                Assert.Equal(0L, row.GetInt64(1));
                Assert.Equal(1L, row.GetInt64(2));
                Assert.Equal(1L, row.GetInt64(3));
            }
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

}
