using Npgsql;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// PostgreSQL source-release fencing and recovery with a fake OneDrive
/// provider and a disposable synthetic source. Never touches production DB,
/// OneDrive, real video bytes or a user-owned source path.
/// </summary>
public sealed class VideoFirstInspectionReleasePersistenceTests
{
    [Fact]
    public async Task Only_owned_unpinned_provisional_video_can_release_and_reconcile()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string databaseName = "photoidentity_wi0207_src_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), "wi0207-synthetic-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string filename = Path.Combine(root, "synthetic.mov");
        // Synthetic placeholder surrogate only. Bytes are not a real video.
        await File.WriteAllBytesAsync(filename, new byte[128]);

        await using NpgsqlConnection server = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await server.OpenAsync();
        await using (NpgsqlCommand cmd = server.CreateCommand())
        {
            cmd.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var settings = new NpgsqlConnectionStringBuilder(admin)
            { Database = databaseName, Pooling = false };
            await using PostgresCatalogueDatabase db = new(settings.ConnectionString);
            await db.InitializeAsync();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var source = await new PostgresCatalogueSourceRepository(db)
                .GetOrCreateLocalFolderSourceAsync(root, now);
            FileInfo info = new(filename);
            var asset = new SourceAsset(
                new SourceAssetReference(source.SourceId, "synthetic.mov"),
                "synthetic.mov", "video/quicktime",
                info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                AssetAvailability.OnlineOnly);
            ArchiveSourceObservationSnapshot observed =
                await new PostgresVideoProvisionalRepository(db)
                    .ObserveAsync(source, asset, now);
            var receipts = new PostgresArchiveSourceHydrationRepository(db);
            var fake = new FakeVideoCloudPlatform();
            var service = new VideoManagedHydrationCoordinator(
                db, new PostgresArchiveHydrationRepository(db),
                receipts, null!, fake, TimeProvider.System);

            // Never release a pre-existing user-owned local original.
            Assert.False(await service.ReleaseUninspectedAsync(observed, filename));
            Assert.Equal(0, fake.ReleaseRequests);

            await receipts.ClaimAsync(observed.AssetId, now);
            fake.IsPinned = true;
            Assert.False(await service.ReleaseUninspectedAsync(observed, filename));
            Assert.Equal(0, fake.ReleaseRequests);
            Assert.False((await receipts.GetAsync(observed.AssetId))!.IsReleaseRequested);

            // Unpinned Photo Identity-owned source gets a source-locked
            // durable release receipt before one Cloud Files +u request.
            fake.IsPinned = false;
            Assert.True(await service.ReleaseUninspectedAsync(observed, filename));
            Assert.Equal(1, fake.ReleaseRequests);
            Assert.True((await receipts.GetAsync(observed.AssetId))!.IsReleaseRequested);

            // The new revision cannot be registered while release is pending.
            var verified = new PhotoIdentity.Core.Catalogue.VerifiedLocalVideo(
                source,
                new SourceAsset(asset.Reference, asset.RelativePath,
                    asset.MediaType, asset.SizeBytes, asset.LastWriteTimeUtc,
                    AssetAvailability.Local),
                new PhotoIdentity.Core.Recognition.Sha256Digest(new string('a', 64)),
                now.AddSeconds(1));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PostgresVideoAssetRepository(db)
                    .RegisterVerifiedAsync(verified));

            await service.ReconcileUninspectedAsync();
            Assert.False((await receipts.GetAsync(observed.AssetId))!.IsActive);
            Assert.Empty(await new PostgresVideoProvisionalRepository(db)
                .GetActiveAsync());
        }
        finally
        {
            await using (NpgsqlCommand stop = server.CreateCommand())
            {
                stop.CommandText = $"""
                    SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                    WHERE datname='{databaseName}' AND pid <> pg_backend_pid();
                    """;
                await stop.ExecuteNonQueryAsync();
            }
            await using (NpgsqlCommand drop = server.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE \"{databaseName}\";";
                await drop.ExecuteNonQueryAsync();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeVideoCloudPlatform : IOneDriveFilesOnDemandPlatform
    {
        public bool IsPinned { get; set; }
        public int ReleaseRequests { get; private set; }
        private bool _onlineOnly;

        public OneDriveFilesOnDemandState GetState(string path) =>
            new(_onlineOnly ? AssetAvailability.OnlineOnly : AssetAvailability.Local,
                IsPinned, _onlineOnly);
        public Task RequestHydrationAsync(
            string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RequestOnlineOnlyAsync(
            string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RequestUnpinnedVideoReleaseAsync(
            string path, CancellationToken cancellationToken = default)
        {
            Assert.False(IsPinned);
            ReleaseRequests++;
            _onlineOnly = true;
            return Task.CompletedTask;
        }
    }
}
