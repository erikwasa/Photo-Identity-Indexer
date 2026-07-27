using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CatalogueEvaluationExportCommandTests
{
    private const string DetectorId = "test-detector";
    private const string EmbedderId = "test-embedder";
    private static readonly string DetectorHash = new('a', 64);
    private static readonly string EmbedderHash = new('b', 64);

    [Fact]
    public async Task Export_is_deterministic_grouped_private_and_evaluable_for_run_and_revision_scopes()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            ExportFixture fixture = await CreateFixtureAsync(directory, includeUnknownPeople: true);
            string firstPath = Path.Combine(directory, "run-first.json");
            string secondPath = Path.Combine(directory, "run-second.json");

            (int firstExit, string firstOutput, string firstError) = await RunExportAsync(
                fixture.DatabasePath,
                firstPath,
                ["--run", fixture.RunId.ToString()]);
            (int secondExit, _, string secondError) = await RunExportAsync(
                fixture.DatabasePath,
                secondPath,
                ["--run", fixture.RunId.ToString()]);

            Assert.Equal(0, firstExit);
            Assert.Equal(0, secondExit);
            Assert.Empty(firstError);
            Assert.Empty(secondError);
            Assert.Contains("catalogue-input-sha256:", firstOutput, StringComparison.Ordinal);
            Assert.Equal(
                await File.ReadAllBytesAsync(firstPath),
                await File.ReadAllBytesAsync(secondPath));

            string json = await File.ReadAllTextAsync(firstPath);
            Assert.DoesNotContain(fixture.PrivateRoot, json, StringComparison.OrdinalIgnoreCase);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.Equal("catalogue-pilot", root.GetProperty("datasetId").GetString());
            Assert.Equal("pipeline-test-v1", root.GetProperty("pipelineVersion").GetString());
            Assert.Equal(DetectorHash, root.GetProperty("detector").GetProperty("modelHash").GetString());
            Assert.Equal(EmbedderHash, root.GetProperty("embedder").GetProperty("modelHash").GetString());
            Assert.Equal(2, root.GetProperty("embedder").GetProperty("dimensions").GetInt32());

            JsonElement export = root.GetProperty("catalogueExport");
            Assert.Equal("processing-run", export.GetProperty("scope").GetProperty("kind").GetString());
            Assert.Equal(fixture.RunId.ToString(), export.GetProperty("scope").GetProperty("processingRunId").GetString());
            Assert.Equal("split-seed-v1", export.GetProperty("seed").GetString());
            Assert.Equal(64, export.GetProperty("catalogueInputSha256").GetString()!.Length);
            Assert.Equal(fixture.RevisionIds.Count, export.GetProperty("sourceRevisions").GetArrayLength());
            Assert.Equal(1, export.GetProperty("knownPersonCount").GetInt32());
            Assert.Equal(0, export.GetProperty("fallbackTimingSampleCount").GetInt32());

            HashSet<string> galleryRevisions = RevisionIds(root.GetProperty("gallery"));
            HashSet<string> validationRevisions = RevisionIds(root.GetProperty("validation"));
            HashSet<string> testRevisions = RevisionIds(root.GetProperty("test"));
            Assert.Empty(galleryRevisions.Intersect(validationRevisions));
            Assert.Empty(galleryRevisions.Intersect(testRevisions));
            Assert.Empty(validationRevisions.Intersect(testRevisions));

            string[] allFaceIds = root.GetProperty("gallery").EnumerateArray()
                .Select(item => item.GetProperty("faceId").GetString()!)
                .Concat(root.GetProperty("validation").EnumerateArray().Select(item => item.GetProperty("faceId").GetString()!))
                .Concat(root.GetProperty("test").EnumerateArray().Select(item => item.GetProperty("faceId").GetString()!))
                .ToArray();
            Assert.Equal(allFaceIds.Length, allFaceIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(root.GetProperty("validation").EnumerateArray(), item =>
                item.GetProperty("expectedPersonId").ValueKind == JsonValueKind.Null);
            Assert.Contains(root.GetProperty("test").EnumerateArray(), item =>
                item.GetProperty("expectedPersonId").ValueKind == JsonValueKind.Null);

            string reportPath = Path.Combine(directory, "evaluation-report.json");
            StringWriter evaluationOutput = new();
            StringWriter evaluationError = new();
            int evaluationExit = await PhotoIdentity.Cli.Program.RunAsync(
                ["evaluate", "--dataset", firstPath, "--output", reportPath],
                evaluationOutput,
                evaluationError);
            Assert.Equal(0, evaluationExit);
            Assert.Empty(evaluationError.ToString());
            Assert.True(File.Exists(reportPath));

            string revisionPath = Path.Combine(directory, "revision-scope.json");
            List<string> revisionScope = [];
            foreach (AssetRevisionId revisionId in fixture.RevisionIds)
            {
                revisionScope.Add("--revision");
                revisionScope.Add(revisionId.ToString());
            }
            (int revisionExit, _, string revisionError) = await RunExportAsync(
                fixture.DatabasePath,
                revisionPath,
                revisionScope);
            Assert.Equal(0, revisionExit);
            Assert.Empty(revisionError);
            using JsonDocument revisionDocument = JsonDocument.Parse(await File.ReadAllTextAsync(revisionPath));
            Assert.Equal(
                "asset-revisions",
                revisionDocument.RootElement
                    .GetProperty("catalogueExport")
                    .GetProperty("scope")
                    .GetProperty("kind")
                    .GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_reports_insufficient_unknown_examples_without_writing_a_manifest()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            ExportFixture fixture = await CreateFixtureAsync(directory, includeUnknownPeople: false);
            string outputPath = Path.Combine(directory, "insufficient.json");
            List<string> scope = [];
            foreach (AssetRevisionId revisionId in fixture.RevisionIds)
            {
                scope.Add("--revision");
                scope.Add(revisionId.ToString());
            }

            (int exitCode, _, string error) = await RunExportAsync(
                fixture.DatabasePath,
                outputPath,
                scope);

            Assert.Equal(2, exitCode);
            Assert.Contains("Insufficient unknown examples", error, StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HashSet<string> RevisionIds(JsonElement items) =>
        items.EnumerateArray()
            .Select(item => item.GetProperty("sourceRevisionId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

    private static async Task<(int ExitCode, string Output, string Error)> RunExportAsync(
        string databasePath,
        string outputPath,
        IReadOnlyList<string> scope)
    {
        List<string> arguments =
        [
            "evaluate", "export",
            "--database", databasePath,
            "--output", outputPath,
            "--dataset-id", "catalogue-pilot",
            "--pipeline-version", "pipeline-test-v1",
            "--detector-id", DetectorId,
            "--detector-hash", DetectorHash,
            "--embedder-id", EmbedderId,
            "--embedder-hash", EmbedderHash,
            "--seed", "split-seed-v1",
        ];
        arguments.AddRange(scope);
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await PhotoIdentity.Cli.Program.RunAsync(arguments.ToArray(), output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static async Task<ExportFixture> CreateFixtureAsync(
        string directory,
        bool includeUnknownPeople)
    {
        string databasePath = Path.Combine(directory, "catalogue.db");
        string privateRoot = Path.Combine(directory, "private-photos");
        Directory.CreateDirectory(privateRoot);
        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        ProcessingRunId runId = ProcessingRunId.New();
        SourceId sourceId = SourceId.New();

        await using (SqliteConnection connection = await database.OpenConnectionAsync())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $root, $created_at);
                INSERT INTO processing_runs (
                    id, status, configuration_json, started_at_utc, completed_at_utc)
                VALUES ($run_id, 'completed', '{}', $created_at, $completed_at);
                """;
            command.Parameters.AddWithValue("$source_id", sourceId.ToString());
            command.Parameters.AddWithValue("$root", privateRoot);
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            command.Parameters.AddWithValue("$created_at", now.ToString("O"));
            command.Parameters.AddWithValue("$completed_at", now.AddMinutes(10).ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        SqliteReviewRepository reviewRepository = new(database);
        CatalogueReviewPerson knownPerson = await reviewRepository.CreatePersonAsync("Known Person", now);
        CatalogueReviewPerson unknownPerson = await reviewRepository.CreatePersonAsync("Held-out Person", now);
        List<AssetRevisionId> revisions = [];
        for (int index = 0; index < 3; index++)
        {
            (AssetRevisionId revisionId, FaceOccurrenceId faceId) = await InsertFaceAsync(
                database,
                sourceId,
                runId,
                index,
                [1f, index * 0.02f],
                now.AddMinutes(index));
            revisions.Add(revisionId);
            await reviewRepository.AssignAsync(faceId, knownPerson.Id, "test", now.AddMinutes(20 + index));
        }

        if (includeUnknownPeople)
        {
            for (int index = 0; index < 2; index++)
            {
                int ordinal = index + 3;
                (AssetRevisionId revisionId, FaceOccurrenceId faceId) = await InsertFaceAsync(
                    database,
                    sourceId,
                    runId,
                    ordinal,
                    [index * 0.02f, 1f],
                    now.AddMinutes(ordinal));
                revisions.Add(revisionId);
                await reviewRepository.AssignAsync(faceId, unknownPerson.Id, "test", now.AddMinutes(30 + index));
            }
        }

        return new ExportFixture(databasePath, privateRoot, runId, revisions);
    }

    private static async Task<(AssetRevisionId RevisionId, FaceOccurrenceId FaceId)> InsertFaceAsync(
        SqliteCatalogueDatabase database,
        SourceId sourceId,
        ProcessingRunId runId,
        int index,
        float[] vector,
        DateTimeOffset createdAt)
    {
        AssetId assetId = AssetId.New();
        AssetRevisionId revisionId = AssetRevisionId.New();
        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        FaceCropId cropId = FaceCropId.New();
        ProcessingJobId jobId = ProcessingJobId.New();
        byte[] blob = SerializeVector(vector);
        double norm = Math.Sqrt(vector.Sum(value => value * value));

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES ($asset_id, $source_id, $source_key, $created_at, $created_at);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES ($revision_id, $asset_id, $revision_hash, 100, $created_at, 'image/jpeg', 1200, 800);
            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, started_at_utc, completed_at_utc, idempotency_key)
            VALUES (
                $job_id, $run_id, $revision_id, 'succeeded', 1,
                $created_at, $started_at, $completed_at, $idempotency_key);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face_id, $revision_id, 0, $created_at);
            INSERT INTO face_observations (
                face_occurrence_id, detector_model_id, detector_model_hash, confidence,
                bounding_box_json, landmarks_json, observed_at_utc)
            VALUES (
                $face_id, $detector_id, $detector_hash, 0.99,
                '[0.1,0.1,0.5,0.5]',
                '[[0.2,0.2],[0.4,0.2],[0.3,0.3],[0.2,0.4],[0.4,0.4]]',
                $created_at);
            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256, storage_path,
                width, height, created_at_utc)
            VALUES (
                $crop_id, $face_id, 'sface-112-v1', $crop_hash, $crop_path,
                112, 112, $created_at);
            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (
                $crop_id, $embedder_id, $embedder_hash, 2, $norm, $vector, $created_at);
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$source_key", $"private/photo-{index:D2}.jpg");
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$revision_hash", index.ToString("x64"));
        command.Parameters.AddWithValue("$job_id", jobId.ToString());
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        command.Parameters.AddWithValue("$started_at", createdAt.AddSeconds(1).ToString("O"));
        command.Parameters.AddWithValue("$completed_at", createdAt.AddSeconds(2).ToString("O"));
        command.Parameters.AddWithValue("$idempotency_key", $"evaluation:{runId}:{revisionId}");
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$detector_id", DetectorId);
        command.Parameters.AddWithValue("$detector_hash", DetectorHash);
        command.Parameters.AddWithValue("$crop_id", cropId.ToString());
        command.Parameters.AddWithValue("$crop_hash", (index + 100).ToString("x64"));
        command.Parameters.AddWithValue("$crop_path", $"runs/{runId}/assets/{revisionId}/faces/face-001/aligned.png");
        command.Parameters.AddWithValue("$embedder_id", EmbedderId);
        command.Parameters.AddWithValue("$embedder_hash", EmbedderHash);
        command.Parameters.AddWithValue("$norm", norm);
        command.Parameters.AddWithValue("$vector", blob);
        command.Parameters.AddWithValue("$created_at", createdAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
        return (revisionId, faceId);
    }

    private static byte[] SerializeVector(IReadOnlyList<float> vector)
    {
        byte[] bytes = new byte[vector.Count * sizeof(float)];
        for (int index = 0; index < vector.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(vector[index]));
        }
        return bytes;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"photoidentity-catalogue-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record ExportFixture(
        string DatabasePath,
        string PrivateRoot,
        ProcessingRunId RunId,
        IReadOnlyList<AssetRevisionId> RevisionIds);
}
