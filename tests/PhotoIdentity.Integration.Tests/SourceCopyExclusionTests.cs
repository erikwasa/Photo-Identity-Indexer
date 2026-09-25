using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class SourceCopyExclusionTests
{
    [Fact]
    public async Task Exclusion_is_durable_and_scoped_to_one_exact_duplicate_locator()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [1, 2, 3, 4];
        DateTimeOffset t0 = Utc(10);

        source.Set(
            new TestItem("A/one.jpg", content, t0, AssetAvailability.Local),
            new TestItem("B/two.jpg", content, t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A", "B"], t0);

        AssetRow first = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/one.jpg"));
        AssetRow second = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/two.jpg"));
        Assert.NotNull(first.RevisionId);
        Assert.NotNull(second.RevisionId);

        SourceCopyExclusionState state = await catalogue.Exclusions.ExcludeAsync(
            catalogue.SourceId,
            "A/one.jpg",
            Utc(11));
        Assert.Equal(SourceCopyPurgeStates.Pending, state.PurgeState);
        Assert.True(await catalogue.Exclusions.IsRevisionExcludedAsync(first.RevisionId!.Value));
        Assert.False(await catalogue.Exclusions.IsRevisionExcludedAsync(second.RevisionId!.Value));

        await catalogue.Exclusions.SetPurgeStateAsync(
            catalogue.SourceId,
            "A/one.jpg",
            SourceCopyPurgeStates.Failed,
            "derivative-delete-failed",
            Utc(12));

        // A new repository instance simulates application restart; the locator tombstone survives.
        SqliteSourceCopyExclusionRepository restarted = new(catalogue.Database);
        SourceCopyExclusionState durable = Assert.IsType<SourceCopyExclusionState>(
            await restarted.GetAsync(catalogue.SourceId, "A/one.jpg"));
        Assert.Equal(state.ExcludedAtUtc, durable.ExcludedAtUtc);
        Assert.Equal(SourceCopyPurgeStates.Failed, durable.PurgeState);
        Assert.Equal("derivative-delete-failed", durable.PurgeErrorCode);
        Assert.True(await restarted.IsRevisionExcludedAsync(first.RevisionId.Value));
        Assert.False(await restarted.IsRevisionExcludedAsync(second.RevisionId.Value));
    }

    [Fact]
    public async Task Excluded_locator_is_observed_without_opening_and_exclusion_does_not_follow_move()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        byte[] content = [9, 8, 7, 6];
        DateTimeOffset t0 = Utc(10);
        DateTimeOffset t1 = Utc(11);
        DateTimeOffset t2 = Utc(12);

        source.Set(new TestItem("A/private.jpg", content, t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A", "B"], t0);
        AssetRow old = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/private.jpg"));
        Assert.NotNull(old.RevisionId);
        int openedBeforeExclusion = source.OpenCount;

        await catalogue.Exclusions.ExcludeAsync(catalogue.SourceId, "A/private.jpg", t1);

        // Same locator may be observed for lifecycle status, but its bytes must not be reopened.
        LocalArchiveSyncSummary sameLocator = await catalogue.SyncAsync(source, ["A", "B"], t1);
        Assert.Equal(openedBeforeExclusion, source.OpenCount);
        Assert.Equal(0, sameLocator.ReconciledMoveCount);
        SourceCopyExclusionState observed = Assert.IsType<SourceCopyExclusionState>(
            await catalogue.Exclusions.GetAsync(catalogue.SourceId, "A/private.jpg"));
        Assert.Equal(t1, observed.LastSeenAtUtc);

        // Rename/move the excluded source. The new locator is an independently included source copy.
        source.Set(new TestItem("B/moved.jpg", content, t0, AssetAvailability.Local));
        LocalArchiveSyncSummary moved = await catalogue.SyncAsync(source, ["A", "B"], t2);
        Assert.Equal(0, moved.ReconciledMoveCount);

        AssetRow newCopy = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/moved.jpg"));
        Assert.NotEqual(old.AssetId, newCopy.AssetId);
        Assert.NotNull(newCopy.RevisionId);
        Assert.False(await catalogue.Exclusions.IsRevisionExcludedAsync(newCopy.RevisionId!.Value));
        Assert.True(await catalogue.Exclusions.IsRevisionExcludedAsync(old.RevisionId!.Value));
        Assert.Null(await catalogue.Exclusions.GetAsync(catalogue.SourceId, "B/moved.jpg"));
    }

    [Fact]
    public async Task Excluded_revision_is_not_returned_for_archive_analysis_scheduling()
    {
        await using TestCatalogue catalogue = await TestCatalogue.CreateAsync();
        MutableArchiveSource source = new(catalogue.SourceId);
        DateTimeOffset t0 = Utc(10);

        source.Set(
            new TestItem("A/private.jpg", [1, 3, 5, 7], t0, AssetAvailability.Local),
            new TestItem("B/visible.jpg", [2, 4, 6, 8], t0, AssetAvailability.Local));
        await catalogue.SyncAsync(source, ["A", "B"], t0);

        AssetRow privateAsset = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("A/private.jpg"));
        AssetRow visibleAsset = Assert.IsType<AssetRow>(await catalogue.FindAssetAsync("B/visible.jpg"));
        Assert.NotNull(privateAsset.RevisionId);
        Assert.NotNull(visibleAsset.RevisionId);

        await catalogue.Exclusions.ExcludeAsync(catalogue.SourceId, "A/private.jpg", Utc(11));

        SqliteArchiveAnalysisRepository analysis = new(catalogue.Database);
        IReadOnlyList<AssetRevisionId> pending = await analysis.GetPendingCurrentRevisionIdsAsync(
            catalogue.SourceId,
            new Sha256Digest(new string('a', 64)));

        Assert.DoesNotContain(privateAsset.RevisionId.Value, pending);
        Assert.Contains(visibleAsset.RevisionId.Value, pending);
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
        public int OpenCount { get; private set; }

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
                throw new InvalidOperationException("Non-local test assets must not be opened.");
            }
            OpenCount++;
            return Task.FromResult<Stream>(new MemoryStream(item.Content, writable: false));
        }
    }

    private sealed record AssetRow(
        AssetId AssetId,
        AssetRevisionId? RevisionId,
        DateTimeOffset? DeletedAtUtc);

    private sealed class TestCatalogue : IAsyncDisposable
    {
        private readonly string _root;
        private readonly LocalArchiveSyncCoordinator _coordinator;

        private TestCatalogue(
            string root,
            SqliteCatalogueDatabase database,
            SourceId sourceId,
            SqliteSourceCopyExclusionRepository exclusions,
            LocalArchiveSyncCoordinator coordinator)
        {
            _root = root;
            Database = database;
            SourceId = sourceId;
            Exclusions = exclusions;
            _coordinator = coordinator;
        }

        public SqliteCatalogueDatabase Database { get; }
        public SourceId SourceId { get; }
        public SqliteSourceCopyExclusionRepository Exclusions { get; }

        public static async Task<TestCatalogue> CreateAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), $"photoidentity-exclusion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            SqliteCatalogueDatabase database = new(Path.Combine(root, "catalogue.db"));
            await database.InitializeAsync();
            SourceId sourceId = SourceId.New();
            SqliteSourceCopyExclusionRepository exclusions = new(database);
            ArchiveSourceCatalogueScanner scanner = new(
                database,
                new SqliteArchiveSourceScanBatchRepository(database),
                exclusions);
            LocalArchiveSyncCoordinator coordinator = new(
                scanner,
                moveReconciler: new SqliteArchiveSourceMoveReconciler(database, exclusions));
            return new TestCatalogue(root, database, sourceId, exclusions, coordinator);
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
            await using SqliteConnection connection = await Database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT asset.id, observation.verified_revision_id, asset.deleted_at_utc
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
                reader.IsDBNull(1) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
                reader.IsDBNull(2) ? null : Parse(reader.GetString(2)));
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static DateTimeOffset Parse(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }
}
