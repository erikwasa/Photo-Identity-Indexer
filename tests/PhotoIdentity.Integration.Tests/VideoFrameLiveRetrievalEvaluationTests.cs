using System.Data;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameLiveRetrievalEvaluationTests
{
    [Fact]
    public void Private_judgments_require_two_distinct_jobs_and_unique_relevance_labels()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), query = Guid.NewGuid();
        var sample = new VideoFrameLiveRetrievalJudgments(
            "wi0190-live-retrieval-judgments-v1", [a, b],
            [new(query, "a bright outdoor scene", [a])]);
        VideoFrameLiveRetrievalSnapshot.ValidateJudgments(sample);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalSnapshot.ValidateJudgments(sample with
                { JobIds = [a, a] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalSnapshot.ValidateJudgments(sample with
                { Queries = [new(query, "private", [Guid.NewGuid()])] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalSnapshot.ValidateJudgments(sample with
                { Queries = [new(query, "private", [a, a])] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalSnapshot.ValidateJudgments(sample with
                { Queries = [new(query, "private\nline", [a])] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalSnapshot.ValidateJudgments(sample with
                { Queries = [new(query, "private", [a]), new(query, "other", [b])] }));
    }

    [Fact]
    public void Parser_refuses_implicit_or_remote_assets_and_unpinned_model()
    {
        string path = Path.Combine(Path.GetTempPath(), "synthetic-private.json");
        string[] valid =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST_LOCAL",
            "--database", "photoidentity_test",
            "--judgments", path,
            "--clip-model", path, "--clip-model-sha256", new string('a',64),
            "--tokenizer-vocab", path, "--tokenizer-vocab-sha256", new string('b',64),
            "--tokenizer-merges", path, "--tokenizer-merges-sha256", new string('c',64),
            "--frame-top-k", "2", "--rank-k", "2"
        ];
        var opts = VideoFrameLiveRetrievalOptions.Parse(valid);
        Assert.Equal(2, opts.FrameTopK);
        Assert.Equal(2, opts.RankingK);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalOptions.Parse([.. valid, "--apply", "true"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalOptions.Parse([.. valid, "--rank-k", "3"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalOptions.Parse(
                valid.Select((v,i) => i == 7 ? "RELATIVE_FILE" : v).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameLiveRetrievalOptions.Parse(
                valid.Select((v,i) => i == 9 ? new string('G',64) : v).ToArray()));
        if (OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() =>
                VideoFrameLiveRetrievalOptions.PhysicalLocalFile(
                    @"\\host\share\personal.json"));
    }

    [Fact]
    public async Task Current_postgres_batches_are_read_without_caption_text_and_stale_is_rejected()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string dbName = "photoidentity_live_retrieval_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = new(
            new NpgsqlConnectionStringBuilder(admin)
                { Pooling = false }.ConnectionString);
        await administrator.OpenAsync();
        await using (NpgsqlCommand create = administrator.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using PostgresCatalogueDatabase db = new(
                new NpgsqlConnectionStringBuilder(admin)
                { Database = dbName, Pooling = false }.ConnectionString);
            await db.InitializeAsync();
            Guid[] sources = [Guid.NewGuid(), Guid.NewGuid()];
            Guid[] assets = [Guid.NewGuid(), Guid.NewGuid()];
            Guid[] revisions = [Guid.NewGuid(), Guid.NewGuid()];
            Guid[] jobs = [Guid.NewGuid(), Guid.NewGuid()];
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            {
                for (int i = 0; i < 2; i++)
                {
                    await using NpgsqlCommand insert = conn.CreateCommand();
                    insert.CommandText = """
                        INSERT INTO sources(id,kind,root_locator,created_at_utc)
                        VALUES (@source,'local-folder','live-retrieval-test',now());
                        INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                        VALUES (@asset,@source,'private-video.mp4',now(),'video');
                        INSERT INTO asset_revisions(id,asset_id,content_sha256,
                            size_bytes,observed_at_utc,media_type,media_kind)
                        VALUES (@revision,@asset,repeat('a',64),4000,now(),'video/mp4','video');
                        INSERT INTO video_revision_metadata(asset_revision_id,
                            inspection_status,duration_seconds,color_transfer,
                            inspected_at_utc,tool_sha256,inspection_profile_id)
                        VALUES (@revision,'supported',8,'bt709',now(),repeat('a',64),'fixture');
                        INSERT INTO video_processing_jobs(id,asset_revision_id,
                            idempotency_key,derivative_kind,policy_version,
                            preset_version,tool_sha256,source_bytes,
                            reserved_temporary_bytes,requested_frames,status,
                            progress_percent,completed_at_utc)
                        VALUES (@job,@revision,@idempotency,'FrameEvidence',
                            'video-frame-sampling-v1','wi0190-sdr-frame-jpeg-v1',
                            repeat('c',64),4000,20000,2,'succeeded',100,now());
                        INSERT INTO video_frame_evidence_batches(job_id,asset_revision_id,
                            sampling_policy_version,extraction_profile_version,
                            clip_model_id,clip_model_sha256,clip_preprocessing_version,
                            caption_model_id,caption_model_sha256,
                            caption_prompt_version,frame_count,embedding_dimensions)
                        VALUES (@job,@revision,'video-frame-sampling-v1',
                            'wi0190-sdr-frame-jpeg-v1','openai/clip-vit-base-patch32',
                            repeat('d',64),
                            'clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1',
                            'synthetic-caption',repeat('e',64),'synthetic-prompt',2,2);
                        INSERT INTO video_frame_evidence_frames(job_id,ordinal,
                            timestamp_ms,caption,embedding_le_f32)
                        VALUES (@job,0,2000,'PRIVATE CAPTION FIRST',
                            decode('0000803f00000000','hex')),
                               (@job,1,6000,'PRIVATE CAPTION SECOND',
                            decode('0000803f00000000','hex'));
                        """;
                    insert.Parameters.AddWithValue("source", sources[i]);
                    insert.Parameters.AddWithValue("asset", assets[i]);
                    insert.Parameters.AddWithValue("revision", revisions[i]);
                    insert.Parameters.AddWithValue("job", jobs[i]);
                    insert.Parameters.AddWithValue("idempotency",
                        new string(i == 0 ? 'b' : 'f', 64));
                    await insert.ExecuteNonQueryAsync();
                }
            }
            async Task<VideoFrameRetrievalEvaluationClip[]> Read(string modelDigest)
            {
                await using NpgsqlConnection conn = await db.OpenConnectionAsync();
                await using NpgsqlTransaction tx =
                    await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
                await using NpgsqlCommand readOnly = conn.CreateCommand();
                readOnly.Transaction = tx;
                readOnly.CommandText = "SET TRANSACTION READ ONLY;";
                await readOnly.ExecuteNonQueryAsync();
                var rows = await VideoFrameLiveRetrievalSnapshot.ReadCurrentAsync(
                    conn, tx, jobs, modelDigest);
                await tx.CommitAsync();
                return rows;
            }
            var current = await Read(new string('d',64));
            Assert.Equal(2, current.Length);
            Assert.All(current, c => Assert.Equal(2, c.FrameEmbeddings.Length));
            Assert.Equal(1f, current[0].FrameEmbeddings[0][0]);
            var metrics = VideoFrameRetrievalEvaluation.Evaluate(
                new(VideoFrameRetrievalEvaluation.ManifestProtocol,
                    new string('d',64),
                    "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1",
                    "openai-clip-byte-bpe-v1", current,
                    [new(Guid.NewGuid(), [1f, 0f], [jobs[0]])]), 2, 1);
            Assert.Equal(2, metrics.Clips);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Read(new string('e',64)));
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand invalidate = conn.CreateCommand())
            {
                invalidate.CommandText =
                    "UPDATE video_revision_metadata SET inspection_status='unsupported' WHERE asset_revision_id=@revision;";
                invalidate.Parameters.AddWithValue("revision", revisions[0]);
                await invalidate.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Read(new string('d',64)));

            // A newer revision must make the older completed batch stale,
            // even though its original caption and vectors still exist.
            Guid newest = Guid.NewGuid();
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand newer = conn.CreateCommand())
            {
                newer.CommandText = """
                    UPDATE video_revision_metadata
                    SET inspection_status='supported'
                    WHERE asset_revision_id=@revision;
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@newer,@asset,repeat('f',64),5000,
                        now()+interval '1 minute','video/mp4','video');
                    """;
                newer.Parameters.AddWithValue("revision", revisions[0]);
                newer.Parameters.AddWithValue("newer", newest);
                newer.Parameters.AddWithValue("asset", assets[0]);
                await newer.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Read(new string('d',64)));
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand removeNewer = conn.CreateCommand())
            {
                removeNewer.CommandText =
                    "DELETE FROM asset_revisions WHERE id=@newer;";
                removeNewer.Parameters.AddWithValue("newer", newest);
                await removeNewer.ExecuteNonQueryAsync();
            }

            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = conn.CreateCommand())
            {
                exclude.CommandText = """
                    UPDATE video_revision_metadata
                    SET inspection_status='supported'
                    WHERE asset_revision_id=@revision;
                    INSERT INTO source_copy_exclusions(source_id,source_key,
                        excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'private-video.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", sources[0]);
                exclude.Parameters.AddWithValue("revision", revisions[0]);
                await exclude.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Read(new string('d',64)));
        }
        finally
        {
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Private_malformed_judgments_are_sanitized_before_database_or_model_access()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "pi-live-eval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "private-personal-judgments.json");
        try
        {
            await File.WriteAllTextAsync(file,
                "{ \"privateVideo\": \"UNPUBLISHED_PRIVATE_MOVIE.mov\" }");
            var options = new VideoFrameLiveRetrievalOptions(
                "MISSING_ENV", "photoidentity_test", file,
                file, new string('a', 64), file, new string('b', 64),
                file, new string('c', 64));
            using StringWriter output = new();
            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    VideoFrameLiveRetrievalCommandRunner.RunAsync(options, output));
            Assert.DoesNotContain("UNPUBLISHED_PRIVATE_MOVIE", failure.Message);
            Assert.DoesNotContain(file, failure.Message);
            Assert.Empty(output.ToString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
