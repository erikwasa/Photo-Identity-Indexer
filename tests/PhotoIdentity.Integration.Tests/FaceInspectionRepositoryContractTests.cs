using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class FaceInspectionRepositoryContractTests
{
    [Fact]
    public async Task Sqlite_adapter_preserves_atomic_face_inspection_write_through_contract()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(
                SourceId.New(),
                "local-folder",
                Path.Combine(directory, "source"),
                now);
            CatalogueAsset asset = new(
                AssetId.New(),
                source.Id,
                "photo.jpg",
                now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                42,
                now,
                "image/jpeg");
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
            FaceCropId cropId = FaceCropId.New();
            ModelId detectorModelId = new("contract-detector");
            Sha256Digest detectorHash = new(new string('b', 64));
            ModelId embeddingModelId = new("contract-embedder");
            Sha256Digest embeddingHash = new(new string('c', 64));
            AlignmentProtocolId protocol = new("contract-alignment");
            Sha256Digest cropHash = new(new string('d', 64));
            EmbeddingVector vector = new(new float[] { 1, 2, 3, 4 });

            FaceInspectionWrite inspection = new(
                occurrenceId,
                persistedRevision.Id,
                0,
                now.AddMinutes(1),
                detectorModelId,
                detectorHash,
                0.95,
                new NormalizedBoundingBox(0.1, 0.2, 0.3, 0.4),
                new NormalizedFaceLandmarks(
                    LeftEye: new NormalizedPoint(0.2, 0.3),
                    RightEye: new NormalizedPoint(0.4, 0.3),
                    Nose: new NormalizedPoint(0.3, 0.4),
                    MouthLeft: new NormalizedPoint(0.24, 0.5),
                    MouthRight: new NormalizedPoint(0.36, 0.5)),
                cropId,
                protocol,
                cropHash,
                "runs/test/faces/face-001/aligned.png",
                112,
                112,
                embeddingModelId,
                embeddingHash,
                vector);

            IFaceInspectionRepository repository = new SqliteFaceCatalogueRepository(database);
            await repository.SaveInspectionAsync(inspection);

            SqliteFaceCatalogueRepository sqlite = new(database);
            CatalogueFaceOccurrence occurrence = Assert.Single(
                await sqlite.GetOccurrencesAsync(persistedRevision.Id));
            CatalogueFaceObservation observation = Assert.IsType<CatalogueFaceObservation>(
                await sqlite.GetObservationAsync(occurrence.Id, detectorModelId, detectorHash));
            CatalogueFaceCrop crop = Assert.IsType<CatalogueFaceCrop>(
                await sqlite.FindCropAsync(occurrence.Id, protocol, cropHash));
            CatalogueFaceEmbedding embedding = Assert.IsType<CatalogueFaceEmbedding>(
                await sqlite.GetEmbeddingAsync(crop.Id, embeddingModelId, embeddingHash));

            Assert.Equal(occurrenceId, occurrence.Id);
            Assert.Equal(inspection.Confidence, observation.Confidence);
            Assert.Equal(inspection.BoundingBox, observation.BoundingBox);
            Assert.Equal(inspection.Landmarks, observation.Landmarks);
            Assert.Equal(inspection.CropStoragePath, crop.StoragePath);
            Assert.Equal(inspection.CropWidth, crop.Width);
            Assert.Equal(inspection.CropHeight, crop.Height);
            Assert.Equal(vector.ToArray(), embedding.Vector.ToArray());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
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

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
