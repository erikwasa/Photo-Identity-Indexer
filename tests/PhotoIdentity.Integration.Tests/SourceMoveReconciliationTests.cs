using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class SourceMoveReconciliationTests
{
    [Fact]
    public async Task Unique_exact_move_across_included_folders_preserves_asset_revision_and_history()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [1, 2, 3, 4, 5];
        DateTimeOffset t0 = Utc(10);
        DateTimeOffset t1 = Utc(11);
        DateTimeOffset t2 = Utc(12);

        source.Set(new TestItem("A/old.jpg", content, t0, AssetAvailability.Local));
        LocalArchiveSyncSummary first = await catalogue.SyncAsync(source, ["A", "B"], t0);
        Assert.Equal(0, first.ReconciledMoveCount);

        AssetRow before = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/old.jpg"));
        Assert.NotNull(before.RevisionId);
        await catalogue.AddFaceHistoryAsync(before.RevisionId!.Value, t0);

        source.Set(new TestItem("B/new.jpg", content, t0, AssetAvailability.Local));
        LocalArchiveSyncSummary moved = await catalogue.SyncAsync(source, ["A", "B"], t1);

        Assert.Equal(1, moved.ReconciledMoveCount);
        Assert.Equal(0, moved.MarkedDeletedCount);
        Assert.Equal(1, await catalogue.CountAssetsAsync());
        Assert.Null(await catalogue.FindAssetAsync("A/old.jpg"));

        AssetRow after = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/new.jpg"));
        Assert.Equal(before.AssetId, after.AssetId);
        Assert.Equal(before.RevisionId, after.RevisionId);
        Assert.Null(after.DeletedAtUtc);
        Assert.Equal(1, await catalogue.CountFaceHistoryAsync(before.RevisionId.Value));

        LocalArchiveSyncSummary repeated = await catalogue.SyncAsync(source, ["A", "B"], t2);
        Assert.Equal(0, repeated.ReconciledMoveCount);
        AssetRow stable = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/new.jpg"));
        Assert.Equal(before.AssetId, stable.AssetId);
        Assert.Equal(before.RevisionId, stable.RevisionId);
        Assert.Equal(1, await catalogue.CountAssetsAsync());
    }

    [Fact]
    public async Task Existing_same_hash_path_is_a_duplicate_not_a_move()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [9, 8, 7];
        DateTimeOffset t0 = Utc(10);
        DateTimeOffset t1 = Utc(11);

        source.Set(new TestItem("A/original.jpg", content, t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A", "B"], t0);
        AssetRow original = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/original.jpg"));

        source.Set(
            new TestItem("A/original.jpg", content, t0, AssetAvailability.Local),
            new TestItem("B/copy.jpg", content, t0, AssetAvailability.Local));
        LocalArchiveSyncSummary second = await catalogue.SyncAsync(source, ["A", "B"], t1);

        Assert.Equal(0, second.ReconciledMoveCount);
        Assert.Equal(2, await catalogue.CountAssetsAsync());
        AssetRow stillOriginal = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/original.jpg"));
        AssetRow copy = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/copy.jpg"));
        Assert.Equal(original.AssetId, stillOriginal.AssetId);
        Assert.NotEqual(original.AssetId, copy.AssetId);
    }

    [Fact]
    public async Task Multiple_missing_same_hash_candidates_are_left_unresolved()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [4, 4, 4];
        DateTimeOffset t0 = Utc(10);
        DateTimeOffset t1 = Utc(11);

        source.Set(
            new TestItem("A/one.jpg", content, t0, AssetAvailability.Local),
            new TestItem("A/two.jpg", content, t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A", "B"], t0);

        source.Set(new TestItem("B/new.jpg", content, t0, AssetAvailability.Local));
        LocalArchiveSyncSummary second = await catalogue.SyncAsync(source, ["A", "B"], t1);

        Assert.Equal(0, second.ReconciledMoveCount);
        Assert.Equal(3, await catalogue.CountAssetsAsync());
        Assert.NotNull((await catalogue.FindAssetAsync("A/one.jpg"))?.DeletedAtUtc);
        Assert.NotNull((await catalogue.FindAssetAsync("A/two.jpg"))?.DeletedAtUtc);
        Assert.Null((await catalogue.FindAssetAsync("B/new.jpg"))?.DeletedAtUtc);
    }

    [Fact]
    public async Task Online_only_new_path_is_not_reconciled_without_authoritative_hash()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [7, 7, 7];
        DateTimeOffset t0 = Utc(10);
        DateTimeOffset t1 = Utc(11);

        source.Set(new TestItem("A/old.jpg", content, t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A", "B"], t0);
        AssetRow old = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/old.jpg"));

        source.Set(new TestItem("B/new.jpg", content, t0, AssetAvailability.OnlineOnly));
        LocalArchiveSyncSummary second = await catalogue.SyncAsync(source, ["A", "B"], t1);

        Assert.Equal(0, second.ReconciledMoveCount);
        Assert.Equal(2, await catalogue.CountAssetsAsync());
        Assert.NotNull((await catalogue.FindAssetAsync("A/old.jpg"))?.DeletedAtUtc);
        AssetRow onlineOnly = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/new.jpg"));
        Assert.NotEqual(old.AssetId, onlineOnly.AssetId);
        Assert.Null(onlineOnly.RevisionId);
    }

    [Fact]
    public async Task Scoped_scan_does_not_reconcile_against_unscanned_path()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [2, 2, 2];
        DateTimeOffset t0 = Utc(10);
        DateTimeOffset t1 = Utc(11);

        source.Set(new TestItem("A/old.jpg", content, t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A"], t0);
        AssetRow old = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/old.jpg"));

        source.Set(new TestItem("B/new.jpg", content, t0, AssetAvailability.Local));
        LocalArchiveSyncSummary scoped = await catalogue.SyncAsync(source, ["B"], t1);

        Assert.Equal(0, scoped.ReconciledMoveCount);
        Assert.Equal(2, await catalogue.CountAssetsAsync());
        AssetRow unscannedOld = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/old.jpg"));
        Assert.Equal(old.AssetId, unscannedOld.AssetId);
        Assert.Null(unscannedOld.DeletedAtUtc);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 9, 25, hour, 0, 0, TimeSpan.Zero);

    private sealed record TestItem(
        string SourceKey,
        byte[] Content,
        DateTimeOffset LastWriteTimeUtc,
        AssetAvailability Availability);

    private sealed class MutableArchiveSource : IAssetSource
    {
        private readonly SourceId _sourceId;
        private readonly Dictionary<string, TestItem> _items = new(StringComparer.Ordinal);

        public MutableArchiveSource(SourceId sourceId) => _sourceId = sourceId;

        public void Set(params TestItem[] items)
        {
            _items.Clear();
            foreach (TestItem item in items)
            {
                _items[item.SourceKey] = item;
            }
        }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            string scope = ArchiveCoverage.NormalizeRelativeFolder(options.RelativeRoot ?? string.Empty);
            foreach (TestItem item in _items.Values.OrderBy(static item => item.SourceKey, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ArchiveCoverage.Covers(scope, item.SourceKey))
                {
                    continue;
                }

                yield return new SourceAsset(
                    new SourceAssetReference(_sourceId, item.SourceKey),
                    item.SourceKey,
                    "image/jpeg",
                    item.Content.LongLength,
                    item.LastWriteTimeUtc,
                    item.Availability);
            }
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) =>
            Task.FromResult(_items[asset.ItemKey].Availability);

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            TestItem item = _items[asset.ItemKey];
            if (item.Availability != AssetAvailability.Local)
            {
                throw new InvalidOperationException("Online-only test assets must not be opened.");
            }

            return Task.FromResult<Stream>(new MemoryStream(item.Content, writable: false));
        }
    }

    private sealed record AssetRow(
        AssetId AssetId,
        string SourceKey,
        AssetRevisionId? RevisionId,
        DateTimeOffset? DeletedAtUtc);

    private sealed class TestCatalogue : IAsyncDisposable
    {
        private readonly string _root;
        private readonly SqliteCatalogueDatabase _database;
        private readonly LocalArchiveSyncCoordinator _coordinator;

        private TestCatalogue(
            string root,
            SqliteCatalogueDatabase database,
            SourceId sourceId,
            LocalArchiveSyncCoordinator coordinator)
        {
            _root = root;
            _database = database;
            SourceId = sourceId;
            _coordinator = coordinator;
        }

        public SourceId SourceId { get; }

        public static async Task<TestCatalogue> CreateAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), $"photoidentity-move-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            SqliteCatalogueDatabase database = new(Path.Combine(root, "catalogue.db"));
            await database.InitializeAsync();
            SourceId sourceId = SourceId.New();
            ArchiveSourceCatalogueScanner scanner = new(
                database,
                new SqliteArchiveSourceScanBatchRepository(database));
            LocalArchiveSyncCoordinator coordinator = new(
                scanner,
                moveReconciler: new SqliteArchiveSourceMoveReconciler(database));
            return new TestCatalogue(root, database, sourceId, coordinator);
        }

        public Task<LocalArchiveSyncSummary> SyncAsync(
            IAssetSource source,
            IReadOnlyList<string> includedFolders,
            DateTimeOffset scannedAtUtc) =>
            _coordinator.SyncAsync(
                source,
                new ArchiveCatalogueSource(SourceId, "local-folder", _root, Utc(9)),
                includedFolders,
                scannedAtUtc);

        public async Task<AssetRow?> FindAssetAsync(string sourceKey)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT asset.id, asset.source_key, observation.verified_revision_id, asset.deleted_at_utc
                FROM assets AS asset
                LEFT JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
                WHERE asset.source_id = $source_id AND asset.source_key = $source_key;
                """;
            command.Parameters.AddWithValue("$source_id", SourceId.ToString());
            command.Parameters.AddWithValue("$source_key", sourceKey);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new AssetRow(
                AssetId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(2))),
                reader.IsDBNull(3) ? null : Parse(reader.GetString(3)));
        }

        public async Task<int> CountAssetsAsync()
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM assets WHERE source_id = $source_id;";
            command.Parameters.AddWithValue("$source_id", SourceId.ToString());
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task AddFaceHistoryAsync(AssetRevisionId revisionId, DateTimeOffset createdAtUtc)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($id, $revision_id, 0, $created_at_utc);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> CountFaceHistoryAsync(AssetRevisionId revisionId)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM face_occurrences WHERE asset_revision_id = $revision_id;";
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static string Format(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static DateTimeOffset Parse(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }
}
