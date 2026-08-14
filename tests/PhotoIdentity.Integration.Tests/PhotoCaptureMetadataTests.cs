using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoCaptureMetadataTests
{
    [Fact]
    public void Camera_time_remains_unspecified()
    {
        PhotoCaptureMetadata metadata = new(new DateTime(2025, 5, 10, 13, 45, 22, DateTimeKind.Local));
        Assert.Equal(DateTimeKind.Unspecified, metadata.TakenAtLocal!.Value.Kind);
    }

    [Fact]
    public async Task Metadata_round_trips_and_empty_metadata_marks_revision_inspected()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhotoIdentity.MetadataTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(root, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            DateTimeOffset observed = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            CatalogueSource source = new(sourceId, "local-folder", root, observed);
            CatalogueAsset asset = new(assetId, sourceId, "photo.jpg", observed);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(), assetId, new Sha256Digest(new string('a', 64)), 1234,
                observed, "image/jpeg", 640, 480);
            await repository.SaveRevisionAsync(source, asset, revision);

            Assert.Single(await repository.GetPhotoMetadataBackfillCandidatesAsync());
            PhotoCaptureMetadata expected = new(
                new DateTime(2025, 5, 10, 13, 45, 22), TimeSpan.FromHours(2), 59.3293, 18.0686);
            await repository.SavePhotoMetadataAsync(revision.Id, expected, observed);
            PhotoCaptureMetadata actual = Assert.IsType<PhotoCaptureMetadata>(
                await repository.GetPhotoMetadataAsync(revision.Id));
            Assert.Equal(expected, actual);
            Assert.Equal(DateTimeKind.Unspecified, actual.TakenAtLocal!.Value.Kind);
            Assert.Empty(await repository.GetPhotoMetadataBackfillCandidatesAsync());

            await repository.SavePhotoMetadataAsync(revision.Id, new PhotoCaptureMetadata(), observed);
            Assert.False((await repository.GetPhotoMetadataAsync(revision.Id))!.HasAnyValue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
