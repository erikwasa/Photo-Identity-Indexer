using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

public sealed class CollectionOriginalAccessApplicationTests
{
    [Fact]
    public async Task Online_only_original_requires_explicit_hydration_is_hash_verified_and_only_managed_content_can_release()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            byte[] content = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, content, content);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true));

            await using CollectionOriginalApiFactory factory = new(databasePath, platform);
            using HttpClient client = factory.CreateClient();

            CollectionOriginalAccessResponse initial = await GetStatusAsync(client, revisionId);
            Assert.Equal("online-only", initial.State);
            Assert.False(initial.ManagedHydration);
            Assert.True(initial.CanRequestHydration);

            using HttpResponseMessage implicitOriginal = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/original");
            Assert.Equal(HttpStatusCode.NotFound, implicitOriginal.StatusCode);
            Assert.Equal(0, platform.HydrationRequests);

            using HttpResponseMessage hydrateResponse = await client.PostAsync(
                $"/api/collections/photos/{revisionId}/original/hydrate",
                content: null);
            hydrateResponse.EnsureSuccessStatusCode();
            CollectionOriginalAccessResponse hydrating =
                await hydrateResponse.Content.ReadFromJsonAsync<CollectionOriginalAccessResponse>()
                ?? throw new InvalidOperationException("Hydration response was empty.");
            Assert.Equal("downloading", hydrating.State);
            Assert.True(hydrating.ManagedHydration);
            Assert.Equal(1, platform.HydrationRequests);

            platform.State = new OneDriveFilesOnDemandState(AssetAvailability.Local, true, false);
            CollectionOriginalAccessResponse ready = await GetStatusAsync(client, revisionId);
            Assert.Equal("ready", ready.State);
            Assert.True(ready.ManagedHydration);
            Assert.True(ready.CanView);
            Assert.True(ready.CanRelease);

            using HttpResponseMessage original = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/original");
            original.EnsureSuccessStatusCode();
            Assert.Equal(content, await original.Content.ReadAsByteArrayAsync());

            using HttpResponseMessage releaseResponse = await client.PostAsync(
                $"/api/collections/photos/{revisionId}/original/release",
                content: null);
            releaseResponse.EnsureSuccessStatusCode();
            CollectionOriginalAccessResponse released =
                await releaseResponse.Content.ReadFromJsonAsync<CollectionOriginalAccessResponse>()
                ?? throw new InvalidOperationException("Release response was empty.");
            Assert.Equal("online-only", released.State);
            Assert.False(released.ManagedHydration);
            Assert.Equal(1, platform.ReleaseRequests);

            using HttpResponseMessage secondRelease = await client.PostAsync(
                $"/api/collections/photos/{revisionId}/original/release",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, secondRelease.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Preexisting_local_pinned_original_is_never_claimed_or_released()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            byte[] content = Enumerable.Range(0, 1024).Select(index => (byte)(index % 239)).ToArray();
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, content, content);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, true, false));

            await using CollectionOriginalApiFactory factory = new(databasePath, platform);
            using HttpClient client = factory.CreateClient();

            CollectionOriginalAccessResponse initial = await GetStatusAsync(client, revisionId);
            Assert.Equal("ready", initial.State);
            Assert.False(initial.ManagedHydration);
            Assert.True(initial.IsPinned);
            Assert.False(initial.CanRelease);

            using HttpResponseMessage hydrateResponse = await client.PostAsync(
                $"/api/collections/photos/{revisionId}/original/hydrate",
                content: null);
            hydrateResponse.EnsureSuccessStatusCode();
            CollectionOriginalAccessResponse afterHydrate =
                await hydrateResponse.Content.ReadFromJsonAsync<CollectionOriginalAccessResponse>()
                ?? throw new InvalidOperationException("Hydration response was empty.");
            Assert.False(afterHydrate.ManagedHydration);
            Assert.Equal(0, platform.HydrationRequests);

            using HttpResponseMessage releaseResponse = await client.PostAsync(
                $"/api/collections/photos/{revisionId}/original/release",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, releaseResponse.StatusCode);
            Assert.Equal(0, platform.ReleaseRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Local_original_with_wrong_hash_is_never_served()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            byte[] actual = Enumerable.Range(0, 2048).Select(index => (byte)(index % 233)).ToArray();
            byte[] catalogued = actual.ToArray();
            catalogued[100] ^= 0xff;
            AssetRevisionId revisionId = await CreateRevisionAsync(
                databasePath,
                directory,
                actual,
                catalogued);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using CollectionOriginalApiFactory factory = new(databasePath, platform);
            using HttpClient client = factory.CreateClient();

            CollectionOriginalAccessResponse status = await GetStatusAsync(client, revisionId);
            Assert.Equal("hash-mismatch", status.State);
            Assert.False(status.CanView);

            using HttpResponseMessage original = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/original");
            Assert.Equal(HttpStatusCode.NotFound, original.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CollectionOriginalAccessResponse> GetStatusAsync(
        HttpClient client,
        AssetRevisionId revisionId) =>
        await client.GetFromJsonAsync<CollectionOriginalAccessResponse>(
            $"/api/collections/photos/{revisionId}/original/status")
        ?? throw new InvalidOperationException("Original status response was empty.");

    private static async Task<AssetRevisionId> CreateRevisionAsync(
        string databasePath,
        string sourceRoot,
        byte[] actualContent,
        byte[] cataloguedContent)
    {
        string relativeDirectory = Path.Combine(sourceRoot, "family");
        Directory.CreateDirectory(relativeDirectory);
        await File.WriteAllBytesAsync(Path.Combine(relativeDirectory, "photo.jpg"), actualContent);

        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 8, 9, 0, 30, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "family/photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(cataloguedContent)).ToLowerInvariant()),
            actualContent.LongLength,
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

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CollectionOriginalApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly FakeFilesOnDemandPlatform _platform;

        public CollectionOriginalApiFactory(
            string databasePath,
            FakeFilesOnDemandPlatform platform)
        {
            _databasePath = databasePath;
            _platform = platform;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOneDriveFilesOnDemandPlatform>();
                services.AddSingleton<IOneDriveFilesOnDemandPlatform>(_platform);
                services.RemoveAll<ArchiveHydrationPolicyConfiguration>();
                services.AddSingleton(new ArchiveHydrationPolicyConfiguration(
                    MinimumFreeSpaceReserveBytes: 0,
                    MaximumManagedHydrationBytes: 1024L * 1024L * 1024L,
                    MaximumConcurrentOperations: 2));
                services.RemoveAll<IArchiveStorageProbe>();
                services.AddSingleton<IArchiveStorageProbe>(new FixedStorageProbe(10L * 1024L * 1024L * 1024L));
            });
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
        public int HydrationRequests { get; private set; }
        public int ReleaseRequests { get; private set; }

        public OneDriveFilesOnDemandState GetState(string path) => State;

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HydrationRequests++;
            State = new OneDriveFilesOnDemandState(AssetAvailability.Downloading, true, false);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseRequests++;
            State = new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);
            return Task.CompletedTask;
        }
    }
}
