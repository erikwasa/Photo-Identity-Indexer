using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    private static async Task<SeededRun> SeedRunAsync(
        SqliteCatalogueDatabase database,
        string sourceRoot,
        byte[] groupBytes,
        byte[] smallBytes,
        IReadOnlyList<DetectionSeed> groupDetections,
        IReadOnlyList<DetectionSeed> smallDetections)
    {
        DateTimeOffset now = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "R001__group.jpg"), groupBytes);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "R002__small.jpg"), smallBytes);

        SourceId sourceId = SourceId.New();
        CatalogueSource source = new(sourceId, "local-folder", sourceRoot, now);
        SqliteAssetCatalogueRepository assetRepository = new(database);
        AssetId groupAssetId = AssetId.New();
        CatalogueAssetRevision groupRevision = await assetRepository.SaveRevisionAsync(
            source,
            new CatalogueAsset(groupAssetId, sourceId, "R001__group.jpg", now),
            Revision(groupAssetId, groupBytes));
        AssetId smallAssetId = AssetId.New();
        CatalogueAssetRevision smallRevision = await assetRepository.SaveRevisionAsync(
            source,
            new CatalogueAsset(smallAssetId, sourceId, "R002__small.jpg", now),
            Revision(smallAssetId, smallBytes));

        ProcessingRunId runId = ProcessingRunId.New();
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO processing_runs (
                    id, status, configuration_json, started_at_utc, completed_at_utc)
                VALUES ($run_id, 'completed', '{}', $created_at_utc, $created_at_utc);

                INSERT INTO processing_jobs (
                    id, processing_run_id, asset_revision_id, status, attempt_count,
                    available_at_utc, started_at_utc, completed_at_utc, idempotency_key)
                VALUES
                    ($first_job_id, $run_id, $first_revision_id, 'succeeded', 1,
                     $created_at_utc, $created_at_utc, $created_at_utc, $first_key),
                    ($second_job_id, $run_id, $second_revision_id, 'succeeded', 1,
                     $created_at_utc, $created_at_utc, $created_at_utc, $second_key);
                """;
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
            command.Parameters.AddWithValue("$first_job_id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$second_job_id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$first_revision_id", groupRevision.Id.ToString());
            command.Parameters.AddWithValue("$second_revision_id", smallRevision.Id.ToString());
            command.Parameters.AddWithValue("$first_key", $"evaluation:{runId}:{groupRevision.Id}");
            command.Parameters.AddWithValue("$second_key", $"evaluation:{runId}:{smallRevision.Id}");
            await command.ExecuteNonQueryAsync();
        }

        await InsertDetectionsAsync(connection, groupRevision.Id, groupDetections, now);
        await InsertDetectionsAsync(connection, smallRevision.Id, smallDetections, now);
        return new SeededRun(runId);

        CatalogueAssetRevision Revision(AssetId assetId, byte[] bytes)
        {
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(hash),
                bytes.Length,
                now,
                "image/jpeg",
                1200,
                800);
        }
    }

    private static async Task InsertDetectionsAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        IReadOnlyList<DetectionSeed> detections,
        DateTimeOffset observedAtUtc)
    {
        for (int ordinal = 0; ordinal < detections.Count; ordinal++)
        {
            DetectionSeed detection = detections[ordinal];
            string faceId = FaceOccurrenceId.New().ToString();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, $ordinal, $observed_at_utc);

                INSERT INTO face_observations (
                    face_occurrence_id, detector_model_id, detector_model_hash,
                    confidence, bounding_box_json, landmarks_json, observed_at_utc)
                VALUES (
                    $face_id, 'candidate-detector', $model_hash,
                    $confidence, $bounding_box_json, '[]', $observed_at_utc);
                """;
            command.Parameters.AddWithValue("$face_id", faceId);
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$ordinal", ordinal);
            command.Parameters.AddWithValue("$observed_at_utc", observedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$model_hash", new string('c', 64));
            command.Parameters.AddWithValue("$confidence", detection.Confidence);
            command.Parameters.AddWithValue(
                "$bounding_box_json",
                $"[{detection.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{detection.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{detection.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)},{detection.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)}]");
            await command.ExecuteNonQueryAsync();
        }
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

    private sealed record SeededRun(ProcessingRunId RunId);

    private sealed record DetectionSeed(
        double Confidence,
        double X,
        double Y,
        double Width,
        double Height);

    private sealed class DetectorEvaluationApiFactory : PhotoIdentityApiTestFactory
    {
        public DetectorEvaluationApiFactory(string databasePath, string sessionRoot)
            : base(
                databasePath,
                builder => builder.UseSetting("PhotoIdentity:DetectorEvaluationRoot", sessionRoot))
        {
        }
    }
}
