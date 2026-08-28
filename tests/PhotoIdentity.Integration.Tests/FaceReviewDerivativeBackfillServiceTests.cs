using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class FaceReviewDerivativeBackfillServiceTests
{
    [Fact]
    public async Task Ready_revision_generation_completes_without_hydration_or_release()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "source");
            string sourceDirectory = Path.Combine(sourceRoot, "2026");
            string sourcePath = Path.Combine(sourceDirectory, "photo.jpg");
            string derivativeRoot = Path.Combine(directory, "derivatives");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(derivativeRoot);

            byte[] originalBytes;
            using (Mat original = new(new Size(1200, 800), MatType.CV_8UC3, new Scalar(40, 80, 120)))
            {
                Cv2.Rectangle(original, new Rect(300, 160, 360, 360), new Scalar(180, 120, 60), thickness: -1);
                Cv2.ImEncode(".jpg", original, out originalBytes);
            }
            await File.WriteAllBytesAsync(sourcePath, originalBytes);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
            CatalogueAsset asset = new(AssetId.New(), source.Id, "2026/photo.jpg", now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant()),
                originalBytes.LongLength,
                now,
                "image/jpeg",
                1200,
                800);
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($face_id, $revision_id, 0, $created_at_utc);

                    INSERT INTO face_observations (
                        face_occurrence_id,
                        detector_model_id,
                        detector_model_hash,
                        confidence,
                        bounding_box_json,
                        landmarks_json,
                        observed_at_utc)
                    VALUES (
                        $face_id,
                        'derivative-liveness-test',
                        $model_hash,
                        0.99,
                        '[0.25,0.20,0.30,0.45]',
                        '[]',
                        $created_at_utc);
                    """;
                command.Parameters.AddWithValue("$face_id", faceId.ToString());
                command.Parameters.AddWithValue("$revision_id", persistedRevision.Id.ToString());
                command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
                command.Parameters.AddWithValue("$model_hash", new string('c', 64));
                await command.ExecuteNonQueryAsync();
            }

            FakeFilesOnDemandPlatform platform = new();
            SqliteArchiveHydrationRepository hydrations = new(database);
            ArchiveHydrationCapacityService capacity = new(
                database,
                hydrations,
                new SqliteArchiveSourceHydrationRepository(database),
                new SqliteArchiveStorageRepository(database),
                platform,
                new FixedStorageProbe(),
                new ArchiveHydrationPolicyConfiguration(null, null, null),
                new ReviewProxyServingConfiguration(derivativeRoot, "test-proxy"),
                TimeProvider.System);
            CollectionOriginalAccessService originals = new(
                new SqliteLocalBatchRepository(database),
                hydrations,
                new SqliteArchiveAvailabilityRepository(database),
                platform,
                capacity,
                TimeProvider.System);

            FaceReviewDerivativeBackfillService service = new(
                database,
                new SqliteLocalBatchRepository(database),
                originals,
                new ReviewProxyGenerationConfiguration(
                    derivativeRoot,
                    "test-proxy",
                    1600,
                    90),
                TimeProvider.System);

            await service.GenerateReadyRevisionAsync(persistedRevision.Id);

            SqliteFaceReviewDerivativeRepository derivatives = new(database);
            Assert.True(await derivatives.IsRevisionCompleteAsync(
                persistedRevision.Id,
                ArchiveFaceReviewDerivativeWriter.ProfileId));
            Assert.NotNull(await derivatives.GetAsync(
                faceId,
                ArchiveFaceReviewDerivativeWriter.ProfileId));
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal(0, platform.ReleaseRequests);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
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

    private sealed class FixedStorageProbe : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => long.MaxValue / 2;
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        public int HydrationRequests { get; private set; }
        public int ReleaseRequests { get; private set; }

        public OneDriveFilesOnDemandState GetState(string path) =>
            new(AssetAvailability.Local, false, false);

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HydrationRequests++;
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseRequests++;
            return Task.CompletedTask;
        }
    }
}
