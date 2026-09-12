using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
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
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowBrowserFormatPreparationApplicationTests
{
    private static readonly byte[] OriginalBytes =
        Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
    private static readonly byte[] ProxyBytes = [0xff, 0xd8, 0xff, 0xd9];

    [Fact]
    public async Task Browser_unsupported_verified_original_prepares_and_uses_proxy_for_playback()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(
                databasePath,
                directory,
                OriginalBytes,
                mediaType: "image/heic",
                fileName: "photo.heic");
            await CreateProxyAsync(databasePath, directory, revisionId, ProxyBytes);

            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));
            await using PhotoIdentityApiTestFactory factory = CreateApiFactory(
                databasePath,
                directory,
                platform);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/api/slideshows/original-preparation",
                new SlideshowOriginalPreparationRequest([revisionId.ToString()]));
            await startResponse.EnsureSuccessWithDiagnosticBodyAsync("start browser-unsupported slideshow preparation");
            SlideshowOriginalPreparationResponse started =
                await startResponse.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>()
                ?? throw new InvalidOperationException("Preparation start response was empty.");

            SlideshowOriginalPreparationResponse ready = await WaitForReadyAsync(
                client,
                started.SessionId);
            Assert.Equal("ready", ready.State);
            Assert.Equal(1, ready.Ready);
            Assert.Equal(1, ready.Total);
            Assert.Equal(0, platform.HydrationRequests);

            using HttpResponseMessage playback = await client.GetAsync(
                $"/api/slideshows/original-preparation/{started.SessionId}/photos/{revisionId}/original");
            await playback.EnsureSuccessWithDiagnosticBodyAsync(
                "prepared playback for browser-unsupported original");
            Assert.Equal("image/jpeg", playback.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ProxyBytes, await playback.Content.ReadAsByteArrayAsync());

            using HttpResponseMessage endResponse = await client.DeleteAsync(
                $"/api/slideshows/original-preparation/{started.SessionId}");
            await endResponse.EnsureSuccessWithDiagnosticBodyAsync("end slideshow preparation");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SlideshowOriginalPreparationResponse> WaitForReadyAsync(
        HttpClient client,
        string sessionId)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            SlideshowOriginalPreparationResponse status =
                await client.GetFromJsonAsync<SlideshowOriginalPreparationResponse>(
                    $"/api/slideshows/original-preparation/{sessionId}",
                    timeout.Token)
                ?? throw new InvalidOperationException("Preparation status response was empty.");

            if (status.State == "ready")
            {
                return status;
            }

            if (status.State is "failed" or "cancelled")
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected preparation to become ready but observed '{status.State}': {status.Message}");
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task<AssetRevisionId> CreateRevisionAsync(
        string databasePath,
        string sourceRoot,
        byte[] content,
        string mediaType,
        string fileName)
    {
        string relativeDirectory = Path.Combine(sourceRoot, "family");
        Directory.CreateDirectory(relativeDirectory);
        await File.WriteAllBytesAsync(Path.Combine(relativeDirectory, fileName), content);

        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
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
        DateTimeOffset now = new(2026, 9, 13, 0, 1, 0, TimeSpan.Zero);
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

    private static PhotoIdentityApiTestFactory CreateApiFactory(
        string databasePath,
        string root,
        FakeFilesOnDemandPlatform platform) =>
        new(
            databasePath,
            builder =>
            {
                builder.UseSetting("PhotoIdentity:ReviewProxyRoot", Path.Combine(root, "proxies"));
                builder.UseSetting("PhotoIdentity:ReviewProxyProfileId", "test-preview");
                builder.UseSetting("PhotoIdentity:ReviewProxyMaximumLongEdge", "1600");
                builder.UseSetting("PhotoIdentity:ReviewProxyJpegQuality", "78");

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
            });

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
            State = new OneDriveFilesOnDemandState(AssetAvailability.Local, true, false);
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
