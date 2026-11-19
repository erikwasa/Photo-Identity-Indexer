using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Disposable PostgreSQL and 128D synthetic vectors only. No video bytes,
/// models, image inference, live catalogue writes or source-file access.
/// </summary>
public sealed class VideoFaceProductionScorerParityTests
{
    private const string Model = "sface-2021dec-fp32";
    private static readonly string ModelHash = new('c', 64);

    private static EmbeddingVector Vector(float x, float y)
    {
        float[] data = new float[128];
        data[0] = x;
        data[1] = y;
        return new EmbeddingVector(data);
    }

    [Fact]
    public async Task Video_faces_use_production_best_of_all_photo_exemplars_without_group_averaging()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_wi0155_parity_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection server = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await server.OpenAsync();
        await using (NpgsqlCommand create = server.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connectionString = new NpgsqlConnectionStringBuilder(admin)
            { Database = name, Pooling = false }.ConnectionString;
            await using PostgresCatalogueDatabase database = new(connectionString);
            await database.InitializeAsync();
            Guid source = Guid.NewGuid(), photoAsset = Guid.NewGuid(),
                videoAsset = Guid.NewGuid(), photoRevision = Guid.NewGuid(),
                videoRevision = Guid.NewGuid(),
                alice = Guid.NewGuid(), bob = Guid.NewGuid(), carol = Guid.NewGuid();
            DateTimeOffset now = new(2026, 11, 19, 20, 0, 0, TimeSpan.Zero);

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources (id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-parity',@now);
                    INSERT INTO assets (id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@photo_asset,@source,'photo.jpg',@now,'photo'),
                           (@video_asset,@source,'video.mp4',@now,'video');
                    INSERT INTO asset_revisions
                      (id,asset_id,media_kind,content_sha256,size_bytes,observed_at_utc,media_type)
                    VALUES (@photo_revision,@photo_asset,'photo',repeat('e',64),2000,@now,'image/jpeg'),
                           (@video_revision,@video_asset,'video',repeat('a',64),4000,@now,'video/mp4');
                    INSERT INTO video_revision_metadata
                      (asset_revision_id,inspection_status,inspected_at_utc,
                       tool_sha256,inspection_profile_id)
                    VALUES (@video_revision,'supported',@now,repeat('f',64),'synthetic');
                    INSERT INTO people (id,display_name,created_at_utc)
                    VALUES (@alice,'Alice',@now),(@bob,'Bob',@now),(@carol,'Carol',@now);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("photo_asset", photoAsset);
                seed.Parameters.AddWithValue("video_asset", videoAsset);
                seed.Parameters.AddWithValue("photo_revision", photoRevision);
                seed.Parameters.AddWithValue("video_revision", videoRevision);
                seed.Parameters.AddWithValue("alice", alice);
                seed.Parameters.AddWithValue("bob", bob);
                seed.Parameters.AddWithValue("carol", carol);
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            // The fourth confirmed Alice exemplar is the only one that
            // resembles face 0. The old experiment's per-person cap of 3
            // would miss it; the production scorer must not.
            for (int i = 0; i < 4; i++)
                await AddExemplarAsync(database, photoRevision, alice, i, now,
                    i == 3 ? Vector(1, 0) : Vector(0, 1));
            await AddExemplarAsync(database, photoRevision, bob, 4, now, Vector(.8f, .6f));
            await AddExemplarAsync(database, photoRevision, carol, 5, now, Vector(-1, 0));

            var scorer = new PostgresIdentityMatchRegenerationScorer(database);
            IReadOnlyList<IReadOnlyList<VideoFaceIdentityScore>> rankings =
                await scorer.RankVideoFaceVectorsAsync(
                    new ModelId(Model), new Sha256Digest(ModelHash),
                    [Vector(1, 0), Vector(.8f, .6f)]);
            Assert.Equal(2, rankings.Count);
            Assert.Equal(3, rankings[0].Count);
            Assert.Equal(alice, rankings[0][0].PersonId);
            Assert.InRange(rankings[0][0].Similarity, .999999, 1.000001);
            Assert.Equal(bob, rankings[0][1].PersonId);
            Assert.InRange(rankings[0][1].Similarity, .79999, .80001);
            Assert.Equal(bob, rankings[1][0].PersonId);
            Assert.Equal(alice, rankings[1][1].PersonId);
            Assert.InRange(rankings[1][0].Similarity, .99999, 1.00001);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scorer.RankVideoFaceVectorsAsync(new ModelId(Model),
                    new Sha256Digest(new string('9', 64)), [Vector(1, 0)]));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                scorer.RankVideoFaceVectorsAsync(new ModelId(Model),
                    new Sha256Digest(ModelHash), [new EmbeddingVector([1f, 0f])]));

            var publisher = new PostgresVideoFaceMatchPublisher(
                scorer, new PostgresVideoFaceReviewRepository(database, TimeProvider.System));
            AssetRevisionId revision = AssetRevisionId.From(videoRevision);
            VideoFaceEmbeddingPublication evidence = new(revision, new string('a', 64),
                "wi0155-synthetic-parity-v1", "yunet-2023mar-fp32",
                new string('b', 64), Model, ModelHash, "sface-five-point-v1",
                [
                    new(0, 1000, 0, 1920, 1080, .2, .1, .05, .1, Vector(1, 0)),
                    new(1, 2000, 0, 1920, 1080, .3, .1, .05, .1, Vector(.8f, .6f))
                ]);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                publisher.PublishAsync(evidence with { AlignmentProtocol = "wrong" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                publisher.PublishAsync(evidence with { SourceSha256 = new string('d', 64) }));

            Guid batch = await publisher.PublishAsync(evidence);
            Assert.Equal(batch, await publisher.PublishAsync(evidence));
            var stored = Assert.IsType<VideoFaceReviewBatch>(
                await new PostgresVideoFaceReviewRepository(database, TimeProvider.System)
                    .GetCurrentAsync(revision));
            Assert.Equal(batch.ToString("D"), stored.BatchId);
            Assert.Equal(2, stored.Observations.Count);
            Assert.Equal(alice.ToString("D"), stored.Observations[0].Suggestions[0].PersonId);
            Assert.Equal(bob.ToString("D"), stored.Observations[1].Suggestions[0].PersonId);
            Assert.Equal(3, stored.Observations[0].Suggestions.Count);
            Assert.Null(stored.Observations[0].Decision);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand check = connection.CreateCommand())
            {
                check.CommandText = """
                    SELECT (SELECT count(*) FROM video_face_suggestions),
                           (SELECT count(*) FROM video_face_review_actions),
                           (SELECT count(*) FROM video_person_actions),
                           (SELECT count(*) FROM photo_person_actions);
                    """;
                await using NpgsqlDataReader reader = await check.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(6L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
            }

            Guid next = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand newer = connection.CreateCommand())
            {
                newer.CommandText = """
                    INSERT INTO asset_revisions
                      (id,asset_id,media_kind,content_sha256,size_bytes,observed_at_utc,media_type)
                    VALUES (@next,@asset,'video',repeat('6',64),4001,@later,'video/mp4');
                    """;
                newer.Parameters.AddWithValue("next", next);
                newer.Parameters.AddWithValue("asset", videoAsset);
                newer.Parameters.AddWithValue("later", now.AddDays(1));
                await newer.ExecuteNonQueryAsync();
            }
            Assert.Null(await new PostgresVideoFaceReviewRepository(database, TimeProvider.System)
                .GetCurrentAsync(revision));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => publisher.PublishAsync(evidence));
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

    private static async Task AddExemplarAsync(
        PostgresCatalogueDatabase database, Guid photoRevision,
        Guid person, int ordinal, DateTimeOffset now, EmbeddingVector vector)
    {
        Guid faceId = Guid.NewGuid(), cropId = Guid.NewGuid();
        float[] values = vector.ToArray();
        byte[] blob = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, blob, 0, blob.Length);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id,asset_revision_id,ordinal,created_at_utc)
            VALUES (@face,@revision,@ordinal,@now);
            INSERT INTO face_crops
              (id,face_occurrence_id,crop_protocol,content_sha256,
               storage_path,width,height,created_at_utc)
            VALUES (@crop,@face,'sface-five-point-v1',repeat('d',64),
                    @storage,112,112,@now);
            INSERT INTO embeddings
              (face_crop_id,model_id,model_hash,dimensions,
               l2_norm,vector_blob,created_at_utc)
            VALUES (@crop,@model,@model_hash,128,@norm,@blob,@now);
            INSERT INTO person_labels
              (person_id,face_occurrence_id,label_kind,assigned_by,assigned_at_utc)
            VALUES (@person,@face,'confirmed','synthetic-parity',@now);
            """;
        command.Parameters.AddWithValue("face", faceId);
        command.Parameters.AddWithValue("revision", photoRevision);
        command.Parameters.AddWithValue("ordinal", ordinal);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("crop", cropId);
        command.Parameters.AddWithValue("storage", $"synthetic/{ordinal}.jpg");
        command.Parameters.AddWithValue("model", Model);
        command.Parameters.AddWithValue("model_hash", ModelHash);
        command.Parameters.AddWithValue("norm", vector.L2Norm);
        command.Parameters.AddWithValue("blob", blob);
        command.Parameters.AddWithValue("person", person);
        await command.ExecuteNonQueryAsync();
    }
}
