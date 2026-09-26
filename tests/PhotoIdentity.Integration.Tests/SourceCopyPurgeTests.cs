using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class SourceCopyPurgeTests
{
    [Fact]
    public async Task Purge_deletes_known_files_and_revision_state_but_retains_tombstone_and_shared_people()
    {
        await using PurgeFixture fixture = await PurgeFixture.CreateAsync();
        await fixture.Exclusions.ExcludeAsync(fixture.SourceId, fixture.SourceKey, fixture.Now.AddMinutes(1));

        Assert.False(await fixture.Exclusions.RestoreAsync(fixture.SourceId, fixture.SourceKey));

        SourceCopyPurgeService service = fixture.CreateService(new SourceCopyPurgeFileSystem());
        Assert.True(await service.PurgeAsync(fixture.SourceId, fixture.SourceKey));
        Assert.True(await service.PurgeAsync(fixture.SourceId, fixture.SourceKey));

        Assert.False(File.Exists(fixture.ProxyPath));
        Assert.False(File.Exists(fixture.FaceReviewPath));
        Assert.False(File.Exists(fixture.FaceCropPath));
        Assert.Equal(0, await fixture.CountAsync("assets"));
        Assert.Equal(0, await fixture.CountAsync("asset_revisions"));
        Assert.Equal(0, await fixture.CountAsync("face_occurrences"));
        Assert.Equal(0, await fixture.CountAsync("face_crops"));
        Assert.Equal(0, await fixture.CountAsync("asset_revision_review_proxies"));
        Assert.Equal(0, await fixture.CountAsync("face_review_derivatives"));
        Assert.Equal(1, await fixture.CountAsync("people"));

        SourceCopyExclusionState state = Assert.IsType<SourceCopyExclusionState>(
            await fixture.Exclusions.GetAsync(fixture.SourceId, fixture.SourceKey));
        Assert.Equal(SourceCopyPurgeStates.Completed, state.PurgeState);
        Assert.Null(state.PurgeErrorCode);

        Assert.True(await fixture.Exclusions.RestoreAsync(fixture.SourceId, fixture.SourceKey));
        Assert.Null(await fixture.Exclusions.GetAsync(fixture.SourceId, fixture.SourceKey));
    }

    [Fact]
    public async Task Failed_file_deletion_keeps_catalogue_and_restart_reuses_durable_manifest()
    {
        await using PurgeFixture fixture = await PurgeFixture.CreateAsync();
        await fixture.Exclusions.ExcludeAsync(fixture.SourceId, fixture.SourceKey, fixture.Now.AddMinutes(1));

        SourceCopyPurgeService failing = fixture.CreateService(
            new ThrowOnceFileSystem(fixture.ProxyPath));
        Assert.False(await failing.PurgeAsync(fixture.SourceId, fixture.SourceKey));

        SourceCopyExclusionState failed = Assert.IsType<SourceCopyExclusionState>(
            await fixture.Exclusions.GetAsync(fixture.SourceId, fixture.SourceKey));
        Assert.Equal(SourceCopyPurgeStates.Failed, failed.PurgeState);
        Assert.Equal("artifact-delete-failed", failed.PurgeErrorCode);
        Assert.True(File.Exists(fixture.ProxyPath));
        Assert.Equal(1, await fixture.CountAsync("assets"));
        Assert.False(await fixture.Exclusions.RestoreAsync(fixture.SourceId, fixture.SourceKey));

        SourceCopyPurgeService restarted = fixture.CreateRestartedService();
        Assert.True(await restarted.PurgeAsync(fixture.SourceId, fixture.SourceKey));
        await AssertCompletedAsync(fixture);
    }

    [Fact]
    public async Task Crash_after_filesystem_cleanup_retries_catalogue_cleanup_with_missing_files()
    {
        await using PurgeFixture fixture = await PurgeFixture.CreateAsync();
        await fixture.Exclusions.ExcludeAsync(fixture.SourceId, fixture.SourceKey, fixture.Now.AddMinutes(1));

        ISourceCopyPurgeRepository inner = new SqliteSourceCopyPurgeRepository(fixture.Database);
        SourceCopyPurgeService failing = fixture.CreateService(
            new SourceCopyPurgeFileSystem(),
            new ThrowOncePurgeRepository(inner, ThrowStage.BeforeCatalogueDelete));
        Assert.False(await failing.PurgeAsync(fixture.SourceId, fixture.SourceKey));

        Assert.False(File.Exists(fixture.ProxyPath));
        Assert.False(File.Exists(fixture.FaceReviewPath));
        Assert.False(File.Exists(fixture.FaceCropPath));
        Assert.Equal(1, await fixture.CountAsync("assets"));
        Assert.Equal(1, await fixture.CountAsync("source_copy_purge_manifests"));
        Assert.False(await fixture.Exclusions.RestoreAsync(fixture.SourceId, fixture.SourceKey));

        SourceCopyPurgeService restarted = fixture.CreateRestartedService();
        Assert.True(await restarted.PurgeAsync(fixture.SourceId, fixture.SourceKey));
        await AssertCompletedAsync(fixture);
    }

    [Fact]
    public async Task Crash_after_catalogue_cleanup_retries_from_manifest_and_finishes_tombstone()
    {
        await using PurgeFixture fixture = await PurgeFixture.CreateAsync();
        await fixture.Exclusions.ExcludeAsync(fixture.SourceId, fixture.SourceKey, fixture.Now.AddMinutes(1));

        ISourceCopyPurgeRepository inner = new SqliteSourceCopyPurgeRepository(fixture.Database);
        SourceCopyPurgeService failing = fixture.CreateService(
            new SourceCopyPurgeFileSystem(),
            new ThrowOncePurgeRepository(inner, ThrowStage.BeforeManifestClear));
        Assert.False(await failing.PurgeAsync(fixture.SourceId, fixture.SourceKey));

        Assert.False(File.Exists(fixture.ProxyPath));
        Assert.False(File.Exists(fixture.FaceReviewPath));
        Assert.False(File.Exists(fixture.FaceCropPath));
        Assert.Equal(0, await fixture.CountAsync("assets"));
        Assert.Equal(1, await fixture.CountAsync("source_copy_purge_manifests"));
        SourceCopyExclusionState failed = Assert.IsType<SourceCopyExclusionState>(
            await fixture.Exclusions.GetAsync(fixture.SourceId, fixture.SourceKey));
        Assert.Equal(SourceCopyPurgeStates.Failed, failed.PurgeState);
        Assert.False(await fixture.Exclusions.RestoreAsync(fixture.SourceId, fixture.SourceKey));

        SourceCopyPurgeService restarted = fixture.CreateRestartedService();
        Assert.True(await restarted.PurgeAsync(fixture.SourceId, fixture.SourceKey));
        await AssertCompletedAsync(fixture);
        Assert.Equal(0, await fixture.CountAsync("source_copy_purge_manifests"));
    }

    private static async Task AssertCompletedAsync(PurgeFixture fixture)
    {
        Assert.False(File.Exists(fixture.ProxyPath));
        Assert.False(File.Exists(fixture.FaceReviewPath));
        Assert.False(File.Exists(fixture.FaceCropPath));
        Assert.Equal(0, await fixture.CountAsync("assets"));

        SourceCopyExclusionState completed = Assert.IsType<SourceCopyExclusionState>(
            await fixture.Exclusions.GetAsync(fixture.SourceId, fixture.SourceKey));
        Assert.Equal(SourceCopyPurgeStates.Completed, completed.PurgeState);
        Assert.Null(completed.PurgeErrorCode);
    }

    private sealed class ThrowOnceFileSystem : ISourceCopyPurgeFileSystem
    {
        private readonly string _blockedPath;
        private bool _thrown;
        private readonly SourceCopyPurgeFileSystem _inner = new();

        public ThrowOnceFileSystem(string blockedPath) => _blockedPath = Path.GetFullPath(blockedPath);

        public void DeleteFile(string path)
        {
            if (!_thrown && string.Equals(
                    Path.GetFullPath(path),
                    _blockedPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                _thrown = true;
                throw new IOException("simulated locked derivative");
            }
            _inner.DeleteFile(path);
        }

        public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    }

    private enum ThrowStage
    {
        BeforeCatalogueDelete,
        BeforeManifestClear,
    }

    private sealed class ThrowOncePurgeRepository : ISourceCopyPurgeRepository
    {
        private readonly ISourceCopyPurgeRepository _inner;
        private readonly ThrowStage _stage;
        private bool _thrown;

        public ThrowOncePurgeRepository(ISourceCopyPurgeRepository inner, ThrowStage stage)
        {
            _inner = inner;
            _stage = stage;
        }

        public Task<SourceCopyPurgeManifest> PrepareManifestAsync(
            SourceId sourceId,
            string sourceKey,
            SourceCopyPurgeRoots roots,
            DateTimeOffset preparedAtUtc,
            CancellationToken cancellationToken = default) =>
            _inner.PrepareManifestAsync(sourceId, sourceKey, roots, preparedAtUtc, cancellationToken);

        public async Task PurgeCatalogueAsync(
            SourceId sourceId,
            string sourceKey,
            CancellationToken cancellationToken = default)
        {
            if (!_thrown && _stage == ThrowStage.BeforeCatalogueDelete)
            {
                _thrown = true;
                throw new InvalidOperationException("simulated crash before catalogue cleanup");
            }

            await _inner.PurgeCatalogueAsync(sourceId, sourceKey, cancellationToken);
        }

        public async Task ClearManifestAsync(
            SourceId sourceId,
            string sourceKey,
            CancellationToken cancellationToken = default)
        {
            if (!_thrown && _stage == ThrowStage.BeforeManifestClear)
            {
                _thrown = true;
                throw new InvalidOperationException("simulated crash after catalogue cleanup");
            }

            await _inner.ClearManifestAsync(sourceId, sourceKey, cancellationToken);
        }
    }

    private sealed class PurgeFixture : IAsyncDisposable
    {
        private readonly string _root;

        private PurgeFixture(
            string root,
            SqliteCatalogueDatabase database,
            SqliteSourceCopyExclusionRepository exclusions,
            SourceId sourceId,
            AssetRevisionId revisionId,
            string sourceKey,
            DateTimeOffset now,
            SourceCopyPurgeRoots roots,
            string proxyPath,
            string faceReviewPath,
            string faceCropPath)
        {
            _root = root;
            Database = database;
            Exclusions = exclusions;
            SourceId = sourceId;
            RevisionId = revisionId;
            SourceKey = sourceKey;
            Now = now;
            Roots = roots;
            ProxyPath = proxyPath;
            FaceReviewPath = faceReviewPath;
            FaceCropPath = faceCropPath;
        }

        public SqliteCatalogueDatabase Database { get; }
        public SqliteSourceCopyExclusionRepository Exclusions { get; }
        public SourceId SourceId { get; }
        public AssetRevisionId RevisionId { get; }
        public string SourceKey { get; }
        public DateTimeOffset Now { get; }
        public SourceCopyPurgeRoots Roots { get; }
        public string ProxyPath { get; }
        public string FaceReviewPath { get; }
        public string FaceCropPath { get; }

        public static async Task<PurgeFixture> CreateAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), $"photoidentity-purge-{Guid.NewGuid():N}");
            string analysisRoot = Path.Combine(root, "analysis");
            string reviewRoot = Path.Combine(root, "review");
            string detectorRoot = Path.Combine(root, "detector");
            Directory.CreateDirectory(analysisRoot);
            Directory.CreateDirectory(reviewRoot);
            Directory.CreateDirectory(detectorRoot);

            SqliteCatalogueDatabase database = new(Path.Combine(root, "catalogue.db"));
            await database.InitializeAsync();
            SqliteArchiveReviewProxyRepository reviewProxies = new(database);
            await reviewProxies.RegisterProfileAsync(
                new ReviewProxyProfile("purge-test", 1600, 85),
                DateTimeOffset.UtcNow);
            await new SqliteFaceReviewDerivativeRepository(database).EnsureSchemaAsync();

            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            FaceCropId cropId = FaceCropId.New();
            PersonId personId = PersonId.New();
            DateTimeOffset now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
            string sourceKey = "Private/example.jpg";
            string proxyRelative = "archive/proxy.jpg";
            string faceReviewRelative = "faces/review.jpg";
            Guid runId = Guid.NewGuid();
            string faceCropRelative = $"runs/{runId:D}/assets/{revisionId}/faces/face-001/aligned.png";

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES ($source_id, 'local-folder', 'private-root', $now);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES ($asset_id, $source_id, $source_key, $now, $now);
                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                    VALUES ($revision_id, $asset_id, $hash, 4, $now, 'image/jpeg', 10, 10);
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($face_id, $revision_id, 0, $now);
                    INSERT INTO face_crops (
                        id, face_occurrence_id, crop_protocol, content_sha256,
                        storage_path, width, height, created_at_utc)
                    VALUES ($crop_id, $face_id, 'aligned-test', $hash, $crop_path, 8, 8, $now);
                    INSERT INTO asset_revision_review_proxies (
                        asset_revision_id, profile_id, encoded_byte_length, content_sha256,
                        width, height, generated_at_utc, relative_path)
                    VALUES ($revision_id, 'purge-test', 4, $hash, 10, 10, $now, $proxy_path);
                    INSERT INTO face_review_derivatives (
                        face_occurrence_id, profile_id, encoded_byte_length, content_sha256,
                        width, height, generated_at_utc, relative_path)
                    VALUES ($face_id, 'purge-test', 4, $hash, 8, 8, $now, $face_review_path);
                    INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                    VALUES ($person_id, 'Shared Person', $now, NULL);
                    """;
                seed.Parameters.AddWithValue("$source_id", sourceId.ToString());
                seed.Parameters.AddWithValue("$asset_id", assetId.ToString());
                seed.Parameters.AddWithValue("$source_key", sourceKey);
                seed.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                seed.Parameters.AddWithValue("$face_id", faceId.ToString());
                seed.Parameters.AddWithValue("$crop_id", cropId.ToString());
                seed.Parameters.AddWithValue("$person_id", personId.ToString());
                seed.Parameters.AddWithValue("$hash", new string('a', 64));
                seed.Parameters.AddWithValue("$crop_path", faceCropRelative);
                seed.Parameters.AddWithValue("$proxy_path", proxyRelative);
                seed.Parameters.AddWithValue("$face_review_path", faceReviewRelative);
                seed.Parameters.AddWithValue("$now", Format(now));
                await seed.ExecuteNonQueryAsync();
            }

            string proxyPath = Resolve(reviewRoot, proxyRelative);
            string faceReviewPath = Resolve(reviewRoot, faceReviewRelative);
            string faceCropPath = Resolve(analysisRoot, faceCropRelative);
            await WriteAsync(proxyPath);
            await WriteAsync(faceReviewPath);
            await WriteAsync(faceCropPath);

            SqliteSourceCopyExclusionRepository exclusions = new(database);
            return new PurgeFixture(
                root,
                database,
                exclusions,
                sourceId,
                revisionId,
                sourceKey,
                now,
                new SourceCopyPurgeRoots(analysisRoot, reviewRoot, detectorRoot),
                proxyPath,
                faceReviewPath,
                faceCropPath);
        }

        public SourceCopyPurgeService CreateService(
            ISourceCopyPurgeFileSystem fileSystem,
            ISourceCopyPurgeRepository? purgeRepository = null) => new(
            Exclusions,
            purgeRepository ?? new SqliteSourceCopyPurgeRepository(Database),
            Roots,
            fileSystem,
            TimeProvider.System);

        public SourceCopyPurgeService CreateRestartedService()
        {
            SqliteSourceCopyExclusionRepository exclusions = new(Database);
            SqliteSourceCopyPurgeRepository purges = new(Database);
            return new SourceCopyPurgeService(
                exclusions,
                purges,
                Roots,
                new SourceCopyPurgeFileSystem(),
                TimeProvider.System);
        }

        public async Task<long> CountAsync(string table)
        {
            await using SqliteConnection connection = await Database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
            return ValueTask.CompletedTask;
        }

        private static string Resolve(string root, string relativePath) =>
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static async Task WriteAsync(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        }

        private static string Format(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
