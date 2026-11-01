using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Recognition.Onnx.Semantic;

namespace PhotoIdentity.Cli;

/// <summary>Private labels only; no original media path or exported embeddings.</summary>
public sealed record VideoFrameLiveRetrievalJudgments(
    string Protocol, Guid[] JobIds, VideoFrameLiveRetrievalJudgment[] Queries);

public sealed record VideoFrameLiveRetrievalJudgment(
    Guid QueryId, string Text, Guid[] RelevantJobIds);

public sealed record VideoFrameLiveRetrievalOptions(
    string ConnectionEnv, string Database, string JudgmentsPath,
    string ClipModel, string ClipModelSha256,
    string Vocabulary, string VocabularySha256,
    string Merges, string MergesSha256,
    int FrameTopK = 3, int RankingK = 5)
{
    public static VideoFrameLiveRetrievalOptions Parse(string[] args)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        string[] allowed =
        [
            "--postgres-connection-env", "--database", "--judgments",
            "--clip-model", "--clip-model-sha256", "--tokenizer-vocab",
            "--tokenizer-vocab-sha256", "--tokenizer-merges",
            "--tokenizer-merges-sha256", "--frame-top-k", "--rank-k"
        ];
        for (int i = 0; i < args.Length; i++)
        {
            if (!allowed.Contains(args[i], StringComparer.Ordinal) ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !fields.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unrecognized or repeated live retrieval option.");
        }
        string Required(string key) =>
            fields.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Live retrieval requires explicit local inputs.");
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid local PostgreSQL selector.");
        static string Digest(string value)
        {
            if (value.Length != 64 || value.Any(c => c is not
                (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new ArgumentException("Pinned local file digest must be lowercase SHA-256.");
            return value;
        }
        int Bound(string name, int fallback, int max)
        {
            if (!fields.TryGetValue(name, out string? text)) return fallback;
            if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int value) ||
                value is < 1 || value > max)
                throw new ArgumentException("Invalid retrieval ranking bound.");
            return value;
        }
        return new(env, db, PhysicalLocalFile(Required("--judgments")),
            PhysicalLocalFile(Required("--clip-model")),
            Digest(Required("--clip-model-sha256")),
            PhysicalLocalFile(Required("--tokenizer-vocab")),
            Digest(Required("--tokenizer-vocab-sha256")),
            PhysicalLocalFile(Required("--tokenizer-merges")),
            Digest(Required("--tokenizer-merges-sha256")),
            Bound("--frame-top-k", 3, VideoFrameSamplingPlanner.HardMaximumFrames),
            Bound("--rank-k", 5, VideoFrameRetrievalEvaluation.MaximumClips));
    }

    internal static string PhysicalLocalFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("A physical absolute local file is required.");
        return Path.GetFullPath(path);
    }
}

/// <summary>
/// Read-only PostgreSQL snapshot reader. Unlike the metadata-only audit,
/// this private benchmark intentionally fetches only embedding bytea into
/// memory. It never fetches captions, original paths or JPEGs, exports
/// vectors, or performs writes, migrations or video enqueue operations.
/// </summary>
public static class VideoFrameLiveRetrievalSnapshot
{
    public static void ValidateJudgments(VideoFrameLiveRetrievalJudgments value)
    {
        if (value is null || value.Protocol != "wi0190-live-retrieval-judgments-v1" ||
            value.JobIds is null ||
            value.JobIds.Length is < 2 or > VideoFrameRetrievalEvaluation.MaximumClips ||
            value.JobIds.Any(id => id == Guid.Empty) ||
            value.JobIds.Distinct().Count() != value.JobIds.Length ||
            value.Queries is null || value.Queries.Length is < 1 or > 16)
            throw new ArgumentException("Private retrieval judgments are invalid.");
        HashSet<Guid> jobs = value.JobIds.ToHashSet();
        HashSet<Guid> queries = [];
        foreach (var query in value.Queries)
        {
            if (query is null || query.QueryId == Guid.Empty ||
                !queries.Add(query.QueryId) ||
                string.IsNullOrWhiteSpace(query.Text) ||
                query.Text.Length is > 200 ||
                query.Text.Any(char.IsControl) ||
                query.RelevantJobIds is null ||
                query.RelevantJobIds.Length is < 1 or > VideoFrameRetrievalEvaluation.MaximumClips ||
                query.RelevantJobIds.Distinct().Count() != query.RelevantJobIds.Length ||
                query.RelevantJobIds.Any(id => !jobs.Contains(id)))
                throw new ArgumentException("Private retrieval judgments are invalid.");
        }
    }

    public static async Task<VideoFrameRetrievalEvaluationClip[]> ReadCurrentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyList<Guid> jobIds, string expectedModelSha256,
        CancellationToken token = default)
    {
        if (jobIds is null || jobIds.Count is < 2 or
            > VideoFrameRetrievalEvaluation.MaximumClips ||
            jobIds.Any(id => id == Guid.Empty) ||
            jobIds.Distinct().Count() != jobIds.Count ||
            expectedModelSha256 is not { Length: 64 })
            throw new ArgumentException("Invalid retrieval snapshot selectors.");

        List<VideoFrameRetrievalEvaluationClip> clips = [];
        HashSet<Guid> distinctAssets = [];
        foreach (Guid job in jobIds)
        {
            var audit = await VideoFrameEvidenceAuditCommandRunner.ReadSnapshotAsync(
                connection, transaction, job, token);
            if (VideoFrameEvidenceAuditPolicy.Classify(audit) != "current-complete" ||
                audit!.ExpectedFrames > VideoFrameSamplingPlanner.HardMaximumFrames)
                throw new InvalidOperationException(
                    "One or more selected frame batches are no longer current and complete.");

            // Model identity and revision are private, never returned in the
            // report. All decisions use the SAME PG repeatable-read snapshot.
            await using NpgsqlCommand header = connection.CreateCommand();
            header.Transaction = transaction;
            header.CommandText = """
                SELECT b.asset_revision_id, b.clip_model_id,
                       b.clip_model_sha256, b.clip_preprocessing_version,
                       revision.asset_id
                FROM video_frame_evidence_batches b
                JOIN asset_revisions revision ON revision.id=b.asset_revision_id
                WHERE b.job_id=@job;
                """;
            header.Parameters.AddWithValue("job", job);
            await using (NpgsqlDataReader reader = await header.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token) ||
                    reader.GetString(1) != ClipEmbeddingEncoder.ModelId ||
                    reader.GetString(2) != expectedModelSha256 ||
                    reader.GetString(3) != ClipZeroShotTagger.ImagePreprocessingVersion ||
                    !distinctAssets.Add(reader.GetGuid(4)))
                    throw new InvalidOperationException(
                        "Selected video evidence has incompatible CLIP provenance or duplicate source.");
            }

            var vectors = new List<float[]>();
            await using NpgsqlCommand frames = connection.CreateCommand();
            frames.Transaction = transaction;
            frames.CommandText = """
                SELECT ordinal, embedding_le_f32
                FROM video_frame_evidence_frames WHERE job_id=@job
                ORDER BY ordinal;
                """;
            frames.Parameters.AddWithValue("job", job);
            await using (NpgsqlDataReader reader = await frames.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    if (reader.GetInt32(0) != vectors.Count ||
                        vectors.Count >= VideoFrameSamplingPlanner.HardMaximumFrames)
                        throw new InvalidOperationException(
                            "Selected frame batch has incomplete vector ordinals.");
                    byte[] bytes = reader.GetFieldValue<byte[]>(1);
                    float[] vector = PostgresVideoFrameEvidenceRepository.DecodeVector(
                        bytes, audit.Dimensions);
                    if (vector.Any(v => !float.IsFinite(v)))
                        throw new InvalidOperationException(
                            "Selected frame batch has invalid vector values.");
                    vectors.Add(vector);
                }
            }
            if (vectors.Count != audit.ExpectedFrames)
                throw new InvalidOperationException(
                    "Selected frame batch has incomplete vectors.");
            clips.Add(new(job, vectors.ToArray()));
        }
        return clips.ToArray();
    }
}

public static class VideoFrameLiveRetrievalCommandRunner
{
    private const int MaximumJudgmentBytes = 64 * 1024;

    public static async Task<int> RunAsync(
        VideoFrameLiveRetrievalOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (options.FrameTopK is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            options.RankingK is < 1 or > VideoFrameRetrievalEvaluation.MaximumClips)
            throw new ArgumentException("Invalid live retrieval bounds.");

        VideoFrameLiveRetrievalJudgments judgments;
        try
        {
            await using FileStream input = OpenLocal(options.JudgmentsPath, MaximumJudgmentBytes);
            byte[] bytes = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(bytes, token);
            judgments = JsonSerializer.Deserialize<VideoFrameLiveRetrievalJudgments>(
                bytes, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    MaxDepth = 12
                }) ?? throw new InvalidDataException();
            VideoFrameLiveRetrievalSnapshot.ValidateJudgments(judgments);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or NotSupportedException)
        {
            throw new InvalidOperationException("Private retrieval judgments are invalid or unavailable.");
        }

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Local PostgreSQL connection environment is missing.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Live retrieval evaluation requires loopback PostgreSQL.");
        builder.Database = options.Database;

        // Pin all three local files before inference. The stored frame
        // evidence independently pins the image/dual ONNX model digest.
        long preflightStarted = Stopwatch.GetTimestamp();
        await CheckDigestAsync(options.ClipModel, options.ClipModelSha256,
            2L * 1024 * 1024 * 1024, token);
        await CheckDigestAsync(options.Vocabulary, options.VocabularySha256,
            128L * 1024 * 1024, token);
        await CheckDigestAsync(options.Merges, options.MergesSha256,
            128L * 1024 * 1024, token);
        long preflightWallMs = (long)Stopwatch.GetElapsedTime(preflightStarted).TotalMilliseconds;

        long textStarted = Stopwatch.GetTimestamp();
        float[][] queries = new float[judgments.Queries.Length][];
        try
        {
            using ClipEmbeddingEncoder encoder = new(
                options.ClipModel, options.Vocabulary, options.Merges);
            for (int i = 0; i < queries.Length; i++)
                queries[i] = encoder.EncodeText(judgments.Queries[i].Text, token)
                    .Vector.ToArray();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Pinned local CLIP text inference failed.");
        }

        long textInferenceWallMs = (long)Stopwatch.GetElapsedTime(textStarted).TotalMilliseconds;
        long snapshotStarted = Stopwatch.GetTimestamp();
        VideoFrameRetrievalEvaluationClip[] clips;
        try
        {
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
            await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, token);
            await using (NpgsqlCommand readOnly = connection.CreateCommand())
            {
                readOnly.Transaction = tx;
                readOnly.CommandText = "SET TRANSACTION READ ONLY;";
                await readOnly.ExecuteNonQueryAsync(token);
            }
            await using (NpgsqlCommand schema = connection.CreateCommand())
            {
                schema.Transaction = tx;
                schema.CommandText =
                    "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
                if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                    PostgresCatalogueDatabase.CurrentSchemaVersion)
                    throw new InvalidOperationException("Current PostgreSQL v38 is required.");
            }
            clips = await VideoFrameLiveRetrievalSnapshot.ReadCurrentAsync(
                connection, tx, judgments.JobIds, options.ClipModelSha256, token);
            await tx.CommitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "Current complete and compatible video evidence could not be verified.");
        }

        long snapshotReadWallMs = (long)Stopwatch.GetElapsedTime(snapshotStarted).TotalMilliseconds;
        var manifest = new VideoFrameRetrievalEvaluationManifest(
            VideoFrameRetrievalEvaluation.ManifestProtocol,
            options.ClipModelSha256,
            ClipZeroShotTagger.ImagePreprocessingVersion,
            ClipZeroShotTagger.TokenizerVersion,
            clips,
            judgments.Queries.Select((q, i) =>
                new VideoFrameRetrievalEvaluationQuery(
                    q.QueryId, queries[i], q.RelevantJobIds)).ToArray());

        long evaluationStarted = Stopwatch.GetTimestamp();
        VideoFrameRetrievalEvaluationResult metrics;
        try
        {
            metrics = VideoFrameRetrievalEvaluation.Evaluate(
                manifest, options.FrameTopK, options.RankingK);
        }
        catch
        {
            throw new InvalidOperationException(
                "Live retrieval vectors and judgments are incompatible.");
        }

        long evaluationWallMs = (long)Stopwatch.GetElapsedTime(evaluationStarted).TotalMilliseconds;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-live-retrieval-evaluation-v1",
            readOnly = true, currentSnapshot = true,
            localTextInference = true,
            modelDigestMatched = true,
            humanJudgments = true,
            modelPreflightWallMs = preflightWallMs,
            textInferenceWallMs, snapshotReadWallMs, evaluationWallMs,
            result = metrics,
            originalsOpened = false, oneDriveHydrationRequested = false,
            mediaDecoded = false, captionsReturned = false,
            embeddingsReturned = false, rawQueryTextReturned = false,
            videoJobsEnqueued = false, schemaChanged = false,
            externalNetworkRequestsMade = false,
            loopbackPostgresReadPerformed = true
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return 0;
    }

    private static FileStream OpenLocal(string path, long maximum)
    {
        path = VideoFrameLiveRetrievalOptions.PhysicalLocalFile(path);
        // Refuse symlink/reparse traversal at every existing directory
        // component, not only at the leaf.
        string? parent = path;
        while (parent is not null)
        {
            FileAttributes attr = File.GetAttributes(parent);
            if ((attr & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked private evaluation input refused.");
            parent = Path.GetDirectoryName(parent);
        }
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw new InvalidDataException("Evaluation input must be a regular file.");
        var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length is < 1 || file.Length > maximum)
        {
            file.Dispose();
            throw new InvalidDataException("Private evaluation input size is invalid.");
        }
        return file;
    }

    private static async Task CheckDigestAsync(string path, string expected,
        long limit, CancellationToken token)
    {
        try
        {
            await using FileStream file = OpenLocal(path, limit);
            string actual = Convert.ToHexString(
                await SHA256.HashDataAsync(file, token)).ToLowerInvariant();
            if (!StringComparer.Ordinal.Equals(actual, expected))
                throw new InvalidDataException("Pinned CLIP input changed.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "A pinned local CLIP model or tokenizer file is unavailable or invalid.");
        }
    }
}
