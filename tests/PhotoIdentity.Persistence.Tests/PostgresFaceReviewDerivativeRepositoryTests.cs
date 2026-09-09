using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresFaceReviewDerivativeRepositoryTests
{
    [Fact]
    public async Task Derivatives_and_completion_are_atomic_replay_safe_and_current_revision_scoped_when_live_postgres_is_configured()
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString)) return;
        string name = $"photoidentity_face_review_{Guid.NewGuid():N}";
        await using NpgsqlConnection admin = new(new NpgsqlConnectionStringBuilder(adminString) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = new($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            await using PostgresCatalogueDatabase database = new(new NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false }.ConnectionString);
            await database.InitializeAsync();
            await database.InitializeAsync();
            SourceId source = SourceId.New();
            Guid asset = Guid.NewGuid();
            AssetRevisionId revision = AssetRevisionId.New();
            AssetRevisionId emptyRevision = AssetRevisionId.New();
            FaceOccurrenceId first = FaceOccurrenceId.New();
            FaceOccurrenceId second = FaceOccurrenceId.New();
            DateTimeOffset now = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            await using NpgsqlConnection connection = await database.OpenConnectionAsync();
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc) VALUES (@source, 'test', 'synthetic', @now);
                    INSERT INTO assets(id, source_id, source_key, created_at_utc) VALUES (@asset, @source, 'photo.jpg', @now);
                    INSERT INTO asset_revisions(id, asset_id, content_sha256, size_bytes, observed_at_utc, width, height)
                    VALUES (@revision, @asset, @hash, 123, @now, 1000, 500),
                           (@empty, @asset, @otherhash, 123, @now - interval '1 minute', 1000, 500);
                    INSERT INTO face_occurrences(id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@first, @revision, 0, @now), (@second, @revision, 1, @now);
                    INSERT INTO face_observations(face_occurrence_id, detector_model_id, detector_model_hash, confidence, bounding_box_json, landmarks_json, observed_at_utc)
                    VALUES (@first, 'test', @hash, 0.9, '[100,100,200,100]', '{}', @now),
                           (@second, 'test', @hash, 0.8, '{"x":0.2,"y":0.1,"width":0.2,"height":0.2}', '{}', @now);
                    """;
                seed.Parameters.AddWithValue("source", Guid.Parse(source.ToString()));
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", Guid.Parse(revision.ToString()));
                seed.Parameters.AddWithValue("empty", Guid.Parse(emptyRevision.ToString()));
                seed.Parameters.AddWithValue("first", Guid.Parse(first.ToString()));
                seed.Parameters.AddWithValue("second", Guid.Parse(second.ToString()));
                seed.Parameters.AddWithValue("hash", new string('a', 64));
                seed.Parameters.AddWithValue("otherhash", new string('b', 64));
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }
            // Reconstruct v21 in this disposable database, retaining existing catalogue rows,
            // then verify the additive v22 migration and repeated initialization.
            await using (NpgsqlCommand previousSchema = connection.CreateCommand())
            {
                previousSchema.CommandText = """
                    DROP TABLE face_review_derivatives, asset_revision_face_review_completions;
                    DELETE FROM photo_identity_schema_migrations WHERE version = 22;
                    """;
                await previousSchema.ExecuteNonQueryAsync();
            }
            await database.InitializeAsync();
            await database.InitializeAsync();
            IFaceReviewDerivativeRepository repository = new PostgresFaceReviewDerivativeRepository(database);
            IFaceReviewDerivativeBackfillRepository pending = new PostgresFaceReviewDerivativeBackfillRepository(database);
            Assert.Equal(revision, await pending.GetNextPendingCurrentRevisionAsync(source, "profile"));
            Assert.Null(await pending.GetNextPendingCurrentRevisionAsync(SourceId.New(), "profile"));
            Assert.Null(await repository.GetAsync(first, "profile"));
            IReadOnlyList<FaceReviewGeometry> geometry = await repository.GetFacesAsync(revision);
            Assert.Equal(2, geometry.Count);
            Assert.Equal(first, geometry[0].FaceOccurrenceId);
            Assert.Equal(new NormalizedBoundingBox(0.1, 0.2, 0.2, 0.2), geometry[0].BoundingBox);
            Assert.Equal(new NormalizedBoundingBox(0.2, 0.1, 0.2, 0.2), geometry[1].BoundingBox);
            FaceReviewDerivativeRecord Record(FaceOccurrenceId id, string path) => new(id, "profile", 100, new Sha256Digest(new string('c', 64)), 960, 800, now, path);
            FaceReviewDerivativeRecord one = Record(first, "face-review/one.jpg");
            FaceReviewDerivativeRecord two = Record(second, "face-review/two.jpg");
            // The second write violates path uniqueness; neither the first row nor completion may survive.
            await Assert.ThrowsAsync<PostgresException>(() => repository.RecordRevisionCompletionAsync(revision, "profile", [one, Record(second, one.RelativePath)], now));
            Assert.Null(await repository.GetAsync(first, "profile"));
            Assert.False(await repository.IsRevisionCompleteAsync(revision, "profile"));
            await Assert.ThrowsAsync<ArgumentException>(() => repository.RecordRevisionCompletionAsync(emptyRevision, "profile", [one], now));
            Assert.False(await repository.IsRevisionCompleteAsync(emptyRevision, "profile"));
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.RecordRevisionCompletionAsync(revision, "profile", [one, two], now)));
            Assert.Equal(one, await repository.GetAsync(first, "profile"));
            Assert.Equal(two, await repository.GetAsync(second, "profile"));
            Assert.True(await repository.IsRevisionCompleteAsync(revision, "profile"));
            Assert.Null(await pending.GetNextPendingCurrentRevisionAsync(source, "profile"));
            Assert.Equal(revision, await pending.GetNextPendingCurrentRevisionAsync(source, "other-profile"));
            await repository.RecordRevisionCompletionAsync(emptyRevision, "profile", [], now);
            Assert.True(await repository.IsRevisionCompleteAsync(emptyRevision, "profile"));
            Assert.Empty(await repository.GetFacesAsync(emptyRevision));
            await using (NpgsqlCommand change = connection.CreateCommand())
            {
                change.CommandText = "UPDATE asset_revisions SET observed_at_utc = @now WHERE id = @id;";
                change.Parameters.AddWithValue("now", now.AddMinutes(1));
                change.Parameters.AddWithValue("id", Guid.Parse(emptyRevision.ToString()));
                await change.ExecuteNonQueryAsync();
            }
            Assert.Null(await pending.GetNextPendingCurrentRevisionAsync(source, "other-profile"));
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.RecordRevisionCompletionAsync(revision, "profile", [one, two], now, cancelled.Token));
            Assert.Equal(one, await repository.GetAsync(first, "profile"));
        }
        finally
        {
            await using NpgsqlCommand drop = new($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
