using System.Security.Cryptography;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Transfer.Bundles;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PortableBundleCommandTests
{
    [Fact]
    public async Task Production_processor_uses_signed_job_configuration_for_image_processing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string inputPath = Path.Combine(directory, "source.png");
            await WritePngAsync(inputPath, CreateFrame(160, 160));
            string jobPath = Path.Combine(directory, "job.photoid-job");
            string resultPath = Path.Combine(directory, "result.photoid-result");
            double observedThreshold = -1;
            DateTimeOffset completedAt = new(2026, 7, 26, 16, 0, 0, TimeSpan.Zero);

            await PortableBundleArchive.CreateJobAsync(
                jobPath,
                new PortableJobBundleRequest(
                    AssetRevisionId.New(),
                    await DigestFileAsync(inputPath),
                    PortableBundleProfile.FullImage,
                    new PortableRecognitionConfiguration(0.73).ToJson(),
                    [new PortableJobInput(inputPath, "inputs/source.png", PortableBundleRoles.SourceImage)],
                    completedAt.AddMinutes(-1)));

            PortableRecognitionProcessor processor = new(
                new OpenCvImageDecoder(),
                new OpenCvPngEncoder(),
                new OpenCvFaceAligner(),
                threshold =>
                {
                    observedThreshold = threshold;
                    return new FakeDetector();
                },
                () => new FakeEmbedder(),
                new FixedTimeProvider(completedAt));
            PortableResultManifest result = await new PortableBundleWorker(processor).ProcessAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "work"));

            Assert.Equal(0.73, observedThreshold, precision: 8);
            Assert.Equal(FakeDetector.DetectorId.ToString(), result.DetectorModelId);
            Assert.Equal(FakeEmbedder.EmbedderId.ToString(), result.EmbedderModelId);
            Assert.Equal(completedAt, result.CompletedAtUtc);
            PortableFaceResult face = Assert.Single(result.Faces);
            Assert.Equal(0, face.Ordinal);
            Assert.Equal(0.97, face.Confidence, precision: 8);
            Assert.Equal(112, face.CropWidth);
            Assert.Equal(112, face.CropHeight);

            ExtractedPortableResult extracted = await PortableBundleArchive.ExtractResultAsync(
                resultPath,
                Path.Combine(directory, "extracted"));
            Assert.True(File.Exists(extracted.ResolveFile(Assert.Single(extracted.Manifest.Files))));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Production_processor_records_crop_input_provenance_without_running_a_detector()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string cropPath = Path.Combine(directory, "aligned.png");
            await WritePngAsync(cropPath, CreateFrame(112, 112));
            string jobPath = Path.Combine(directory, "crop-job.photoid-job");
            string resultPath = Path.Combine(directory, "crop-result.photoid-result");
            int detectorFactoryCalls = 0;

            await PortableBundleArchive.CreateJobAsync(
                jobPath,
                new PortableJobBundleRequest(
                    AssetRevisionId.New(),
                    Digest("canonical-source"u8),
                    PortableBundleProfile.FaceCrops,
                    new PortableRecognitionConfiguration().ToJson(),
                    [new PortableJobInput(cropPath, "inputs/faces/face-001.png", PortableBundleRoles.FaceCrop)],
                    DateTimeOffset.UtcNow));

            PortableRecognitionProcessor processor = new(
                new OpenCvImageDecoder(),
                new OpenCvPngEncoder(),
                new OpenCvFaceAligner(),
                _ =>
                {
                    detectorFactoryCalls++;
                    return new FakeDetector();
                },
                () => new FakeEmbedder());
            PortableResultManifest result = await new PortableBundleWorker(processor).ProcessAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "crop-work"));

            Assert.Equal(0, detectorFactoryCalls);
            Assert.Equal("portable-aligned-face-crop-v1", result.DetectorModelId);
            PortableFaceResult face = Assert.Single(result.Faces);
            Assert.Equal(1, face.Confidence);
            Assert.Equal(0, face.BoundingBox.X);
            Assert.Equal(0, face.BoundingBox.Y);
            Assert.Equal(1, face.BoundingBox.Width);
            Assert.Equal(1, face.BoundingBox.Height);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Exporter_creates_full_and_reduced_jobs_and_rejects_changed_source_content()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            DateTimeOffset now = new(2026, 7, 26, 16, 30, 0, TimeSpan.Zero);
            (CatalogueAssetRevision revision, string sourcePath) = await SeedRevisionAsync(
                database,
                directory,
                CreateFrame(640, 320),
                now);
            PortableBundleExportCoordinator exporter = new(
                database,
                timeProvider: new FixedTimeProvider(now.AddMinutes(1)));

            string fullPath = Path.Combine(directory, "full.photoid-job");
            PortableBundleExportResult full = await exporter.ExportAsync(
                new PortableBundleExportOptions(
                    revision.Id,
                    PortableBundleProfile.FullImage,
                    fullPath,
                    Path.Combine(directory, "export-work"),
                    ConfidenceThreshold: 0.81));
            Assert.Equal(PortableBundleProfile.FullImage, full.Manifest.Profile);
            Assert.Equal(revision.ContentHash.ToString(), full.Manifest.SourceContentSha256);
            Assert.Equal(0.81, PortableRecognitionConfiguration.FromJson(full.Manifest.ConfigurationJson).ConfidenceThreshold, 8);
            Assert.Equal(PortableBundleRoles.SourceImage, Assert.Single(full.Manifest.Files).Role);

            string reducedPath = Path.Combine(directory, "reduced.photoid-job");
            PortableBundleExportResult reduced = await exporter.ExportAsync(
                new PortableBundleExportOptions(
                    revision.Id,
                    PortableBundleProfile.ReducedImage,
                    reducedPath,
                    Path.Combine(directory, "export-work"),
                    ReducedMaximumWidth: 160,
                    ReducedMaximumHeight: 160));
            ExtractedPortableJob extracted = await PortableBundleArchive.ExtractJobAsync(
                reducedPath,
                Path.Combine(directory, "reduced-extracted"));
            PortableBundleFile reducedInput = Assert.Single(extracted.Manifest.Files);
            Assert.Equal(PortableBundleRoles.ReducedImage, reducedInput.Role);
            ImageFrame reducedFrame;
            await using (FileStream stream = File.OpenRead(extracted.ResolveFile(reducedInput)))
            {
                reducedFrame = await new OpenCvImageDecoder().DecodeAsync(stream, new DecodeOptions(), CancellationToken.None);
            }
            Assert.True(reducedFrame.Size.Width <= 160);
            Assert.True(reducedFrame.Size.Height <= 160);

            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
            await Assert.ThrowsAsync<PortableBundleValidationException>(() => exporter.ExportAsync(
                new PortableBundleExportOptions(
                    revision.Id,
                    PortableBundleProfile.FullImage,
                    Path.Combine(directory, "stale.photoid-job"),
                    Path.Combine(directory, "export-work"))));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Bundle_cli_exports_and_imports_a_verified_result()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            (CatalogueAssetRevision revision, _) = await SeedRevisionAsync(
                database,
                directory,
                CreateFrame(180, 180),
                DateTimeOffset.UtcNow);
            string jobPath = Path.Combine(directory, "cli.photoid-job");
            StringWriter exportOutput = new();
            StringWriter exportError = new();

            int exportExit = await Program.RunAsync(
                [
                    "bundle", "export",
                    "--database", databasePath,
                    "--revision", revision.Id.ToString(),
                    "--job", jobPath,
                    "--confidence", "0.77",
                    "--work", Path.Combine(directory, "cli-export-work"),
                ],
                exportOutput,
                exportError);
            Assert.Equal(0, exportExit);
            Assert.Equal(string.Empty, exportError.ToString());
            Assert.True(File.Exists(jobPath));
            Assert.DoesNotContain("source", exportOutput.ToString(), StringComparison.OrdinalIgnoreCase);

            string resultPath = Path.Combine(directory, "cli.photoid-result");
            await new PortableBundleWorker(new FakeResultProcessor()).ProcessAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "fake-worker"));
            StringWriter importOutput = new();
            StringWriter importError = new();
            int importExit = await Program.RunAsync(
                [
                    "bundle", "import",
                    "--database", databasePath,
                    "--job", jobPath,
                    "--result", resultPath,
                    "--output", Path.Combine(directory, "imported"),
                    "--work", Path.Combine(directory, "cli-import-work"),
                ],
                importOutput,
                importError);

            Assert.Equal(0, importExit);
            Assert.Equal(string.Empty, importError.ToString());
            Assert.Contains("imported-faces: 1", importOutput.ToString(), StringComparison.Ordinal);
            Assert.Single(await new SqliteFaceCatalogueRepository(database).GetOccurrencesAsync(revision.Id));

            StringWriter invalidOutput = new();
            StringWriter invalidError = new();
            int invalidExit = await Program.RunAsync(
                ["bundle", "process", "--job", jobPath, "--result", resultPath, "--confidence", "0.2"],
                invalidOutput,
                invalidError);
            Assert.Equal(2, invalidExit);
            Assert.Contains("accepts only", invalidError.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<(CatalogueAssetRevision Revision, string SourcePath)> SeedRevisionAsync(
        SqliteCatalogueDatabase database,
        string directory,
        ImageFrame frame,
        DateTimeOffset now)
    {
        await database.InitializeAsync();
        string sourceRoot = Path.Combine(directory, $"source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        string sourcePath = Path.Combine(sourceRoot, "photo.png");
        await WritePngAsync(sourcePath, frame);
        FileInfo info = new(sourcePath);
        Sha256Digest hash = await DigestFileAsync(sourcePath);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueAssetRevision revision = await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, "photo.png", now),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                hash,
                info.Length,
                now,
                "image/png",
                frame.Size.Width,
                frame.Size.Height));
        return (revision, sourcePath);
    }

    private static async Task WritePngAsync(string path, ImageFrame frame)
    {
        await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await new OpenCvPngEncoder().EncodeAsync(frame, stream, CancellationToken.None);
    }

    private static ImageFrame CreateFrame(int width, int height)
    {
        int stride = checked(width * 3);
        byte[] data = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 3);
                data[offset] = (byte)((x * 3 + y) % 256);
                data[offset + 1] = (byte)((x + y * 5) % 256);
                data[offset + 2] = (byte)((x * 7 + y * 2) % 256);
            }
        }
        return new ImageFrame(new ImageSize(width, height), PixelFormat.Bgr24, stride, data);
    }

    private static async Task<Sha256Digest> DigestFileAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly NormalizedBoundingBox Box = new(0.2, 0.15, 0.6, 0.72);
    private static readonly NormalizedFaceLandmarks Landmarks = new(
        LeftEye: new NormalizedPoint(0.62, 0.38),
        RightEye: new NormalizedPoint(0.38, 0.38),
        Nose: new NormalizedPoint(0.5, 0.52),
        MouthLeft: new NormalizedPoint(0.6, 0.68),
        MouthRight: new NormalizedPoint(0.4, 0.68));

    private sealed class FakeDetector : IFaceDetector
    {
        public static ModelId DetectorId { get; } = new("fake-portable-yunet");

        public ModelDescriptor Descriptor { get; } = new(
            DetectorId,
            ModelRole.FaceDetection,
            ModelFormat.Onnx,
            new Sha256Digest(new string('a', 64)),
            new ImageSize(640, 640),
            "fake-runtime",
            "Apache-2.0",
            "test");

        public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
            ImageFrame image,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DetectedFaceCandidate> result =
            [
                new DetectedFaceCandidate(Box, Landmarks, 0.97),
            ];
            return Task.FromResult(result);
        }
    }

    private sealed class FakeEmbedder : IFaceEmbedder
    {
        public static ModelId EmbedderId { get; } = new("fake-portable-sface");

        public ModelDescriptor Descriptor { get; } = new(
            EmbedderId,
            ModelRole.FaceEmbedding,
            ModelFormat.Onnx,
            new Sha256Digest(new string('b', 64)),
            new ImageSize(112, 112),
            "fake-runtime",
            "Apache-2.0",
            "test",
            outputDimensions: 128,
            distanceMetric: DistanceMetric.Cosine,
            alignmentProtocol: OpenCvFaceAligner.SFaceFivePointV1);

        public Task<EmbeddingVector> EmbedAsync(
            AlignedFace face,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Descriptor.InputSize, face.Image.Size);
            Assert.Equal(Descriptor.AlignmentProtocol, face.Protocol);
            float[] values = Enumerable.Range(1, 128).Select(value => (float)value).ToArray();
            return Task.FromResult(new EmbeddingVector(values).Normalize());
        }
    }

    private sealed class FakeResultProcessor : IPortableBundleProcessor
    {
        public async Task<PortableProcessingOutput> ProcessAsync(
            ExtractedPortableJob job,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            string cropPath = Path.Combine(outputDirectory, "crop.png");
            await WritePngAsync(cropPath, CreateFrame(112, 112));
            return new PortableProcessingOutput(
                FakeDetector.DetectorId,
                new Sha256Digest(new string('a', 64)),
                FakeEmbedder.EmbedderId,
                new Sha256Digest(new string('b', 64)),
                OpenCvFaceAligner.SFaceFivePointV1,
                [new PortableProcessedFace(0, 0.97, Box, Landmarks, cropPath, 112, 112, new float[] { 1f, 0f })],
                DateTimeOffset.UtcNow);
        }
    }

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
