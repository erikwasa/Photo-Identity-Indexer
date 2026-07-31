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

public sealed class SecondModelCoexistenceTests
{
    private static readonly ModelId BaselineModelId = new("sface-2021dec-fp32");
    private static readonly Sha256Digest BaselineModelHash = new(new string('b', 64));
    private static readonly ModelId CandidateModelId = new("sface-2021dec-int8");
    private static readonly Sha256Digest CandidateModelHash = new(new string('c', 64));

    [Fact]
    public async Task Same_revision_keeps_human_review_and_both_exact_model_embeddings()
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
            AssetRevisionId revisionId = await CatalogueRevisionAsync(database, sourceRoot);
            LocalBatchConfiguration baselineConfiguration = new(
                sourceRoot,
                outputRoot,
                directory,
                detectorModelId: LocalBatchConfiguration.DefaultDetectorModelId,
                embedderModelId: BaselineModelId.ToString());

            await ProcessAsync(
                database,
                baselineConfiguration,
                encoder,
                new FakeEmbedder(BaselineModelId, BaselineModelHash, vectorOffset: 0),
                revisionId);

            SqliteFaceCatalogueRepository faceRepository = new(database);
            CatalogueFaceOccurrence occurrence = Assert.Single(
                await faceRepository.GetOccurrencesAsync(revisionId));
            DateTimeOffset reviewedAt = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson person = await reviewRepository.CreatePersonAsync(
                "Confirmed person",
                reviewedAt);
            await reviewRepository.AssignAsync(
                occurrence.Id,
                person.Id,
                "human:test",
                reviewedAt.AddMinutes(1),
                "Assignment must survive candidate processing.");

            string legacyConfigurationJson = $$"""
                {
                  "sourceRoot": {{System.Text.Json.JsonSerializer.Serialize(sourceRoot)}},
                  "outputRoot": {{System.Text.Json.JsonSerializer.Serialize(outputRoot)}},
                  "repositoryRoot": {{System.Text.Json.JsonSerializer.Serialize(directory)}},
                  "modelDirectory": null,
                  "recursive": true,
                  "confidenceThreshold": 0.9,
                  "paddingRatio": 0.25
                }
                """;
            LocalBatchConfiguration legacyConfiguration = LocalBatchConfiguration.FromJson(
                legacyConfigurationJson);
            Assert.Equal(LocalBatchConfiguration.DefaultDetectorModelId, legacyConfiguration.DetectorModelId);
            Assert.Equal(LocalBatchConfiguration.DefaultEmbedderModelId, legacyConfiguration.EmbedderModelId);

            LocalBatchConfiguration candidateConfiguration = new(
                sourceRoot,
                outputRoot,
                directory,
                detectorModelId: LocalBatchConfiguration.DefaultDetectorModelId,
                embedderModelId: CandidateModelId.ToString());
            LocalBatchConfiguration roundTripped = LocalBatchConfiguration.FromJson(
                candidateConfiguration.ToJson());
            Assert.Equal(CandidateModelId.ToString(), roundTripped.EmbedderModelId);

            await ProcessAsync(
                database,
                candidateConfiguration,
                encoder,
                new FakeEmbedder(CandidateModelId, CandidateModelHash, vectorOffset: 7),
                revisionId);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "face_occurrences"));
            Assert.Equal(1, await CountAsync(connection, "face_observations"));
            Assert.Equal(1, await CountAsync(connection, "face_crops"));
            Assert.Equal(2, await CountAsync(connection, "embeddings"));
            Assert.Equal(1, await CountAsync(connection, "person_labels"));
            Assert.Equal(1, await CountAsync(connection, "review_actions"));

            IReadOnlyList<(string ModelId, string ModelHash)> persistedModels =
                await ReadEmbeddingModelsAsync(connection);
            Assert.Equal(
                [
                    (BaselineModelId.ToString(), BaselineModelHash.ToString()),
                    (CandidateModelId.ToString(), CandidateModelHash.ToString()),
                ],
                persistedModels);

            IReadOnlyList<CatalogueHumanLabel> labels =
                await new SqliteIdentityCatalogueRepository(database)
                    .GetHumanLabelsAsync(occurrence.Id);
            CatalogueHumanLabel label = Assert.Single(labels);
            Assert.Equal(person.Id, label.PersonId);
            Assert.Equal("manual", label.LabelKind);

            FaceCropId cropId = await ReadOnlyCropIdAsync(connection);
            Assert.NotNull(await faceRepository.GetEmbeddingAsync(
                cropId,
                BaselineModelId,
                BaselineModelHash));
            Assert.NotNull(await faceRepository.GetEmbeddingAsync(
                cropId,
                CandidateModelId,
                CandidateModelHash));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> CatalogueRevisionAsync(
        SqliteCatalogueDatabase database,
        string sourceRoot)
    {
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
        return Assert.Single(await batchRepository.GetCurrentRevisionIdsAsync(sourceRecord.Id));
    }

    private static async Task ProcessAsync(
        SqliteCatalogueDatabase database,
        LocalBatchConfiguration configuration,
        OpenCvPngEncoder encoder,
        IFaceEmbedder embedder,
        AssetRevisionId revisionId)
    {
        using LocalInspectionJobHandler handler = new(
            database,
            configuration,
            new OpenCvImageDecoder(),
            encoder,
            new FakeDetector(),
            new OpenCvFaceAligner(),
            embedder);
        ProcessingRunId runId = ProcessingRunId.New();
        await handler.ProcessAsync(
            new ProcessingJobContext(
                runId,
                ProcessingJobId.New(),
                revisionId,
                attempt: 1,
                idempotencyKey: $"test:{runId}:{revisionId}",
                checkpointJson: null),
            new RecordingCheckpointWriter(),
            CancellationToken.None);
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

    private static async Task<IReadOnlyList<(string ModelId, string ModelHash)>> ReadEmbeddingModelsAsync(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_id, model_hash
            FROM embeddings
            ORDER BY model_id, model_hash;
            """;
        List<(string ModelId, string ModelHash)> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    private static async Task<FaceCropId> ReadOnlyCropIdAsync(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM face_crops;";
        object? value = await command.ExecuteScalarAsync();
        return FaceCropId.From(Guid.Parse(Assert.IsType<string>(value)));
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

    private sealed class RecordingCheckpointWriter : IProcessingCheckpointWriter
    {
        public Task WriteAsync(string checkpointJson, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDetector : IFaceDetector
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId(LocalBatchConfiguration.DefaultDetectorModelId),
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
        private readonly int _vectorOffset;

        public FakeEmbedder(ModelId modelId, Sha256Digest modelHash, int vectorOffset)
        {
            _vectorOffset = vectorOffset;
            Descriptor = new ModelDescriptor(
                modelId,
                ModelRole.FaceEmbedding,
                ModelFormat.Onnx,
                modelHash,
                new ImageSize(112, 112),
                "fake-runtime",
                "Apache-2.0",
                "test",
                outputDimensions: 128,
                distanceMetric: DistanceMetric.Cosine,
                alignmentProtocol: OpenCvFaceAligner.SFaceFivePointV1);
        }

        public ModelDescriptor Descriptor { get; }

        public Task<EmbeddingVector> EmbedAsync(
            AlignedFace face,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] values = Enumerable.Range(1, 128)
                .Select(value => (float)(value + _vectorOffset))
                .ToArray();
            return Task.FromResult(new EmbeddingVector(values).Normalize());
        }
    }
}
