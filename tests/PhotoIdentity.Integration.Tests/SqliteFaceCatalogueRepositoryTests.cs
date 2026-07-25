using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteFaceCatalogueRepositoryTests
{
    [Fact]
    public async Task Save_inspection_round_trips_complete_face_result()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = await SeedRevisionAsync(database);
            SqliteFaceCatalogueRepository repository = new(database);
            CatalogueFaceInspection inspection = CreateInspection(revision.Id);

            CatalogueFaceInspection persisted = await repository.SaveInspectionAsync(
                inspection.Occurrence,
                inspection.Observation,
                inspection.Crop,
                inspection.Embedding);

            Assert.Equal(inspection.Occurrence, persisted.Occurrence);
            Assert.Equal(inspection.Observation, persisted.Observation);
            Assert.Equal(inspection.Crop, persisted.Crop);
            Assert.Equal(inspection.Embedding.FaceCropId, persisted.Embedding.FaceCropId);
            Assert.Equal(inspection.Embedding.ModelId, persisted.Embedding.ModelId);
            Assert.Equal(inspection.Embedding.ModelHash, persisted.Embedding.ModelHash);
            Assert.Equal(inspection.Embedding.CreatedAtUtc, persisted.Embedding.CreatedAtUtc);
            Assert.Equal(inspection.Embedding.Vector.ToArray(), persisted.Embedding.Vector.ToArray());

            IReadOnlyList<CatalogueFaceOccurrence> occurrences = await repository.GetOccurrencesAsync(revision.Id);
            Assert.Equal([inspection.Occurrence], occurrences);

            CatalogueFaceObservation observation = Assert.IsType<CatalogueFaceObservation>(
                await repository.GetObservationAsync(
                    inspection.Occurrence.Id,
                    inspection.Observation.DetectorModelId,
                    inspection.Observation.DetectorModelHash));
            Assert.Equal(inspection.Observation, observation);

            CatalogueFaceCrop crop = Assert.IsType<CatalogueFaceCrop>(
                await repository.FindCropAsync(
                    inspection.Occurrence.Id,
                    inspection.Crop.Protocol,
                    inspection.Crop.ContentHash));
            Assert.Equal(inspection.Crop, crop);

            CatalogueFaceEmbedding embedding = Assert.IsType<CatalogueFaceEmbedding>(
                await repository.GetEmbeddingAsync(
                    inspection.Crop.Id,
                    inspection.Embedding.ModelId,
                    inspection.Embedding.ModelHash));
            Assert.Equal(inspection.Embedding.Vector.ToArray(), embedding.Vector.ToArray());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_inspection_is_idempotent_and_refreshes_mutable_model_output()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = await SeedRevisionAsync(database);
            SqliteFaceCatalogueRepository repository = new(database);
            CatalogueFaceInspection first = CreateInspection(revision.Id);
            CatalogueFaceInspection initiallyPersisted = await repository.SaveInspectionAsync(
                first.Occurrence,
                first.Observation,
                first.Crop,
                first.Embedding);

            DateTimeOffset rerunAt = first.Observation.ObservedAtUtc.AddMinutes(5);
            FaceOccurrenceId duplicateOccurrenceId = FaceOccurrenceId.New();
            FaceCropId duplicateCropId = FaceCropId.New();
            CatalogueFaceInspection rerun = new(
                new CatalogueFaceOccurrence(
                    duplicateOccurrenceId,
                    revision.Id,
                    first.Occurrence.Ordinal,
                    rerunAt),
                new CatalogueFaceObservation(
                    duplicateOccurrenceId,
                    first.Observation.DetectorModelId,
                    first.Observation.DetectorModelHash,
                    0.97,
                    new NormalizedBoundingBox(0.2, 0.15, 0.35, 0.45),
                    CreateLandmarks(offset: 0.02),
                    rerunAt),
                new CatalogueFaceCrop(
                    duplicateCropId,
                    duplicateOccurrenceId,
                    first.Crop.Protocol,
                    first.Crop.ContentHash,
                    "faces/moved/0001.png",
                    first.Crop.Width,
                    first.Crop.Height,
                    rerunAt),
                new CatalogueFaceEmbedding(
                    duplicateCropId,
                    first.Embedding.ModelId,
                    first.Embedding.ModelHash,
                    new EmbeddingVector(new float[] { 4, 3, 2, 1 }),
                    rerunAt));

            CatalogueFaceInspection persistedRerun = await repository.SaveInspectionAsync(
                rerun.Occurrence,
                rerun.Observation,
                rerun.Crop,
                rerun.Embedding);

            Assert.Equal(initiallyPersisted.Occurrence.Id, persistedRerun.Occurrence.Id);
            Assert.Equal(initiallyPersisted.Occurrence.CreatedAtUtc, persistedRerun.Occurrence.CreatedAtUtc);
            Assert.Equal(initiallyPersisted.Crop.Id, persistedRerun.Crop.Id);
            Assert.Equal(rerun.Observation.Confidence, persistedRerun.Observation.Confidence);
            Assert.Equal(rerun.Observation.BoundingBox, persistedRerun.Observation.BoundingBox);
            Assert.Equal(rerun.Observation.Landmarks, persistedRerun.Observation.Landmarks);
            Assert.Equal(rerun.Observation.ObservedAtUtc, persistedRerun.Observation.ObservedAtUtc);
            Assert.Equal(rerun.Crop.StoragePath, persistedRerun.Crop.StoragePath);
            Assert.Equal(first.Embedding.Vector.ToArray(), persistedRerun.Embedding.Vector.ToArray());
            Assert.Equal(first.Embedding.CreatedAtUtc, persistedRerun.Embedding.CreatedAtUtc);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "face_occurrences"));
            Assert.Equal(1, await CountAsync(connection, "face_observations"));
            Assert.Equal(1, await CountAsync(connection, "face_crops"));
            Assert.Equal(1, await CountAsync(connection, "embeddings"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_inspection_rolls_back_the_complete_result_when_revision_is_missing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteFaceCatalogueRepository repository = new(database);
            CatalogueFaceInspection inspection = CreateInspection(AssetRevisionId.New());

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => repository.SaveInspectionAsync(
                    inspection.Occurrence,
                    inspection.Observation,
                    inspection.Crop,
                    inspection.Embedding));

            Assert.Equal(19, exception.SqliteErrorCode);
            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await CountAsync(connection, "face_occurrences"));
            Assert.Equal(0, await CountAsync(connection, "face_observations"));
            Assert.Equal(0, await CountAsync(connection, "face_crops"));
            Assert.Equal(0, await CountAsync(connection, "embeddings"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_inspection_rejects_mismatched_graph_before_writing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = await SeedRevisionAsync(database);
            SqliteFaceCatalogueRepository repository = new(database);
            CatalogueFaceInspection inspection = CreateInspection(revision.Id);
            CatalogueFaceObservation mismatched = new(
                FaceOccurrenceId.New(),
                inspection.Observation.DetectorModelId,
                inspection.Observation.DetectorModelHash,
                inspection.Observation.Confidence,
                inspection.Observation.BoundingBox,
                inspection.Observation.Landmarks,
                inspection.Observation.ObservedAtUtc);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.SaveInspectionAsync(
                    inspection.Occurrence,
                    mismatched,
                    inspection.Crop,
                    inspection.Embedding));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await CountAsync(connection, "face_occurrences"));
            Assert.Equal(0, await CountAsync(connection, "face_observations"));
            Assert.Equal(0, await CountAsync(connection, "face_crops"));
            Assert.Equal(0, await CountAsync(connection, "embeddings"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CatalogueAssetRevision> SeedRevisionAsync(SqliteCatalogueDatabase database)
    {
        DateTimeOffset now = new(2026, 7, 25, 21, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, "photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string('a', 64)),
            1234,
            now,
            "image/jpeg",
            640,
            480);

        SqliteAssetCatalogueRepository repository = new(database);
        return await repository.SaveRevisionAsync(source, asset, revision);
    }

    private static CatalogueFaceInspection CreateInspection(AssetRevisionId revisionId)
    {
        DateTimeOffset now = new(2026, 7, 25, 21, 5, 0, TimeSpan.Zero);
        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
        FaceCropId cropId = FaceCropId.New();
        CatalogueFaceOccurrence occurrence = new(occurrenceId, revisionId, 0, now);
        CatalogueFaceObservation observation = new(
            occurrenceId,
            new ModelId("yunet"),
            new Sha256Digest(new string('b', 64)),
            0.91,
            new NormalizedBoundingBox(0.1, 0.1, 0.4, 0.5),
            CreateLandmarks(offset: 0),
            now);
        CatalogueFaceCrop crop = new(
            cropId,
            occurrenceId,
            new AlignmentProtocolId("sface-five-point-v1"),
            new Sha256Digest(new string('c', 64)),
            "faces/0001.png",
            112,
            112,
            now);
        CatalogueFaceEmbedding embedding = new(
            cropId,
            new ModelId("sface"),
            new Sha256Digest(new string('d', 64)),
            new EmbeddingVector(new float[] { 1, 2, 3, 4 }),
            now);
        return new CatalogueFaceInspection(occurrence, observation, crop, embedding);
    }

    private static NormalizedFaceLandmarks CreateLandmarks(double offset) =>
        new(
            new NormalizedPoint(0.2 + offset, 0.25),
            new NormalizedPoint(0.4 + offset, 0.25),
            new NormalizedPoint(0.3 + offset, 0.35),
            new NormalizedPoint(0.24 + offset, 0.48),
            new NormalizedPoint(0.36 + offset, 0.48));

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
}
