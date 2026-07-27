using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class BulkReviewApplicationTests
{
    [Fact]
    public async Task Preview_and_confirm_commit_only_currently_unreviewed_faces()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            FaceOccurrenceId[] faces = await SeedFacesAsync(database, 3);
            SqliteReviewRepository reviewRepository = new(database);
            DateTimeOffset now = new(2026, 7, 27, 18, 0, 0, TimeSpan.Zero);
            CatalogueReviewPerson person = await reviewRepository.CreatePersonAsync("Ada", now);
            await reviewRepository.RejectAsync(
                faces[2],
                "bulk:test",
                now.AddMinutes(1),
                "Already reviewed before preview.");

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string[] faceIds = faces.Select(face => face.ToString()).ToArray();
            using HttpResponseMessage previewResponse = await client.PostAsJsonAsync(
                "/api/review/bulk/preview",
                new BulkReviewPreviewRequest(
                    faceIds,
                    CatalogueBulkReviewActionKinds.Assign,
                    person.Id.ToString()));

            previewResponse.EnsureSuccessStatusCode();
            BulkReviewPreviewResponse preview = Assert.IsType<BulkReviewPreviewResponse>(
                await previewResponse.Content.ReadFromJsonAsync<BulkReviewPreviewResponse>());
            Assert.Equal(3, preview.RequestedCount);
            Assert.Equal(2, preview.AffectedCount);
            Assert.Equal(1, preview.SkippedCount);
            Assert.Equal(person.Id.ToString(), preview.Person?.Id);
            Assert.Equal(64, preview.PreviewToken.Length);

            await using (SqliteConnection beforeCommit = await database.OpenConnectionAsync())
            {
                Assert.Equal(0, await ReadInt64Async(beforeCommit, "SELECT COUNT(*) FROM person_labels;"));
                Assert.Equal(1, await ReadInt64Async(beforeCommit, "SELECT COUNT(*) FROM review_actions;"));
            }

            BulkReviewCommitRequest unconfirmed = new(
                faceIds,
                CatalogueBulkReviewActionKinds.Assign,
                person.Id.ToString(),
                preview.AffectedCount,
                preview.PreviewToken,
                Confirm: false,
                Actor: "bulk:test",
                Note: "Assign selected faces.");
            using HttpResponseMessage unconfirmedResponse = await client.PostAsJsonAsync(
                "/api/review/bulk/commit",
                unconfirmed);
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmedResponse.StatusCode);

            BulkReviewCommitRequest confirmed = unconfirmed with { Confirm = true };
            using HttpResponseMessage commitResponse = await client.PostAsJsonAsync(
                "/api/review/bulk/commit",
                confirmed);
            commitResponse.EnsureSuccessStatusCode();
            BulkReviewCommitResponse result = Assert.IsType<BulkReviewCommitResponse>(
                await commitResponse.Content.ReadFromJsonAsync<BulkReviewCommitResponse>());
            Assert.Equal(2, result.AffectedCount);
            Assert.Equal(person.Id.ToString(), result.Person?.Id);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(2, await ReadInt64Async(connection, "SELECT COUNT(*) FROM person_labels WHERE label_kind = 'manual';"));
            Assert.Equal(2, await ReadInt64Async(connection, "SELECT COUNT(*) FROM review_actions WHERE action_kind = 'assign';"));
            Assert.Equal(1, await ReadInt64Async(connection, "SELECT COUNT(*) FROM review_actions WHERE action_kind = 'reject';"));
            Assert.Equal(
                2,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM review_actions WHERE note = 'Assign selected faces.';"));

            CatalogueReviewFace first = Assert.IsType<CatalogueReviewFace>(
                await reviewRepository.GetFaceAsync(faces[0]));
            CatalogueReviewFace second = Assert.IsType<CatalogueReviewFace>(
                await reviewRepository.GetFaceAsync(faces[1]));
            CatalogueReviewFace skipped = Assert.IsType<CatalogueReviewFace>(
                await reviewRepository.GetFaceAsync(faces[2]));
            Assert.Equal(CatalogueReviewStates.Assigned, first.State);
            Assert.Equal(CatalogueReviewStates.Assigned, second.State);
            Assert.Equal(CatalogueReviewStates.Rejected, skipped.State);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Stale_preview_is_rejected_without_partial_bulk_changes()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            FaceOccurrenceId[] faces = await SeedFacesAsync(database, 2);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string[] faceIds = faces.Select(face => face.ToString()).ToArray();
            using HttpResponseMessage previewResponse = await client.PostAsJsonAsync(
                "/api/review/bulk/preview",
                new BulkReviewPreviewRequest(
                    faceIds,
                    CatalogueBulkReviewActionKinds.Reject));
            previewResponse.EnsureSuccessStatusCode();
            BulkReviewPreviewResponse preview = Assert.IsType<BulkReviewPreviewResponse>(
                await previewResponse.Content.ReadFromJsonAsync<BulkReviewPreviewResponse>());
            Assert.Equal(2, preview.AffectedCount);

            SqliteReviewRepository repository = new(database);
            await repository.RejectAsync(
                faces[0],
                "other-reviewer",
                new DateTimeOffset(2026, 7, 27, 18, 10, 0, TimeSpan.Zero));

            using HttpResponseMessage commitResponse = await client.PostAsJsonAsync(
                "/api/review/bulk/commit",
                new BulkReviewCommitRequest(
                    faceIds,
                    CatalogueBulkReviewActionKinds.Reject,
                    PersonId: null,
                    preview.AffectedCount,
                    preview.PreviewToken,
                    Confirm: true,
                    Actor: "bulk:test"));

            Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);
            string conflict = await commitResponse.Content.ReadAsStringAsync();
            Assert.Contains("Preview", conflict, StringComparison.OrdinalIgnoreCase);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await ReadInt64Async(connection, "SELECT COUNT(*) FROM review_actions;"));
            CatalogueReviewFace untouched = Assert.IsType<CatalogueReviewFace>(
                await repository.GetFaceAsync(faces[1]));
            Assert.Equal(CatalogueReviewStates.Unreviewed, untouched.State);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<FaceOccurrenceId[]> SeedFacesAsync(
        SqliteCatalogueDatabase database,
        int count)
    {
        string now = new DateTimeOffset(2026, 7, 27, 17, 50, 0, TimeSpan.Zero).ToString("O");
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using (SqliteCommand seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES ($source_id, 'local-folder', $root_locator, $now);
                INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES ($asset_id, $source_id, 'bulk-photo.jpg', $now, $now);
                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc,
                    media_type, width, height)
                    VALUES ($revision_id, $asset_id, $hash, 1234, $now, 'image/jpeg', 640, 480);
                """;
            seed.Parameters.AddWithValue("$source_id", sourceId);
            seed.Parameters.AddWithValue("$root_locator", Path.Combine(Path.GetTempPath(), sourceId));
            seed.Parameters.AddWithValue("$asset_id", assetId);
            seed.Parameters.AddWithValue("$revision_id", revisionId);
            seed.Parameters.AddWithValue("$hash", new string('a', 64));
            seed.Parameters.AddWithValue("$now", now);
            await seed.ExecuteNonQueryAsync();
        }

        FaceOccurrenceId[] faces = Enumerable.Range(0, count)
            .Select(_ => FaceOccurrenceId.New())
            .ToArray();
        for (int index = 0; index < faces.Length; index++)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($face_id, $revision_id, $ordinal, $now);
                """;
            insert.Parameters.AddWithValue("$face_id", faces[index].ToString());
            insert.Parameters.AddWithValue("$revision_id", revisionId);
            insert.Parameters.AddWithValue("$ordinal", index);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync();
        }

        return faces;
    }

    private static async Task<long> ReadInt64Async(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
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
