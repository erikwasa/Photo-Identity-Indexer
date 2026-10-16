using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoPosterPublicationTests
{
    [Fact]
    public async Task Source_exclusion_waits_for_poster_file_and_SQL_publication()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string name = "photoidentity_video_publish_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            NpgsqlConnectionStringBuilder settings = new(admin)
                { Database = name, Pooling = false };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            Guid sourceId = Guid.NewGuid(), assetId = Guid.NewGuid(), revisionId = Guid.NewGuid();
            DateTimeOffset now = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'publication-test-root', @now);
                    INSERT INTO assets(id, source_id, source_key, media_kind, created_at_utc)
                    VALUES (@asset, @source, 'test/clip.mov', 'video', @now);
                    INSERT INTO asset_revisions(id, asset_id, content_sha256,
                        size_bytes, observed_at_utc, media_type, media_kind)
                    VALUES (@revision, @asset, repeat('a',64), 5000, @now, 'video/quicktime', 'video');
                    """;
                seed.Parameters.AddWithValue("source", sourceId);
                seed.Parameters.AddWithValue("asset", assetId);
                seed.Parameters.AddWithValue("revision", revisionId);
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }
            var evidence = new PostgresVideoEvidenceRepository(database);
            var rev = AssetRevisionId.From(revisionId);
            await evidence.SaveInspectionAsync(new VideoRevisionInspection(
                rev, "supported", null, "mov,mp4", "h264", "High", "aac", "yuv420p", "bt709",
                1280, 720, 0, 6m, "30/1", null, null, null, null, null, null,
                now, new string('c', 64), "wi0151-inspection-v2"));
            var owner = SourceId.From(sourceId);
            var exclusions = new PostgresSourceCopyExclusionRepository(database);
            string tool = new('d', 64);
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task<bool> publishing = evidence.PublishCurrentPosterAsync(
                owner, "test/clip.mov", rev, new Sha256Digest(new string('a', 64)),
                "wi0151-sdr-still-v1", tool,
                async token =>
                {
                    entered.TrySetResult(true);
                    await resume.Task.WaitAsync(token);
                    return new VideoStillPoster(
                        rev, "wi0151-sdr-still-v1", tool, new string('e', 64),
                        $"video-posters/wi0151-sdr-still-v1/{revisionId:D}/{tool}.jpg",
                        1024, 640, 360, DateTimeOffset.UtcNow);
                });

            Task? excluding = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                excluding = exclusions.ExcludeAsync(owner, "test/clip.mov", now.AddMinutes(2));
                await Task.Delay(150);
                Assert.False(excluding.IsCompleted,
                    "Exclusion must wait until source-locked poster publication finishes.");
            }
            finally { resume.TrySetResult(true); }

            Assert.True(await publishing.WaitAsync(TimeSpan.FromSeconds(15)));
            await excluding!.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Null(await evidence.GetPosterAsync(rev, "wi0151-sdr-still-v1", tool));
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand verify = connection.CreateCommand())
            {
                verify.CommandText = """
                    SELECT count(*) FROM video_still_posters WHERE asset_revision_id = @revision;
                    """;
                verify.Parameters.AddWithValue("revision", revisionId);
                Assert.Equal(1L, Convert.ToInt64(await verify.ExecuteScalarAsync()));
            }

            bool invokedAfterExclusion = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                evidence.PublishCurrentPosterAsync(
                    owner, "test/clip.mov", rev, new Sha256Digest(new string('a', 64)),
                    "wi0151-sdr-still-v1", new string('f', 64), _ =>
                    {
                        invokedAfterExclusion = true;
                        throw new InvalidOperationException("Excluded producer was invoked.");
                    }));
            Assert.False(invokedAfterExclusion);
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
