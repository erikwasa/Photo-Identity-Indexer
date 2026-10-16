using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoAssetRepositoryTests
{
    [Fact]
    public async Task Verified_local_video_registration_keeps_source_identity_revisions_and_exclusions()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string databaseName = "photoidentity_video_revisions_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder options = new(admin)
            {
                Database = databaseName,
                Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(options.ConnectionString);
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
            string root = Path.Combine(Path.GetTempPath(), "video-archive", Guid.NewGuid().ToString("N"));
            PostgresCatalogueSourceRepository sources = new(database);
            ArchiveCatalogueSource source = await sources.GetOrCreateLocalFolderSourceAsync(root, now);
            ArchiveCatalogueSource again = await sources.GetOrCreateLocalFolderSourceAsync(root, now.AddMinutes(1));
            Assert.Equal(source.SourceId, again.SourceId);

            PostgresVideoAssetRepository videos = new(database);
            SourceAsset local = new(new SourceAssetReference(source.SourceId, "holiday/clip.mov"),
                "holiday/clip.mov", "video/quicktime", 5000, now, AssetAvailability.Local);
            VerifiedLocalVideo first = new(source, local,
                new Sha256Digest(new string('a', 64)), now);
            RegisteredVideoRevision initial = await videos.RegisterVerifiedAsync(first);
            Assert.True(initial.NewAsset);
            Assert.True(initial.NewRevision);
            Assert.Equal(initial.RevisionId,
                await videos.GetLatestVisibleRevisionAsync(source.SourceId, "holiday/clip.mov"));

            RegisteredVideoRevision duplicate = await videos.RegisterVerifiedAsync(
                first with { VerifiedAtUtc = now.AddSeconds(1) });
            Assert.False(duplicate.NewAsset);
            Assert.False(duplicate.NewRevision);
            Assert.Equal(initial.AssetId, duplicate.AssetId);
            Assert.Equal(initial.RevisionId, duplicate.RevisionId);

            RegisteredVideoRevision edited = await videos.RegisterVerifiedAsync(
                first with
                {
                    ContentHash = new Sha256Digest(new string('b', 64)),
                    VerifiedAtUtc = now.AddMinutes(5)
                });
            Assert.False(edited.NewAsset);
            Assert.True(edited.NewRevision);
            Assert.Equal(initial.AssetId, edited.AssetId);
            Assert.NotEqual(initial.RevisionId, edited.RevisionId);
            Assert.Equal(edited.RevisionId,
                await videos.GetLatestVisibleRevisionAsync(source.SourceId, "holiday/clip.mov"));

            // The normal photo table contract is never changed by a video write.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand inspect = connection.CreateCommand())
            {
                inspect.CommandText = """
                    SELECT
                      (SELECT count(*) FROM assets WHERE media_kind='video'),
                      (SELECT count(*) FROM asset_revisions WHERE media_kind='video'),
                      (SELECT count(*) FROM assets WHERE media_kind='photo'),
                      (SELECT count(*) FROM asset_revisions WHERE media_kind='photo');
                    """;
                await using NpgsqlDataReader reader = await inspect.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
            }

            PostgresSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(source.SourceId, "holiday/clip.mov", now.AddMinutes(6));
            Assert.Null(await videos.GetLatestVisibleRevisionAsync(
                source.SourceId, "holiday/clip.mov"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                videos.RegisterVerifiedAsync(first with { VerifiedAtUtc = now.AddMinutes(7) }));

            await exclusions.ExcludeAsync(source.SourceId, "holiday/private.mov", now.AddMinutes(6));
            SourceAsset privateFile = new(new SourceAssetReference(source.SourceId, "holiday/private.mov"),
                "holiday/private.mov", "video/quicktime", 5000, now, AssetAvailability.Local);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                videos.RegisterVerifiedAsync(new VerifiedLocalVideo(
                    source, privateFile, new Sha256Digest(new string('c', 64)), now.AddMinutes(7))));

            // Existing photo-controlled keys cannot be silently reclassified as video.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand photo = connection.CreateCommand())
            {
                photo.CommandText = """
                    INSERT INTO assets (id, source_id, source_key, media_kind, created_at_utc)
                    VALUES (@id, @source, 'holiday/photo-owned.mov', 'photo', @now);
                    """;
                photo.Parameters.AddWithValue("id", Guid.NewGuid());
                photo.Parameters.AddWithValue("source", source.SourceId.Value);
                photo.Parameters.AddWithValue("now", now);
                await photo.ExecuteNonQueryAsync();
            }
            SourceAsset collision = new(new SourceAssetReference(source.SourceId, "holiday/photo-owned.mov"),
                "holiday/photo-owned.mov", "video/quicktime", 1000, now, AssetAvailability.Local);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                videos.RegisterVerifiedAsync(new VerifiedLocalVideo(
                    source, collision, new Sha256Digest(new string('d', 64)), now)));

            SourceAsset placeholder = new(new SourceAssetReference(source.SourceId, "online.mp4"),
                "online.mp4", "video/mp4", 2500, now, AssetAvailability.OnlineOnly);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                videos.RegisterVerifiedAsync(new VerifiedLocalVideo(
                    source, placeholder, new Sha256Digest(new string('e', 64)), now)));
            Assert.Null(await videos.GetLatestVisibleRevisionAsync(source.SourceId, "online.mp4"));
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
