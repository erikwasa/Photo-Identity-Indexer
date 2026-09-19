using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CreativeVisualRedundancyApplicationTests
{
    [Fact]
    public async Task Proxy_experiment_reports_redundant_group_and_reduces_repeated_selection()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string proxyRoot = Path.Combine(directory, "derivatives");
            Directory.CreateDirectory(proxyRoot);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);

            CatalogueAssetRevision first = await CreateRevisionAsync(catalogue, directory, "first.jpg", 'a');
            CatalogueAssetRevision second = await CreateRevisionAsync(catalogue, directory, "second.jpg", 'b');
            CatalogueAssetRevision third = await CreateRevisionAsync(catalogue, directory, "third.jpg", 'c');

            foreach (CatalogueAssetRevision revision in new[] { first, second, third })
            {
                await tags.AddManualTagAsync(revision.Id, "Creative/Burst", "test");
            }

            DateTime start = new(2026, 9, 18, 10, 0, 0);
            await SetTakenAtAsync(database, first.Id, start);
            await SetTakenAtAsync(database, second.Id, start.AddSeconds(5));
            await SetTakenAtAsync(database, third.Id, start.AddSeconds(10));

            ReviewProxyProfile profile = new("creative-burst-test", 1600, 82);
            SqliteArchiveReviewProxyRepository proxies = new(database);
            DateTimeOffset generatedAt = new(2026, 9, 18, 10, 30, 0, TimeSpan.Zero);
            await proxies.RegisterProfileAsync(profile, generatedAt);
            await SaveProxyAsync(
                proxies,
                proxyRoot,
                profile,
                first.Id,
                CreateGradient(reverse: false),
                generatedAt);
            await SaveProxyAsync(
                proxies,
                proxyRoot,
                profile,
                second.Id,
                CreateGradient(reverse: false),
                generatedAt);
            await SaveProxyAsync(
                proxies,
                proxyRoot,
                profile,
                third.Id,
                CreateGradient(reverse: true),
                generatedAt);

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Burst experiment",
                new SmartCollectionFilter(tags: ["Creative/Burst"]));

            await using CreativeVisualApiFactory factory = new(
                databasePath,
                proxyRoot,
                profile.Id);
            using HttpClient client = factory.CreateClient();

            CreativeVisualRedundancyPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeVisualRedundancyPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-redundancy-preview?targetCount=2")
                ?? throw new InvalidOperationException();

            Assert.Equal(3, preview.DirectAnchorCount);
            Assert.Equal(0, preview.AddedContextCount);
            Assert.Equal(3, preview.FingerprintedCandidateCount);
            Assert.Equal(0, preview.MissingProxyCount);
            Assert.Equal(0, preview.UnreadableProxyCount);
            Assert.False(preview.CandidateSampleTruncated);
            Assert.Equal(3, preview.Policies.Length);

            foreach (CreativeVisualRedundancyPolicyResponse policy in preview.Policies)
            {
                CreativeVisualRedundancyGroupResponse group = Assert.Single(policy.Groups);
                Assert.Equal(2, group.MemberCount);
                Assert.Equal(1, policy.SuppressiblePhotoCount);
                Assert.Equal(1, policy.BaselineRepeatedSelectedFrames);
                Assert.Equal(0, policy.RedundancyAwareRepeatedSelectedFrames);
                Assert.Contains(group.Members, member => member.RevisionId == first.Id.ToString());
                Assert.Contains(group.Members, member => member.RevisionId == second.Id.ToString());
                Assert.DoesNotContain(group.Members, member => member.RevisionId == third.Id.ToString());
            }

            CreativeCollectionPreviewResponse normalPreview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-preview?targetCount=2&momentGapMinutes=30")
                ?? throw new InvalidOperationException();

            Assert.Equal(
                [first.Id.ToString(), third.Id.ToString()],
                normalPreview.SelectedCandidates.Select(candidate => candidate.RevisionId).ToArray());

            using HttpResponseMessage snapshotResponse = await client.PostAsync(
                $"/api/smart-collections/{saved.Id}/creative-slideshow-snapshot?targetCount=3&momentGapMinutes=30",
                content: null);
            snapshotResponse.EnsureSuccessStatusCode();
            SmartCollectionSlideshowSnapshotResponse snapshot =
                await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
                ?? throw new InvalidOperationException();

            Assert.Equal(
                [first.Id.ToString(), second.Id.ToString(), third.Id.ToString()],
                snapshot.Items.Select(item => item.RevisionId).ToArray());
            Assert.Equal(
                PhotoVisualRedundancyPolicy.AcceptedCreativeV1.Version,
                snapshot.VisualRedundancyPolicyVersion);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Items[0].VisualGroupId));
            Assert.Equal(snapshot.Items[0].VisualGroupId, snapshot.Items[1].VisualGroupId);
            Assert.Null(snapshot.Items[2].VisualGroupId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task SaveProxyAsync(
        SqliteArchiveReviewProxyRepository repository,
        string proxyRoot,
        ReviewProxyProfile profile,
        AssetRevisionId revisionId,
        byte[] bytes,
        DateTimeOffset generatedAt)
    {
        string relativePath = Path.Combine("review-proxies", profile.Id, $"{revisionId}.jpg");
        string path = Path.Combine(proxyRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);

        await repository.RecordCompletionAsync(new ArchiveReviewProxyRecord(
            revisionId,
            profile.Id,
            bytes.LongLength,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
            90,
            80,
            generatedAt,
            relativePath));
    }

    private static byte[] CreateGradient(bool reverse)
    {
        using Mat image = new(new Size(90, 80), MatType.CV_8UC3);
        int rows = image.Rows;
        int columns = image.Cols;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int normalized = column * 255 / Math.Max(1, columns - 1);
                byte value = (byte)(reverse ? 255 - normalized : normalized);
                image.Set(row, column, new Vec3b(value, value, value));
            }
        }

        Cv2.ImEncode(
            ".jpg",
            image,
            out byte[] encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 92));
        return encoded;
    }

    private static async Task SetTakenAtAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        DateTime takenAtLocal)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_capture_metadata (
                asset_revision_id,
                taken_at_local,
                utc_offset_minutes,
                latitude,
                longitude,
                extracted_at_utc)
            VALUES ($revision_id, $taken_at_local, NULL, NULL, NULL, $extracted_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                taken_at_local = excluded.taken_at_local,
                extracted_at_utc = excluded.extracted_at_utc;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.Value.ToString("D"));
        command.Parameters.AddWithValue("$taken_at_local", takenAtLocal.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
        command.Parameters.AddWithValue("$extracted_at_utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<CatalogueAssetRevision> CreateRevisionAsync(
        SqliteAssetCatalogueRepository catalogue,
        string root,
        string sourceKey,
        char hashCharacter)
    {
        string sourceRoot = Path.Combine(root, Path.GetFileNameWithoutExtension(sourceKey));
        Directory.CreateDirectory(sourceRoot);
        DateTimeOffset now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        return await catalogue.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, sourceKey, now),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                100,
                now,
                "image/jpeg",
                90,
                80));
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

    private sealed class CreativeVisualApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _proxyRoot;
        private readonly string _profileId;

        public CreativeVisualApiFactory(
            string databasePath,
            string proxyRoot,
            string profileId)
        {
            _databasePath = databasePath;
            _proxyRoot = proxyRoot;
            _profileId = profileId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:ReviewProxyRoot", _proxyRoot);
            builder.UseSetting("PhotoIdentity:ReviewProxyProfileId", _profileId);
        }
    }
}
