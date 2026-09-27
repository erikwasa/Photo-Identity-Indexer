using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Shared processing fixture helpers retained after the SQLite-only processing repository tests
/// were retired by WI-0148. The historical class name is intentionally temporary so existing
/// higher-level batch and resumability tests can keep their fixture calls while running on the
/// PostgreSQL test catalogue.
/// </summary>
internal static class SqliteProcessingRepositoryTests
{
    internal static CatalogueProcessingRun CreateRun(DateTimeOffset now) =>
        new(
            ProcessingRunId.New(),
            ProcessingRunStatus.Pending,
            """{"detector":"yunet","embedder":"sface"}""",
            now);

    internal static CatalogueProcessingJob CreateJob(
        ProcessingRunId runId,
        AssetRevisionId revisionId,
        DateTimeOffset availableAtUtc) =>
        new(
            ProcessingJobId.New(),
            runId,
            revisionId,
            ProcessingJobStatus.Queued,
            attemptCount: 0,
            availableAtUtc);

    internal static async Task<IReadOnlyList<CatalogueAssetRevision>> SeedRevisionsAsync(
        SqliteCatalogueDatabase database,
        int count)
    {
        DateTimeOffset now = new(2026, 7, 26, 9, 55, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, "photo.jpg", now);
        SqliteAssetCatalogueRepository repository = new(database);
        List<CatalogueAssetRevision> revisions = [];

        for (int index = 0; index < count; index++)
        {
            string hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index)))
                .ToLowerInvariant();
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(hash),
                1234 + index,
                now.AddTicks(index),
                "image/jpeg",
                640,
                480);
            revisions.Add(await repository.SaveRevisionAsync(source, asset, revision));
        }

        return revisions;
    }

    internal static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
