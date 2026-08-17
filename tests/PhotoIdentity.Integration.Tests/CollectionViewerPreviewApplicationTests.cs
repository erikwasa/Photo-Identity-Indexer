using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionViewerPreviewApplicationTests
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAK0lEQVQIHXXBAQEAAADBMDqI+LBiSWBziT6X6HOJPpfoc4k+l+hziT6X6BtqPwoJ+/i3LAAAAABJRU5ErkJggg==");
    private static readonly byte[] ProxyBytes = [0xff, 0xd8, 0xff, 0xd9];

    [Fact]
    public async Task Local_verified_original_without_proxy_is_served_directly_without_hydration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, PngBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using ViewerApiFactory factory = new(databasePath, directory, platform);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/viewer-preview");

            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(PngBytes, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal("local", await ReadAvailabilityAsync(databasePath, revisionId));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Local_verified_original_without_proxy_profile_is_served_directly_without_hydration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, PngBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using ViewerApiFactory factory = new(
                databasePath,
                directory,
                platform,
                configureProxyProfile: false);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/viewer-preview");

            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(PngBytes, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal("local", await ReadAvailabilityAsync(databasePath, revisionId));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Local_verified_original_wins_when_durable_proxy_also_exists()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, PngBytes);
            await CreateProxyAsync(databasePath, directory, revisionId, ProxyBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using ViewerApiFactory factory = new(databasePath, directory, platform);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/viewer-preview");

            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(PngBytes, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Local_verified_browser_unsupported_original_uses_proxy_without_hydration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(
                databasePath,
                directory,
                PngBytes,
                mediaType: "image/heic",
                fileName: "photo.heic");
            await CreateProxyAsync(databasePath, directory, revisionId, ProxyBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using ViewerApiFactory factory = new(databasePath, directory, platform);
            using HttpClient client = factory.CreateClient();

            using (HttpResponseMessage status = await client.GetAsync(
                       $"/api/collections/photos/{revisionId}/original/status"))
            {
                status.EnsureSuccessStatusCode();
                using JsonDocument payload = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
                Assert.Equal("ready", payload.RootElement.GetProperty("state").GetString());
                Assert.False(payload.RootElement.GetProperty("canView").GetBoolean());
            }

            using (HttpResponseMessage preview = await client.GetAsync(
                       $"/api/collections/photos/{revisionId}/viewer-preview"))
            {
                preview.EnsureSuccessStatusCode();
                Assert.Equal("image/jpeg", preview.Content.Headers.ContentType?.MediaType);
                Assert.Equal(ProxyBytes, await preview.Content.ReadAsByteArrayAsync());
            }

            using (HttpResponseMessage proxy = await client.GetAsync(
                       $"/api/collections/photos/{revisionId}/viewer-proxy"))
            {
                proxy.EnsureSuccessStatusCode();
                Assert.Equal("image/jpeg", proxy.Content.Headers.ContentType?.MediaType);
                Assert.Equal(ProxyBytes, await proxy.Content.ReadAsByteArrayAsync());
            }

            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal("local", await ReadAvailabilityAsync(databasePath, revisionId));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Online_only_original_with_proxy_uses_proxy_without_hydration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, PngBytes);
            await CreateProxyAsync(databasePath, directory, revisionId, ProxyBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true));

            await using ViewerApiFactory factory = new(databasePath, directory, platform);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/viewer-preview");

            response.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ProxyBytes, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal("online-only", await ReadAvailabilityAsync(databasePath, revisionId));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Online_only_original_without_proxy_profile_is_not_hydrated_by_viewer_get()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, PngBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true));

            await using ViewerApiFactory factory = new(
                databasePath,
                directory,
                platform,
                configureProxyProfile: false);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/viewer-preview");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal("online-only", await ReadAvailabilityAsync(databasePath, revisionId));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Explicit_original_status_reconciles_archive_availability_transitions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory, PngBytes);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true));

            await using ViewerApiFactory factory = new(databasePath, directory, platform);
            using HttpClient client = factory.CreateClient();

            using (HttpResponseMessage initial = await client.GetAsync(
                       $"/api/collections/photos/{revisionId}/original/status"))
            {
                initial.EnsureSuccessStatusCode();
            }
            Assert.Equal("online-only", await ReadAvailabilityAsync(databasePath, revisionId));

            using (HttpResponseMessage hydrate = await client.PostAsync(
                       $"/api/collections/photos/{revisionId}/original/hydrate",
                       content: null))
            {
                hydrate.EnsureSuccessStatusCode();
            }
            Assert.Equal("downloading", await ReadAvailabilityAsync(databasePath, revisionId));

            platform.State = new OneDriveFilesOnDemandState(AssetAvailability.Local, true, false);
            using (HttpResponseMessage ready = await client.GetAsync(
                       $"/api/collections/photos/{revisionId}/original/status"))
            {
                ready.EnsureSuccessStatusCode();
            }
            Assert.Equal("local", await ReadAvailabilityAsync(databasePath, revisionId));

            using (HttpResponseMessage release = await client.PostAsync(
                       $"/api/collections/photos/{revisionId}/original/release",
                       content: null))
            {
                release.EnsureSuccessStatusCode();
            }
            Assert.Equal("online-only", await ReadAvailabilityAsync(databasePath, revisionId));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> CreateRevisionAsync(
        string databasePath,
        string sourceRoot,
        byte[] content,
        string mediaType = "image/png",
        string fileName = "photo.png")
    {
        string relativeDirectory = Path.Combine(sourceRoot, "family");
        Directory.CreateDirectory(relativeDirectory);
        await File.WriteAllBytesAsync(Path.Combine(relativeDirectory, fileName), content);

        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, $"family/{fileName}", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()),
            content.LongLength,
            now,
            mediaType,
            10,
            8);
        return (await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(
            source,
            asset,
            revision)).Id;
    }

    private static async Task CreateProxyAsync(
        string databasePath,
        string root,
        AssetRevisionId revisionId,
        byte[] content)
    {
        SqliteCatalogueDatabase database = new(databasePath);
        SqliteArchiveReviewProxyRepository repository = new(database);
        ReviewProxyProfile profile = new("test-preview", maximumLongEdge: 1600, jpegQuality: 78);
        DateTimeOffset now = new(2026, 8, 10, 20, 1, 0, TimeSpan.Zero);
        await repository.RegisterProfileAsync(profile, now);

        string relativePath = $"test-preview/{revisionId}.jpg";
        string fullPath = Path.Combine(
            root,
            "proxies",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content);

        await repository.RecordCompletionAsync(new ArchiveReviewProxyRecord(
            revisionId,
            profile.Id,
            content.LongLength,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()),
            1,
            1,
            now,
            relativePath));
    }

    private static async Task<string?> ReadAvailabilityAsync(
        string databasePath,
        AssetRevisionId revisionId)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT availability.availability
            FROM archive_asset_availability AS availability
            INNER JOIN asset_revisions AS revision
                ON revision.asset_id = availability.asset_id
            WHERE revision.id = $revision_id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        return await command.ExecuteScalarAsync() as string;
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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ViewerApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _root;
        private readonly FakeFilesOnDemandPlatform _platform;
        private readonly bool _configureProxyProfile;

        public ViewerApiFactory(
            string databasePath,
            string root,
            FakeFilesOnDemandPlatform platform,
            bool configureProxyProfile = true)
        {
            _databasePath = databasePath;
            _root = root;
            _platform = platform;
            _configureProxyProfile = configureProxyProfile;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:ReviewProxyRoot", Path.Combine(_root, "proxies"));
            if (_configureProxyProfile)
            {
                builder.UseSetting("PhotoIdentity:ReviewProxyProfileId", "test-preview");
                builder.UseSetting("PhotoIdentity:ReviewProxyMaximumLongEdge", "1600");
                builder.UseSetting("PhotoIdentity:ReviewProxyJpegQuality", "78");
            }

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
            State = new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);
            return Task.CompletedTask;
        }
    }
}
