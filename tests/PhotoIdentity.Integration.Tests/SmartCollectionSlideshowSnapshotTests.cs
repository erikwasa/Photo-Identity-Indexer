using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionSlideshowSnapshotTests
{
    [Fact]
    public async Task Snapshot_handles_zero_one_and_more_than_two_hundred_items_and_remains_immutable()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            FixedTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 29, 8, 0, 0, TimeSpan.Zero));
            SqliteSmartCollectionRepository definitions = new(database, timeProvider);
            SqliteSmartCollectionQueryRepository snapshots = new(database, timeProvider);
            SqliteAssetCatalogueRepository catalogue = new(database);
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "All photos",
                new SmartCollectionFilter());

            SmartCollectionSlideshowSnapshot zero =
                await snapshots.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Empty(zero.RevisionIds);

            SourceId sourceId = SourceId.New();
            string sourceRoot = Path.Combine(directory, "private-source");
            IReadOnlyList<AssetRevisionId> first = await SeedRevisionsAsync(
                database,
                sourceId,
                sourceRoot,
                [new SeedRevisionRequest(
                    "private/000.jpg",
                    new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))]);

            SmartCollectionSlideshowSnapshot one =
                await snapshots.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal(first[0], Assert.Single(one.RevisionIds));

            SeedRevisionRequest[] remainder = Enumerable.Range(1, 204)
                .Select(index => new SeedRevisionRequest(
                    $"private/{index:D3}.jpg",
                    new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index)))
                .ToArray();
            IReadOnlyList<AssetRevisionId> remaining =
                await SeedRevisionsAsync(database, sourceId, sourceRoot, remainder);

            SmartCollectionSlideshowSnapshot many =
                await snapshots.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal(205, many.RevisionIds.Count);
            Assert.Equal(205, many.RevisionIds.Distinct().Count());

            AssetRevisionId[] immutableIds = many.RevisionIds.ToArray();
            AssetRevisionId metadataMutationTarget = remaining[^1];
            await catalogue.SavePhotoMetadataAsync(
                metadataMutationTarget,
                new PhotoCaptureMetadata(
                    new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    null,
                    null,
                    null),
                timeProvider.GetUtcNow());

            SmartCollectionSlideshowSnapshot reevaluated =
                await snapshots.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal(metadataMutationTarget, reevaluated.RevisionIds[0]);
            Assert.Equal(immutableIds, many.RevisionIds);

            SmartCollectionDefinition? updated = await definitions.UpdateAsync(
                saved.Id,
                "No matches",
                new SmartCollectionFilter(tags: ["never/matches"]));
            Assert.NotNull(updated);

            SmartCollectionSlideshowSnapshot afterFilterChange =
                await snapshots.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Empty(afterFilterChange.RevisionIds);
            Assert.Equal("All photos", many.CollectionName);
            Assert.Equal(immutableIds, many.RevisionIds);
            Assert.Single(one.RevisionIds);
            Assert.Empty(zero.RevisionIds);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Snapshot_orders_oldest_to_newest_using_capture_then_observed_time_and_revision_tie_break()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            FixedTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 29, 8, 30, 0, TimeSpan.Zero));
            SqliteSmartCollectionRepository definitions = new(database, timeProvider);
            SqliteSmartCollectionQueryRepository snapshots = new(database, timeProvider);
            SqliteAssetCatalogueRepository catalogue = new(database);
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Chronology",
                new SmartCollectionFilter());

            AssetRevisionId capture2018 = AssetRevisionId.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            AssetRevisionId observed2019 = AssetRevisionId.From(Guid.Parse("00000000-0000-0000-0000-000000000002"));
            AssetRevisionId capture2021 = AssetRevisionId.From(Guid.Parse("00000000-0000-0000-0000-000000000003"));
            AssetRevisionId tiedFirst = AssetRevisionId.From(Guid.Parse("00000000-0000-0000-0000-000000000004"));
            AssetRevisionId tiedSecond = AssetRevisionId.From(Guid.Parse("00000000-0000-0000-0000-000000000005"));

            await SeedRevisionsAsync(
                database,
                SourceId.New(),
                Path.Combine(directory, "chronology-source"),
                [
                    new SeedRevisionRequest(
                        "private/capture-2018.jpg",
                        new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        capture2018),
                    new SeedRevisionRequest(
                        "private/observed-2019.jpg",
                        new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        observed2019),
                    new SeedRevisionRequest(
                        "private/capture-2021.jpg",
                        new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        capture2021),
                    new SeedRevisionRequest(
                        "private/tie-a.jpg",
                        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        tiedFirst),
                    new SeedRevisionRequest(
                        "private/tie-b.jpg",
                        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        tiedSecond),
                ]);

            await catalogue.SavePhotoMetadataAsync(
                capture2018,
                new PhotoCaptureMetadata(
                    new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    null,
                    null,
                    null),
                timeProvider.GetUtcNow());
            await catalogue.SavePhotoMetadataAsync(
                capture2021,
                new PhotoCaptureMetadata(
                    new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    null,
                    null,
                    null),
                timeProvider.GetUtcNow());

            SmartCollectionSlideshowSnapshot snapshot =
                await snapshots.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();

            Assert.Equal(
                [capture2018, observed2019, tiedFirst, tiedSecond, capture2021],
                snapshot.RevisionIds);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Snapshot_API_returns_lightweight_manifest_without_source_paths_or_filenames()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            string privateRoot = Path.Combine(directory, "private-source-root");
            string privateFileName = "family/private-file-name.jpg";
            AssetRevisionId revisionId = (await SeedRevisionsAsync(
                database,
                SourceId.New(),
                privateRoot,
                [new SeedRevisionRequest(
                    privateFileName,
                    new DateTimeOffset(2022, 4, 5, 12, 0, 0, TimeSpan.Zero))]))[0];

            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Phone slideshow",
                new SmartCollectionFilter());

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                $"/api/smart-collections/{saved.Id}/slideshow-snapshot",
                content: null);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(privateFileName, json, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetFileName(privateRoot), json, StringComparison.Ordinal);
            Assert.DoesNotContain("assetId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("thumbnailUrl", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("previewUrl", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("originalUrl", json, StringComparison.OrdinalIgnoreCase);

            SmartCollectionSlideshowSnapshotResponse snapshot =
                await response.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
                ?? throw new InvalidOperationException();
            Assert.Equal(saved.Id.ToString(), snapshot.CollectionId);
            Assert.Equal("Phone slideshow", snapshot.CollectionName);
            Assert.Equal(1, snapshot.Total);
            Assert.Equal(revisionId.ToString(), Assert.Single(snapshot.Items).RevisionId);

            using HttpResponseMessage missing = await client.PostAsync(
                $"/api/smart-collections/{Guid.NewGuid():D}/slideshow-snapshot",
                content: null);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<IReadOnlyList<AssetRevisionId>> SeedRevisionsAsync(
        SqliteCatalogueDatabase database,
        SourceId sourceId,
        string sourceRoot,
        IReadOnlyList<SeedRevisionRequest> revisions)
    {
        Directory.CreateDirectory(sourceRoot);
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
                INSERT OR IGNORE INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($id, 'local-folder', $root, $created);
                """;
            source.Parameters.AddWithValue("$id", sourceId.ToString());
            source.Parameters.AddWithValue("$root", sourceRoot);
            source.Parameters.AddWithValue(
                "$created",
                new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    .ToString("O", CultureInfo.InvariantCulture));
            await source.ExecuteNonQueryAsync();
        }

        List<AssetRevisionId> ids = [];
        for (int index = 0; index < revisions.Count; index++)
        {
            SeedRevisionRequest seed = revisions[index];
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = seed.RevisionId ?? AssetRevisionId.New();
            ids.Add(revisionId);

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO assets (id, source_id, source_key, created_at_utc)
                VALUES ($asset_id, $source_id, $source_key, $created_at_utc);

                INSERT INTO asset_revisions (
                    id,
                    asset_id,
                    content_sha256,
                    size_bytes,
                    observed_at_utc,
                    media_type,
                    width,
                    height)
                VALUES (
                    $revision_id,
                    $asset_id,
                    $content_sha256,
                    100,
                    $observed_at_utc,
                    'image/jpeg',
                    1920,
                    1080);
                """;
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue("$source_id", sourceId.ToString());
            command.Parameters.AddWithValue("$source_key", seed.SourceKey);
            command.Parameters.AddWithValue(
                "$created_at_utc",
                seed.ObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            command.Parameters.AddWithValue(
                "$content_sha256",
                index.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, '0'));
            command.Parameters.AddWithValue(
                "$observed_at_utc",
                seed.ObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        return ids;
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

    private sealed record SeedRevisionRequest(
        string SourceKey,
        DateTimeOffset ObservedAtUtc,
        AssetRevisionId? RevisionId = null);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
