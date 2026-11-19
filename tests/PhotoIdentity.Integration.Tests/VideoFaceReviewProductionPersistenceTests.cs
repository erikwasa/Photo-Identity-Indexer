using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Disposable PostgreSQL; synthetic video and face geometry only.
/// This test never touches originals, live people, inference or user files.
/// </summary>
public sealed class VideoFaceReviewProductionPersistenceTests
{
    private static VideoFaceEvidencePublication Batch(AssetRevisionId revision,
        Guid alice, Guid bob) => new(revision, new string('a', 64),
            "wi0155-production-face-evidence-v1", "yunet-2023mar-fp32",
            new string('b', 64), "sface-2021dec-fp32",
            new string('c', 64), "sface-five-point-v1",
            [
                new(0, 1000, 0, 1920, 1080, .25, .1, .04, .08,
                    [new(alice, .83), new(bob, .73)]),
                new(1, 3000, 0, 1920, 1080, .4, .2, .06, .12,
                    [new(bob, .79), new(alice, .65)])
            ]);

    [Fact]
    public async Task Face_evidence_and_review_are_revision_fenced_audited_and_exclusion_purged()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string name = "photoidentity_face_review_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection server = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await server.OpenAsync();
        await using (NpgsqlCommand command = server.CreateCommand())
        {
            command.CommandText = $"CREATE DATABASE \"{name}\";";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            string connectionString = new NpgsqlConnectionStringBuilder(admin)
            { Database = name, Pooling = false }.ConnectionString;
            await using PostgresCatalogueDatabase database = new(connectionString);
            await database.InitializeAsync();
            await database.InitializeAsync(); // Repeatable migration initialization.

            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), video = Guid.NewGuid(),
                photoAsset = Guid.NewGuid(), photo = Guid.NewGuid(),
                alice = Guid.NewGuid(), bob = Guid.NewGuid(), merged = Guid.NewGuid();
            DateTimeOffset now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources (id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-face-review',@now);
                    INSERT INTO assets (id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'private/V06.mp4',@now,'video'),
                           (@photo_asset,@source,'private/photo.jpg',@now,'photo');
                    INSERT INTO asset_revisions
                      (id,asset_id,media_kind,content_sha256,size_bytes,observed_at_utc,media_type)
                    VALUES (@video,@asset,'video',repeat('a',64),4000,@now,'video/mp4'),
                           (@photo,@photo_asset,'photo',repeat('e',64),150,@now,'image/jpeg');
                    INSERT INTO video_revision_metadata
                      (asset_revision_id,inspection_status,inspected_at_utc,
                       tool_sha256,inspection_profile_id)
                    VALUES (@video,'supported',@now,repeat('f',64),'synthetic-face-review');
                    INSERT INTO people (id,display_name,created_at_utc,merged_into_person_id)
                    VALUES (@alice,'Alice Example',@now,NULL),
                           (@bob,'Bob Example',@now,NULL),
                           (@merged,'Former Alice',@now,@alice);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("photo_asset", photoAsset);
                seed.Parameters.AddWithValue("video", video);
                seed.Parameters.AddWithValue("photo", photo);
                seed.Parameters.AddWithValue("alice", alice);
                seed.Parameters.AddWithValue("bob", bob);
                seed.Parameters.AddWithValue("merged", merged);
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            var repository = new PostgresVideoFaceReviewRepository(database, TimeProvider.System);
            AssetRevisionId revision = AssetRevisionId.From(video);
            var evidence = Batch(revision, alice, bob);
            Assert.Null(await repository.GetCurrentAsync(revision));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.PublishAsync(evidence with { SourceSha256 = new string('d', 64) }));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                repository.PublishAsync(evidence with { RevisionId = AssetRevisionId.From(photo) }));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.PublishAsync(evidence with {
                    Observations = [evidence.Observations[0], evidence.Observations[0]]
                }));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.PublishAsync(evidence with {
                    Observations = [evidence.Observations[0] with {
                        Suggestions = [new(alice, double.NaN)]
                    }]
                }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.PublishAsync(evidence with {
                    Observations = [evidence.Observations[0] with {
                        Suggestions = [new(merged, .95)]
                    }]
                }));

            Guid batchId = await repository.PublishAsync(evidence);
            Assert.NotEqual(Guid.Empty, batchId);
            Assert.Equal(batchId, await repository.PublishAsync(evidence)); // Safe retry.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.PublishAsync(evidence with {
                    Observations = [evidence.Observations[0]]
                })); // Conflicting evidence must not replace reviewed faces.
            VideoFaceReviewBatch batch = Assert.IsType<VideoFaceReviewBatch>(
                await repository.GetCurrentAsync(revision));
            Assert.Equal(batchId.ToString("D"), batch.BatchId);
            Assert.Equal("sface-2021dec-fp32", batch.SFaceModelId);
            Assert.Equal(2, batch.Observations.Count);
            VideoFaceReviewObservation first = batch.Observations[0];
            Assert.Equal(2, first.Suggestions.Count);
            Assert.Equal(alice.ToString("D"), first.Suggestions[0].PersonId);
            Assert.True(first.Suggestions[0].Selectable);
            Assert.Equal(.83, first.Suggestions[0].Similarity);
            Assert.Equal(.04, first.Width);
            Assert.Null(first.Decision);

            Guid faceId = Guid.Parse(first.Id);
            Assert.True(await repository.RecordDecisionAsync(
                revision, faceId, "known", alice, "test-reviewer"));
            Assert.False(await repository.RecordDecisionAsync(
                revision, faceId, "known", alice, "test-reviewer"));
            Assert.True(await repository.RecordDecisionAsync(
                revision, faceId, "unknown", null, "test-reviewer"));
            Assert.True(await repository.RecordDecisionAsync(
                revision, faceId, "clear", null, "test-reviewer"));
            Assert.Null(Assert.Single((await repository.GetCurrentAsync(revision))!
                .Observations, x => x.Id == first.Id).ReviewedPersonId);
            Assert.Equal("clear", (await repository.GetCurrentAsync(revision))!
                .Observations[0].Decision);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.RecordDecisionAsync(revision, faceId, "known", null, "test"));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.RecordDecisionAsync(revision, faceId, "unknown", alice, "test"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.RecordDecisionAsync(revision, faceId, "known", merged, "test"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                repository.RecordDecisionAsync(revision, Guid.NewGuid(), "unknown", null, "test"));

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand query = connection.CreateCommand())
            {
                query.CommandText = """
                    SELECT
                      (SELECT count(*) FROM video_face_evidence_batches),
                      (SELECT count(*) FROM video_face_observations),
                      (SELECT count(*) FROM video_face_suggestions),
                      (SELECT count(*) FROM video_face_review_actions),
                      (SELECT count(*) FROM video_person_actions),
                      (SELECT count(*) FROM photo_person_actions);
                    """;
                await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(4L, reader.GetInt64(2));
                Assert.Equal(3L, reader.GetInt64(3)); // Known -> unknown -> clear
                Assert.Equal(0L, reader.GetInt64(4)); // No video/person tagging.
                Assert.Equal(0L, reader.GetInt64(5)); // No photo identity changes.
            }

            Guid next = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand add = connection.CreateCommand())
            {
                add.CommandText = """
                    INSERT INTO asset_revisions
                      (id,asset_id,media_kind,content_sha256,size_bytes,observed_at_utc,media_type)
                    VALUES (@next,@asset,'video',repeat('9',64),4001,@later,'video/mp4');
                    """;
                add.Parameters.AddWithValue("next", next);
                add.Parameters.AddWithValue("asset", asset);
                add.Parameters.AddWithValue("later", now.AddDays(1));
                await add.ExecuteNonQueryAsync();
            }
            Assert.Null(await repository.GetCurrentAsync(revision));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                repository.RecordDecisionAsync(revision, faceId, "known", alice, "test"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.PublishAsync(evidence));

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions
                      (source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'private/V06.mp4',@now,'pending',@now);
                    """;
                exclude.Parameters.AddWithValue("source", source);
                exclude.Parameters.AddWithValue("now", now.AddDays(2));
                await exclude.ExecuteNonQueryAsync();
            }
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand counts = connection.CreateCommand())
            {
                counts.CommandText = """
                    SELECT (SELECT count(*) FROM video_face_evidence_batches),
                           (SELECT count(*) FROM video_face_observations),
                           (SELECT count(*) FROM video_face_suggestions),
                           (SELECT count(*) FROM video_face_review_actions);
                    """;
                await using NpgsqlDataReader reader = await counts.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                for (int i = 0; i < 4; i++) Assert.Equal(0L, reader.GetInt64(i));
            }
        }
        finally
        {
            await using (NpgsqlCommand stop = server.CreateCommand())
            {
                stop.CommandText = $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='{name}' AND pid <> pg_backend_pid();";
                await stop.ExecuteNonQueryAsync();
            }
            await using (NpgsqlCommand drop = server.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE \"{name}\";";
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
