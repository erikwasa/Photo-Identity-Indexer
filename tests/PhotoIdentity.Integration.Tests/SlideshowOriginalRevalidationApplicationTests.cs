using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowOriginalRevalidationApplicationTests
{
    [Fact]
    public async Task Prepared_original_revalidation_downgrades_after_original_becomes_online_only()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            byte[] content = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, content);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using RevalidationApiFactory factory = new(databasePath, platform);
            using HttpClient client = factory.CreateClient();
            SlideshowOriginalPreparationRequest request = new([revisionId.ToString()]);

            SlideshowOriginalRevalidationResponse ready = await RevalidateAsync(client, request);
            Assert.True(ready.Reusable);
            Assert.Equal(1, ready.Ready);
            Assert.Equal(1, ready.Total);

            platform.State = new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);

            SlideshowOriginalRevalidationResponse released = await RevalidateAsync(client, request);
            Assert.False(released.Reusable);
            Assert.Equal(0, released.Ready);
            Assert.Equal(1, released.Total);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<SlideshowOriginalRevalidationResponse> RevalidateAsync(
        HttpClient client,
        SlideshowOriginalPreparationRequest request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/slideshows/original-preparation/revalidate",
            request);
        await response.EnsureSuccessWithDiagnosticBodyAsync("prepared-original revalidation");
        return await response.Content.ReadFromJsonAsync<SlideshowOriginalRevalidationResponse>()
            ?? throw new InvalidOperationException("Prepared-original revalidation response was empty.");
    }

    private static async Task<AssetRevisionId> CreateRevisionAsync(
        string databasePath,
        string sourceRoot,
        byte[] content)
    {
        string relativeDirectory = Path.Combine(sourceRoot, "family");
        Directory.CreateDirectory(relativeDirectory);
        await File.WriteAllBytesAsync(Path.Combine(relativeDirectory, "photo.jpg"), content);

        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 9, 12, 20, 30, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "family/photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()),
            content.LongLength,
            now,
            "image/jpeg",
            100,
            100);
        return (await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(
            source,
            asset,
            revision)).Id;
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RevalidationApiFactory : PhotoIdentityApiTestFactory
    {
        public RevalidationApiFactory(
            string databasePath,
            FakeFilesOnDemandPlatform platform)
            : base(
                databasePath,
                builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IOneDriveFilesOnDemandPlatform>();
                        services.AddSingleton<IOneDriveFilesOnDemandPlatform>(platform);
                        services.RemoveAll<ArchiveHydrationPolicyConfiguration>();
                        services.AddSingleton(new ArchiveHydrationPolicyConfiguration(
                            MinimumFreeSpaceReserveBytes: 0,
                            MaximumManagedHydrationBytes: 1024L * 1024L * 1024L,
                            MaximumConcurrentOperations: 2));
                        services.RemoveAll<IArchiveStorageProbe>();
                        services.AddSingleton<IArchiveStorageProbe>(
                            new FixedStorageProbe(10L * 1024L * 1024L * 1024L));
                    });
                })
        {
        }
    }

    private sealed class FixedStorageProbe(long availableBytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        public FakeFilesOnDemandPlatform(OneDriveFilesOnDemandState state)
        {
            State = state;
        }

        public OneDriveFilesOnDemandState State { get; set; }

        public OneDriveFilesOnDemandState GetState(string path) => State;

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = new OneDriveFilesOnDemandState(AssetAvailability.Downloading, true, false);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);
            return Task.CompletedTask;
        }
    }
}
