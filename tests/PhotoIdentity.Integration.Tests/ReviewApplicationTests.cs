using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewApplicationTests
{
    [Fact]
    public async Task Review_actions_persist_and_undo_restores_the_previous_state()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFace seeded = await SeedReviewFaceAsync(database, directory);
            SqliteReviewRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
            CatalogueReviewPerson person = await repository.CreatePersonAsync("Ada Lovelace", now);

            await repository.AssignAsync(seeded.Id, person.Id, "human:test", now.AddMinutes(1), "Confirmed manually.");
            CatalogueReviewFace assigned = Assert.IsType<CatalogueReviewFace>(await repository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Assigned, assigned.State);
            Assert.Equal(person, assigned.Person);

            await repository.RejectAsync(seeded.Id, "human:test", now.AddMinutes(2), "Temporary correction.");

            SqliteCatalogueDatabase restartedDatabase = new(databasePath);
            await restartedDatabase.InitializeAsync();
            SqliteReviewRepository restartedRepository = new(restartedDatabase);
            CatalogueReviewFace rejected = Assert.IsType<CatalogueReviewFace>(
                await restartedRepository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Rejected, rejected.State);
            Assert.Null(rejected.Person);

            CatalogueReviewAction firstUndo = Assert.IsType<CatalogueReviewAction>(
                await restartedRepository.UndoLatestAsync(
                    seeded.Id,
                    "human:test",
                    now.AddMinutes(3),
                    "Restore the prior assignment."));
            Assert.Equal(CatalogueReviewActionKinds.Undo, firstUndo.Kind);
            CatalogueReviewFace restored = Assert.IsType<CatalogueReviewFace>(
                await restartedRepository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Assigned, restored.State);
            Assert.Equal(person, restored.Person);

            _ = await restartedRepository.UndoLatestAsync(
                seeded.Id,
                "human:test",
                now.AddMinutes(4));
            CatalogueReviewFace unreviewed = Assert.IsType<CatalogueReviewFace>(
                await restartedRepository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Unreviewed, unreviewed.State);
            Assert.Null(unreviewed.Person);

            IReadOnlyList<CatalogueReviewAction> actions = await restartedRepository.GetActionsAsync(seeded.Id);
            Assert.Equal(4, actions.Count);
            Assert.Equal(2, actions.Count(action => action.ReversedAtUtc is not null));
            Assert.Equal(2, actions.Count(action => action.Kind == CatalogueReviewActionKinds.Undo));

            SqliteIdentityCatalogueRepository identityRepository = new(restartedDatabase);
            CatalogueHumanLabel label = Assert.Single(await identityRepository.GetHumanLabelsAsync(seeded.Id));
            Assert.Equal(person.Id, label.PersonId);
            Assert.Equal("Confirmed manually.", label.Note);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Review_api_hides_internal_paths_and_streams_faces_through_opaque_urls()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFace seeded = await SeedReviewFaceAsync(database, directory);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            string galleryJson = await client.GetStringAsync("/api/review/faces?state=all");
            Assert.Contains("secret-photo.jpg", galleryJson, StringComparison.Ordinal);
            Assert.DoesNotContain(seeded.SourceRoot, galleryJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.CropPath, galleryJson, StringComparison.OrdinalIgnoreCase);

            HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                "/api/review/people",
                new CreatePersonRequest("Grace Hopper"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            ReviewPersonResponse person = Assert.IsType<ReviewPersonResponse>(
                await createResponse.Content.ReadFromJsonAsync<ReviewPersonResponse>());

            HttpResponseMessage assignResponse = await client.PostAsJsonAsync(
                $"/api/review/faces/{seeded.Id}/assign",
                new AssignFaceRequest(person.Id, "pixel-reviewer", "Reviewed on phone."));
            assignResponse.EnsureSuccessStatusCode();

            string detailsJson = await client.GetStringAsync($"/api/review/faces/{seeded.Id}");
            Assert.Contains("Grace Hopper", detailsJson, StringComparison.Ordinal);
            Assert.Contains("pixel-reviewer", detailsJson, StringComparison.Ordinal);
            Assert.DoesNotContain(seeded.SourceRoot, detailsJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.CropPath, detailsJson, StringComparison.OrdinalIgnoreCase);

            byte[] imageBytes = await client.GetByteArrayAsync($"/api/review/faces/{seeded.Id}/image");
            Assert.Equal(seeded.CropBytes, imageBytes);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededReviewFace> SeedReviewFaceAsync(
        SqliteCatalogueDatabase database,
        string directory)
    {
        DateTimeOffset now = new(2026, 7, 26, 7, 50, 0, TimeSpan.Zero);
        string sourceRoot = Path.Combine(directory, "private-photos");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "family"));
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "family", "secret-photo.jpg"), [1, 2, 3]);

        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(sourceId, "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(assetId, sourceId, "family/secret-photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string('a', 64)),
            3,
            now,
            "image/jpeg",
            1200,
            800);
        CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
        byte[] cropBytes = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];
        string cropDirectory = Path.Combine(directory, "private-crops");
        Directory.CreateDirectory(cropDirectory);
        string cropPath = Path.Combine(cropDirectory, "aligned-face.png");
        await File.WriteAllBytesAsync(cropPath, cropBytes);
        string cropHash = Convert.ToHexString(SHA256.HashData(cropBytes)).ToLowerInvariant();

        await using SqliteConnection connection = await database.OpenConnectionAsync();
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
                'test-detector',
                $model_hash,
                0.97,
                '{"x":10,"y":10,"width":80,"height":80}',
                '[]',
                $created_at_utc);

            INSERT INTO face_crops (
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc)
            VALUES (
                $crop_id,
                $face_id,
                'review-test',
                $crop_hash,
                $crop_path,
                112,
                112,
                $created_at_utc);
            """;
        command.Parameters.AddWithValue("$face_id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$revision_id", persistedRevision.Id.ToString());
        command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
        command.Parameters.AddWithValue("$model_hash", new string('b', 64));
        command.Parameters.AddWithValue("$crop_id", FaceCropId.New().ToString());
        command.Parameters.AddWithValue("$crop_hash", cropHash);
        command.Parameters.AddWithValue("$crop_path", cropPath);
        await command.ExecuteNonQueryAsync();

        return new SeededReviewFace(occurrenceId, sourceRoot, cropPath, cropBytes);
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

    private sealed record SeededReviewFace(
        FaceOccurrenceId Id,
        string SourceRoot,
        string CropPath,
        byte[] CropBytes);

    private sealed class ReviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public ReviewApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
