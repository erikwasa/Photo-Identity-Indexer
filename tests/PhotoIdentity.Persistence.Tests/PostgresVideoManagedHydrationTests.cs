using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

/// <summary>
/// Only metadata and durable managed-ownership receipts in a disposable
/// PostgreSQL database. Never touches live OneDrive originals.
/// </summary>
public sealed class PostgresVideoManagedHydrationTests
{
    [Fact]
    public async Task Shared_managed_hydration_records_identify_video_and_photo_for_safe_eviction()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_wi0207_own_" +
            Guid.NewGuid().ToString("N");
        await using NpgsqlConnection server = new(
            new NpgsqlConnectionStringBuilder(admin)
            { Pooling = false }.ConnectionString);
        await server.OpenAsync();
        await using (NpgsqlCommand create = server.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var settings = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = name, Pooling = false
            };
            await using PostgresCatalogueDatabase database =
                new(settings.ConnectionString);
            await database.InitializeAsync();

            Guid source = Guid.NewGuid(),
                videoAsset = Guid.NewGuid(), photoAsset = Guid.NewGuid(),
                videoRevision = Guid.NewGuid(), photoRevision = Guid.NewGuid();
            await using (NpgsqlConnection connection =
                await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','C:\synthetic-only',now());
                    INSERT INTO assets(id,source_id,source_key,
                        created_at_utc,media_kind)
                    VALUES (@videoAsset,@source,'mock/video.mov',now(),'video'),
                           (@photoAsset,@source,'mock/image.jpg',now(),'photo');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@videoRevision,@videoAsset,repeat('a',64),
                        4096,now(),'video/quicktime','video'),
                           (@photoRevision,@photoAsset,repeat('b',64),
                        512,now(),'image/jpeg','photo');
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("videoAsset", videoAsset);
                seed.Parameters.AddWithValue("photoAsset", photoAsset);
                seed.Parameters.AddWithValue("videoRevision", videoRevision);
                seed.Parameters.AddWithValue("photoRevision", photoRevision);
                await seed.ExecuteNonQueryAsync();
            }

            var managed = new PostgresArchiveHydrationRepository(database);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await managed.ClaimAsync(AssetRevisionId.From(videoRevision), now);
            await managed.ClaimAsync(AssetRevisionId.From(photoRevision), now);
            var leases = await managed.GetActiveLeasesAsync();
            Assert.Equal(2, leases.Count);
            var video = Assert.Single(leases,
                x => x.AssetRevisionId == AssetRevisionId.From(videoRevision));
            var photo = Assert.Single(leases,
                x => x.AssetRevisionId == AssetRevisionId.From(photoRevision));
            Assert.True(video.IsVideo);
            Assert.False(photo.IsVideo);
            Assert.Equal(4096, video.SizeBytes);
            Assert.Equal(512, photo.SizeBytes);

            // This is durable evidence across repository instances.
            var restarted = new PostgresArchiveHydrationRepository(database);
            var secondRead = await restarted.GetActiveLeasesAsync();
            Assert.True(Assert.Single(secondRead,
                x => x.AssetRevisionId == AssetRevisionId.From(videoRevision))
                .IsVideo);

            Assert.Empty(await managed.GetPendingVideoReleasesAsync(16));
            await managed.TouchAsync(AssetRevisionId.From(videoRevision),
                now.AddMinutes(1));
            await managed.MarkReleaseRequestedAsync(
                AssetRevisionId.From(videoRevision), now.AddMinutes(2));
            ArchiveManagedHydrationLeaseState pending =
                Assert.Single(await restarted.GetPendingVideoReleasesAsync(16));
            Assert.Equal(AssetRevisionId.From(videoRevision),
                pending.AssetRevisionId);
            Assert.True(pending.IsVideo);
            Assert.True(pending.IsReleaseRequested);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                managed.GetPendingVideoReleasesAsync(17));
            Assert.True((await restarted.GetAsync(
                AssetRevisionId.From(videoRevision)))!.IsReleaseRequested);
            Assert.False(await managed.TryMarkVideoReleaseConfirmedAsync(
                AssetRevisionId.From(photoRevision), now.AddMinutes(3)));
            Assert.True(await managed.TryMarkVideoReleaseConfirmedAsync(
                AssetRevisionId.From(videoRevision), now.AddMinutes(3)));
            Assert.False(await managed.TryMarkVideoReleaseConfirmedAsync(
                AssetRevisionId.From(videoRevision), now.AddMinutes(4)));
            Assert.Empty(await restarted.GetPendingVideoReleasesAsync(16));
            Assert.DoesNotContain(await restarted.GetActiveLeasesAsync(),
                x => x.AssetRevisionId == AssetRevisionId.From(videoRevision));
            Assert.Single(await restarted.GetActiveLeasesAsync());
        }
        finally
        {
            // DROP DATABASE must be a standalone PostgreSQL statement;
            // combining it with SELECT in one command can cause an
            // implicit transaction block and make cleanup fail.
            await using (NpgsqlCommand terminate = server.CreateCommand())
            {
                terminate.CommandText = $"""
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = '{name}' AND pid <> pg_backend_pid();
                    """;
                await terminate.ExecuteNonQueryAsync();
            }
            await using (NpgsqlCommand drop = server.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE \"{name}\";";
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
