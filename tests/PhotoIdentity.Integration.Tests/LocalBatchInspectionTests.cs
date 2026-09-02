using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class LocalBatchInspectionTests
{
    [Fact]
    public void Configuration_rejects_output_below_the_source_root()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Assert.Throws<ArgumentException>(() => new LocalBatchConfiguration(
                root,
                Path.Combine(root, "generated"),
                root));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task Coordinator_starts_and_resumes_without_repeating_completed_revisions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "source");
            string outputRoot = Path.Combine(directory, "output");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "a.png"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "b.jpg"), [4, 5, 6]);
            await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "c.png"), [7, 8, 9]);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "notes.txt"), "unsupported");

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            LocalBatchConfiguration configuration = new(sourceRoot, outputRoot, directory);
            RecordingHandler handler = new();
            SqliteProcessingRepository processingRepository = new(database);
            SqliteLocalBatchCatalogueRepository catalogueRepository = new(database);
            LocalBatchCoordinator coordinator = new(
                database,
                catalogueRepository,
                processingRepository,
                processingRepository);

            LocalBatchStartResult started = await coordinator.StartAsync(
                configuration,
                handler,
                new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: 1));

            Assert.Equal(3, started.ScanSummary.SupportedFileCount);
            Assert.Equal(3, started.ScanSummary.NewRevisionCount);
            Assert.Equal(1, started.UnsupportedFileCount);
            Assert.Equal(1, started.ProcessingSummary.SucceededJobs);
            Assert.Equal(2, started.ProcessingSummary.QueuedJobs);
            Assert.Single(handler.ProcessedRevisionIds);
            Assert.Equal(configuration, await coordinator.GetConfigurationAsync(started.RunId));

            ResumableBatchProcessorResult resumed = await coordinator.ResumeAsync(
                started.RunId,
                handler);

            Assert.Equal(ProcessingRunStatus.Completed, resumed.Summary.Status);
            Assert.Equal(3, resumed.Summary.SucceededJobs);
            Assert.Equal(3, handler.ProcessedRevisionIds.Count);
            Assert.Equal(3, handler.ProcessedRevisionIds.Distinct().Count());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Production_handler_persists_faces_and_resumes_from_the_last_face_checkpoint()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "source");
            string outputRoot = Path.Combine(directory, "output");
            Directory.CreateDirectory(sourceRoot);
            string inputPath = Path.Combine(sourceRoot, "photo.png");
            OpenCvPngEncoder encoder = new();
            await using (FileStream stream = File.Create(inputPath))
            {
                await encoder.EncodeAsync(CreateFrame(160, 160), stream, CancellationToken.None);
            }

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteLocalBatchRepository batchRepository = new(database);
            CatalogueSource sourceRecord = await batchRepository.GetOrCreateLocalFolderSourceAsync(
                sourceRoot,
                DateTimeOffset.UtcNow);
            LocalFolderAssetSource source = new(sourceRecord.Id, sourceRoot);
            await new SqliteSourceCatalogueScanner(database).ScanAsync(
                source,
                sourceRecord,
                new SourceScanOptions(),
                DateTimeOffset.UtcNow);
            AssetRevisionId revisionId = Assert.Single(
                await batchRepository.GetCurrentRevisionIdsAsync(sourceRecord.Id));

            LocalBatchConfiguration configuration = new(sourceRoot, outputRoot, directory);
            using LocalInspectionJobHandler handler = new(
                database,
                configuration,
                new OpenCvImageDecoder(),
                encoder,
                new FakeDetector(),
                new OpenCvFaceAligner(),
                new FakeEmbedder());
            ProcessingRunId runId = ProcessingRunId.New();
            RecordingCheckpointWriter checkpointWriter = new();
            ProcessingJobContext first = new(
                runId,
                ProcessingJobId.New(),
                revisionId,
                attempt: 1,
                idempotencyKey: $"test:{runId}:{revisionId}",
                checkpointJson: null);

            await handler.ProcessAsync(first, checkpointWriter, CancellationToken.None);

            Assert.NotNull(checkpointWriter.LatestCheckpoint);
            using (JsonDocument checkpoint = JsonDocument.Parse(checkpointWriter.LatestCheckpoint!))
            {
                Assert.Equal(1, checkpoint.RootElement.GetProperty("completedFaceCount").GetInt32());
                Assert.Equal(1, checkpoint.RootElement.GetProperty("faceCount").GetInt32());
            }

            ProcessingJobContext resumed = new(
                runId,
                ProcessingJobId.New(),
                revisionId,
                attempt: 2,
                idempotencyKey: first.IdempotencyKey,
                checkpointJson: checkpointWriter.LatestCheckpoint);
            await handler.ProcessAsync(resumed, checkpointWriter, CancellationToken.None);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "face_occurrences"));
            Assert.Equal(1, await CountAsync(connection, "face_observations"));
            Assert.Equal(1, await CountAsync(connection, "face_crops"));
            Assert.Equal(1, await CountAsync(connection, "embeddings"));
            Assert.True(File.Exists(Path.Combine(
                outputRoot,
                "runs",
                runId.ToString(),
                "assets",
                revisionId.ToString(),
                "faces",
                "face-001",
                "aligned.png")));
            Assert.True(File.Exists(Path.Combine(
                outputRoot,
                "runs",
                runId.ToString(),
                "assets",
                revisionId.ToString(),
                "result.json")));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static ImageFrame CreateFrame(int width, int height)
    {
        int stride = width * 3;
        byte[] data = new byte[stride * height];
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

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingHandler : IProcessingJobHandler
    {
        public List<AssetRevisionId> ProcessedRevisionIds { get; } = [];

        public Task ProcessAsync(
            ProcessingJobContext context,
            IProcessingCheckpointWriter checkpointWriter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessedRevisionIds.Add(context.AssetRevisionId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCheckpointWriter : IProcessingCheckpointWriter
    {
        public string? LatestCheckpoint { get; private set; }

        public Task WriteAsync(string checkpointJson, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LatestCheckpoint = checkpointJson;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDetector : IFaceDetector
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-yunet"),
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
            IReadOnlyList<DetectedFaceCandidate> faces =
            [
                new DetectedFaceCandidate(
                    new NormalizedBoundingBox(0.2, 0.15, 0.6, 0.72),
                    new NormalizedFaceLandmarks(
                        LeftEye: new NormalizedPoint(0.62, 0.38),
                        RightEye: new NormalizedPoint(0.38, 0.38),
                        Nose: new NormalizedPoint(0.5, 0.52),
                        MouthLeft: new NormalizedPoint(0.6, 0.68),
                        MouthRight: new NormalizedPoint(0.4, 0.68)),
                    confidence: 0.97),
            ];
            return Task.FromResult(faces);
        }
    }

    private sealed class FakeEmbedder : IFaceEmbedder
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-sface"),
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
            float[] values = Enumerable.Range(1, 128).Select(value => (float)value).ToArray();
            return Task.FromResult(new EmbeddingVector(values).Normalize());
        }
    }
}
