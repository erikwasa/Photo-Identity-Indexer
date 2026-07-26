using System.IO.Compression;
using System.Security.Cryptography;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Transfer.Bundles;
using Xunit;

namespace PhotoIdentity_Bundle_Tests;

public sealed class PortableBundleTests
{
    [Theory]
    [InlineData(PortableBundleProfile.FullImage)]
    [InlineData(PortableBundleProfile.ReducedImage)]
    [InlineData(PortableBundleProfile.FaceCrops)]
    public async Task Worker_processes_every_profile_without_database_access(PortableBundleProfile profile)
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            AssetRevisionId revisionId = AssetRevisionId.New();
            byte[] sourceBytes = "portable-source"u8.ToArray();
            IReadOnlyList<PortableJobInput> inputs = await CreateInputsAsync(directory, profile);
            string jobPath = Path.Combine(directory, "job.photoid-job");
            string resultPath = Path.Combine(directory, "result.photoid-result");
            Sha256Digest transportedHash = profile == PortableBundleProfile.FullImage
                ? Digest(await File.ReadAllBytesAsync(inputs[0].SourcePath))
                : Digest(sourceBytes);
            PortableJobManifest jobManifest = await PortableBundleArchive.CreateJobAsync(
                jobPath,
                new PortableJobBundleRequest(
                    revisionId,
                    transportedHash,
                    profile,
                    "{\"confidenceThreshold\":0.8}",
                    inputs,
                    new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero)));

            RecordingProcessor processor = new();
            PortableResultManifest result = await new PortableBundleWorker(processor).ProcessAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "work"));

            Assert.Equal(profile, processor.ObservedProfile);
            Assert.Equal(inputs.Count, processor.ObservedInputCount);
            Assert.Equal(jobManifest.BundleId, result.BundleId);
            Assert.Equal(revisionId.ToString(), result.AssetRevisionId);
            Assert.Single(result.Faces);

            ExtractedPortableResult extracted = await PortableBundleArchive.ExtractResultAsync(
                resultPath,
                Path.Combine(directory, "result-extracted"));
            PortableBundleFile crop = Assert.Single(extracted.Manifest.Files);
            Assert.True(File.Exists(extracted.ResolveFile(crop)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Corrupt_payload_is_rejected_before_processing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string inputPath = Path.Combine(directory, "input.jpg");
            await File.WriteAllBytesAsync(inputPath, [1, 2, 3, 4]);
            string jobPath = Path.Combine(directory, "job.photoid-job");
            await PortableBundleArchive.CreateJobAsync(
                jobPath,
                new PortableJobBundleRequest(
                    AssetRevisionId.New(),
                    Digest([1, 2, 3, 4]),
                    PortableBundleProfile.FullImage,
                    "{}",
                    [new PortableJobInput(inputPath, "inputs/source.jpg", PortableBundleRoles.SourceImage)],
                    DateTimeOffset.UtcNow));

            using (FileStream stream = new(jobPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Update))
            {
                ZipArchiveEntry original = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("inputs/source.jpg"));
                original.Delete();
                ZipArchiveEntry replacement = archive.CreateEntry("inputs/source.jpg");
                await using Stream output = replacement.Open();
                await output.WriteAsync(new byte[] { 9, 9, 9, 9 });
            }

            await Assert.ThrowsAsync<PortableBundleValidationException>(() =>
                PortableBundleArchive.ExtractJobAsync(
                    jobPath,
                    Path.Combine(directory, "extract")));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Import_is_idempotent_rejects_stale_results_and_preserves_human_labels()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 7, 26, 13, 0, 0, TimeSpan.Zero);
            byte[] sourceBytes = "canonical-photo"u8.ToArray();
            Sha256Digest sourceHash = Digest(sourceBytes);
            CatalogueAssetRevision revision = await SeedRevisionAsync(database, directory, sourceBytes, sourceHash, now);
            CatalogueReviewPerson person = await SeedHumanAssignmentAsync(database, revision.Id, directory, now);

            string inputPath = Path.Combine(directory, "bundle-input.jpg");
            await File.WriteAllBytesAsync(inputPath, sourceBytes);
            string jobPath = Path.Combine(directory, "job.photoid-job");
            string resultPath = Path.Combine(directory, "result.photoid-result");
            await PortableBundleArchive.CreateJobAsync(
                jobPath,
                new PortableJobBundleRequest(
                    revision.Id,
                    sourceHash,
                    PortableBundleProfile.FullImage,
                    "{}",
                    [new PortableJobInput(inputPath, "inputs/source.jpg", PortableBundleRoles.SourceImage)],
                    now));
            await new PortableBundleWorker(new RecordingProcessor()).ProcessAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "worker"));

            SqliteBundleResultImporter importer = new(database);
            PortableBundleImportResult first = await importer.ImportAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "imported"),
                Path.Combine(directory, "import-work"));
            PortableBundleImportResult second = await importer.ImportAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "imported"),
                Path.Combine(directory, "import-work"));
            Assert.Equal(first, second);
            Assert.Single(await new SqliteFaceCatalogueRepository(database).GetOccurrencesAsync(revision.Id));

            CatalogueReviewFace reviewed = Assert.IsType<CatalogueReviewFace>(
                await new SqliteReviewRepository(database).GetFaceAsync(
                    Assert.Single(await new SqliteFaceCatalogueRepository(database).GetOccurrencesAsync(revision.Id)).Id));
            Assert.Equal(CatalogueReviewStates.Assigned, reviewed.State);
            Assert.Equal(person.Id, reviewed.Person?.Id);

            string staleJobPath = Path.Combine(directory, "stale.photoid-job");
            string staleResultPath = Path.Combine(directory, "stale.photoid-result");
            await PortableBundleArchive.CreateJobAsync(
                staleJobPath,
                new PortableJobBundleRequest(
                    revision.Id,
                    Digest("different-content"u8),
                    PortableBundleProfile.ReducedImage,
                    "{}",
                    [new PortableJobInput(inputPath, "inputs/reduced.jpg", PortableBundleRoles.ReducedImage)],
                    now));
            await new PortableBundleWorker(new RecordingProcessor()).ProcessAsync(
                staleJobPath,
                staleResultPath,
                Path.Combine(directory, "stale-worker"));
            await Assert.ThrowsAsync<PortableBundleValidationException>(() => importer.ImportAsync(
                staleJobPath,
                staleResultPath,
                Path.Combine(directory, "imported"),
                Path.Combine(directory, "import-work")));
            await Assert.ThrowsAsync<PortableBundleValidationException>(() => importer.ImportAsync(
                staleJobPath,
                resultPath,
                Path.Combine(directory, "imported"),
                Path.Combine(directory, "import-work")));

            string corruptCropPath = Assert.Single((await PortableBundleArchive.ExtractResultAsync(
                resultPath,
                Path.Combine(directory, "read-before-corrupt"))).Manifest.Files).Path;
            using (FileStream stream = new(resultPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Update))
            {
                ZipArchiveEntry original = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(corruptCropPath));
                original.Delete();
                ZipArchiveEntry replacement = archive.CreateEntry(corruptCropPath);
                await using Stream output = replacement.Open();
                await output.WriteAsync(new byte[] { 7, 7, 7 });
            }
            await Assert.ThrowsAsync<PortableBundleValidationException>(() => importer.ImportAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "imported"),
                Path.Combine(directory, "import-work")));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<IReadOnlyList<PortableJobInput>> CreateInputsAsync(
        string directory,
        PortableBundleProfile profile)
    {
        List<PortableJobInput> inputs = [];
        switch (profile)
        {
            case PortableBundleProfile.FullImage:
                inputs.Add(await CreateInputAsync(directory, "source.jpg", "inputs/source.jpg", PortableBundleRoles.SourceImage, [1, 2, 3]));
                break;
            case PortableBundleProfile.ReducedImage:
                inputs.Add(await CreateInputAsync(directory, "reduced.jpg", "inputs/reduced.jpg", PortableBundleRoles.ReducedImage, [4, 5, 6]));
                break;
            case PortableBundleProfile.FaceCrops:
                inputs.Add(await CreateInputAsync(directory, "crop-1.png", "inputs/faces/face-001.png", PortableBundleRoles.FaceCrop, [7, 8]));
                inputs.Add(await CreateInputAsync(directory, "crop-2.png", "inputs/faces/face-002.png", PortableBundleRoles.FaceCrop, [9, 10]));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile));
        }
        return inputs;
    }

    private static async Task<PortableJobInput> CreateInputAsync(
        string directory,
        string fileName,
        string bundlePath,
        string role,
        byte[] bytes)
    {
        string path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        return new PortableJobInput(path, bundlePath, role);
    }

    private static async Task<CatalogueAssetRevision> SeedRevisionAsync(
        SqliteCatalogueDatabase database,
        string directory,
        byte[] bytes,
        Sha256Digest hash,
        DateTimeOffset now)
    {
        string sourceRoot = Path.Combine(directory, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "photo.jpg"), bytes);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        return await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, "photo.jpg", now),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                hash,
                bytes.LongLength,
                now,
                "image/jpeg",
                640,
                480));
    }

    private static async Task<CatalogueReviewPerson> SeedHumanAssignmentAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        string directory,
        DateTimeOffset now)
    {
        string cropPath = Path.Combine(directory, "existing-crop.png");
        byte[] cropBytes = [11, 12, 13];
        await File.WriteAllBytesAsync(cropPath, cropBytes);
        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
        FaceCropId cropId = FaceCropId.New();
        await new SqliteFaceCatalogueRepository(database).SaveInspectionAsync(
            new CatalogueFaceOccurrence(occurrenceId, revisionId, 0, now),
            new CatalogueFaceObservation(
                occurrenceId,
                new ModelId("existing-detector"),
                Digest("existing-detector"u8),
                0.8,
                Box,
                Landmarks,
                now),
            new CatalogueFaceCrop(
                cropId,
                occurrenceId,
                new AlignmentProtocolId("existing-alignment"),
                Digest(cropBytes),
                cropPath,
                112,
                112,
                now),
            new CatalogueFaceEmbedding(
                cropId,
                new ModelId("existing-embedder"),
                Digest("existing-embedder"u8),
                new EmbeddingVector([1f, 0f]),
                now));
        SqliteReviewRepository reviewRepository = new(database);
        CatalogueReviewPerson person = await reviewRepository.CreatePersonAsync("Existing Human Label", now.AddMinutes(1));
        await reviewRepository.AssignAsync(occurrenceId, person.Id, "human:test", now.AddMinutes(2));
        return person;
    }

    private static readonly NormalizedBoundingBox Box = new(0.1, 0.1, 0.5, 0.5);
    private static readonly NormalizedFaceLandmarks Landmarks = new(
        new NormalizedPoint(0.25, 0.25),
        new NormalizedPoint(0.45, 0.25),
        new NormalizedPoint(0.35, 0.35),
        new NormalizedPoint(0.28, 0.48),
        new NormalizedPoint(0.42, 0.48));

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Bundle.Tests", Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingProcessor : IPortableBundleProcessor
    {
        public PortableBundleProfile? ObservedProfile { get; private set; }
        public int ObservedInputCount { get; private set; }

        public async Task<PortableProcessingOutput> ProcessAsync(
            ExtractedPortableJob job,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            ObservedProfile = job.Manifest.Profile;
            ObservedInputCount = job.Manifest.Files.Count;
            Assert.All(job.Manifest.Files, file => Assert.True(File.Exists(job.ResolveFile(file))));
            Directory.CreateDirectory(outputDirectory);
            string cropPath = Path.Combine(outputDirectory, "crop.png");
            await File.WriteAllBytesAsync(cropPath, [137, 80, 78, 71, 1, 2, 3], cancellationToken);
            return new PortableProcessingOutput(
                new ModelId("portable-detector"),
                Digest("portable-detector"u8),
                new ModelId("portable-embedder"),
                Digest("portable-embedder"u8),
                new AlignmentProtocolId("portable-alignment"),
                [new PortableProcessedFace(0, 0.95, Box, Landmarks, cropPath, 112, 112, new float[] { 1f, 0f })],
                new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero));
        }
    }
}
