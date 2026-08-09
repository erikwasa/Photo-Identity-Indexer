using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveHydrationIdentityTransferTests
{
    [Fact]
    public async Task Hash_mismatch_moves_active_stale_revision_ownership_back_to_source_until_reverification()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            MutableLocalSource scannerSource = new(source.Id, "photo.jpg", [1, 2, 3], now);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);
            _ = await scanner.ScanAsync(
                scannerSource,
                source,
                new SourceScanOptions(null, true),
                now);

            ArchiveSourceObservation initial = await FindObservationAsync(database, source.Id);
            AssetRevisionId staleRevisionId = Assert.IsType<AssetRevisionId>(initial.VerifiedRevisionId);
            SqliteArchiveHydrationRepository revisionHydrations = new(database);
            await revisionHydrations.ClaimAsync(staleRevisionId, now.AddMinutes(1));
            await revisionHydrations.TouchAsync(staleRevisionId, now.AddMinutes(2));

            // A local sync can establish a newer revision while an older queued run still owns the
            // physical hydration. The transfer must find that active lease by asset, not assume the
            // newest catalogue revision is the lease owner.
            scannerSource.Set([9, 8, 7, 6], now.AddMinutes(3));
            _ = await scanner.ScanAsync(
                scannerSource,
                source,
                new SourceScanOptions(null, true),
                now.AddMinutes(3));
            ArchiveSourceObservation newer = await FindObservationAsync(database, source.Id);
            Assert.NotEqual(staleRevisionId, newer.VerifiedRevisionId);

            await new SqliteArchiveSourceVerificationStateRepository(database).MarkNeedsVerificationAsync(
                initial.AssetId,
                now.AddMinutes(4));

            ArchiveManagedHydrationRecord revisionLease = Assert.IsType<ArchiveManagedHydrationRecord>(
                await revisionHydrations.GetAsync(staleRevisionId));
            Assert.False(revisionLease.IsActive);

            ArchiveManagedSourceHydrationRecord sourceLease = Assert.IsType<ArchiveManagedSourceHydrationRecord>(
                await new SqliteArchiveSourceHydrationRepository(database).GetAsync(initial.AssetId));
            Assert.True(sourceLease.IsActive);
            Assert.False(sourceLease.IsReleaseRequested);

            ArchiveSourceObservation pending = Assert.IsType<ArchiveSourceObservation>(
                await new SqliteArchiveSourceObservationRepository(database).GetNextPendingAsync(source.Id));
            Assert.Equal(initial.AssetId, pending.AssetId);
            Assert.Equal(ArchiveSourceVerificationState.NeedsSourceVerification, pending.VerificationState);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<ArchiveSourceObservation> FindObservationAsync(
        SqliteCatalogueDatabase database,
        SourceId sourceId)
    {
        await using Microsoft.Data.Sqlite.SqliteConnection connection = await database.OpenConnectionAsync();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM assets WHERE source_id = $source_id LIMIT 1;";
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        object? value = await command.ExecuteScalarAsync();
        AssetId assetId = value is string id
            ? AssetId.From(Guid.Parse(id))
            : throw new InvalidOperationException("Test asset was unavailable.");
        return await new SqliteArchiveSourceObservationRepository(database).GetAsync(assetId)
            ?? throw new InvalidOperationException("Source observation was unavailable.");
    }

    private sealed class MutableLocalSource : IAssetSource
    {
        private readonly SourceId _sourceId;
        private readonly string _sourceKey;
        private byte[] _content;
        private DateTimeOffset _lastWriteTimeUtc;

        public MutableLocalSource(
            SourceId sourceId,
            string sourceKey,
            byte[] content,
            DateTimeOffset lastWriteTimeUtc)
        {
            _sourceId = sourceId;
            _sourceKey = sourceKey;
            _content = content;
            _lastWriteTimeUtc = lastWriteTimeUtc;
        }

        public void Set(byte[] content, DateTimeOffset lastWriteTimeUtc)
        {
            _content = content;
            _lastWriteTimeUtc = lastWriteTimeUtc;
        }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceAsset(
                new SourceAssetReference(_sourceId, _sourceKey),
                _sourceKey,
                "image/jpeg",
                _content.LongLength,
                _lastWriteTimeUtc,
                AssetAvailability.Local);
            await Task.CompletedTask;
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) => Task.FromResult(AssetAvailability.Local);

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }
    }
}
