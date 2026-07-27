using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class MatchCommandTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Regenerate_command_reports_counts_and_preserves_rejected_pairs_and_review_history()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f, 0f], 0, now);
            FaceOccurrenceId firstExemplar = await SeedFaceAsync(database, [1f, 0f, 0f], 1, now);
            FaceOccurrenceId secondExemplar = await SeedFaceAsync(database, [0.8f, 0.6f, 0f], 2, now);

            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson firstPerson = await reviewRepository.CreatePersonAsync("First", now);
            await reviewRepository.AssignAsync(firstExemplar, firstPerson.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson secondPerson = await reviewRepository.CreatePersonAsync("Second", now);
            await reviewRepository.AssignAsync(secondExemplar, secondPerson.Id, "human:test", now.AddMinutes(2));

            StringWriter firstOutput = new();
            StringWriter firstError = new();
            int firstExitCode = await RunAsync(databasePath, firstOutput, firstError);

            Assert.Equal(0, firstExitCode);
            Assert.Empty(firstError.ToString());
            Assert.Contains("model-id: sface", firstOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains($"model-hash: {EmbeddingModelHash}", firstOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("targets: 1", firstOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("suggested-targets: 1", firstOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("suggestions: 2", firstOutput.ToString(), StringComparison.Ordinal);

            SqliteIdentityCatalogueRepository identityRepository = new(database);
            CatalogueIdentitySuggestion rejected = Assert.Single(
                await identityRepository.GetSuggestionsAsync(target),
                suggestion => suggestion.SuggestedPersonId == firstPerson.Id);
            _ = await identityRepository.UpdateSuggestionStatusAsync(rejected.Id, "rejected");

            await using SqliteConnection beforeConnection = await database.OpenConnectionAsync();
            long labelsBefore = await CountAsync(beforeConnection, "person_labels");
            long actionsBefore = await CountAsync(beforeConnection, "review_actions");

            StringWriter secondOutput = new();
            StringWriter secondError = new();
            int secondExitCode = await RunAsync(databasePath, secondOutput, secondError);

            Assert.Equal(0, secondExitCode);
            Assert.Empty(secondError.ToString());
            Assert.Contains("targets: 1", secondOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("suggested-targets: 1", secondOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("suggestions: 1", secondOutput.ToString(), StringComparison.Ordinal);

            SqliteIdentityMatcher matcher = new(database);
            CatalogueRankedIdentitySuggestion remaining = Assert.Single(
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(secondPerson.Id, remaining.SuggestedPersonId);
            Assert.Equal(1, remaining.Rank);
            Assert.Null(remaining.ScoreMargin);

            IReadOnlyList<CatalogueIdentitySuggestion> persisted =
                await identityRepository.GetSuggestionsAsync(target);
            Assert.Contains(
                persisted,
                suggestion => suggestion.SuggestedPersonId == firstPerson.Id && suggestion.Status == "rejected");

            await using SqliteConnection afterConnection = await database.OpenConnectionAsync();
            Assert.Equal(labelsBefore, await CountAsync(afterConnection, "person_labels"));
            Assert.Equal(actionsBefore, await CountAsync(afterConnection, "review_actions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Regenerate_command_rejects_invalid_model_hash()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await PhotoIdentity.Cli.Program.RunAsync(
            [
                "match", "regenerate",
                "--database", "catalogue.db",
                "--embedder-id", "sface",
                "--embedder-hash", "not-a-hash",
            ],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains("64-character SHA-256", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Regenerate_command_does_not_create_a_missing_catalogue()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "missing.db");
            StringWriter output = new();
            StringWriter error = new();

            FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => RunAsync(databasePath, output, error));

            Assert.Contains("will not create an empty catalogue", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(databasePath));
            Assert.Empty(output.ToString());
            Assert.Empty(error.ToString());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static Task<int> RunAsync(string databasePath, TextWriter output, TextWriter error) =>
        PhotoIdentity.Cli.Program.RunAsync(
            [
                "match", "regenerate",
                "--database", databasePath,
                "--embedder-id", EmbeddingModelId.ToString(),
                "--embedder-hash", EmbeddingModelHash.ToString(),
            ],
            output,
            error);

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        SqliteCatalogueDatabase database,
        float[] vector,
        int index,
        DateTimeOffset now)
    {
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, $"photo-{index:000}.jpg", now);
        char revisionHashCharacter = "abcdef"[index % 6];
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string(revisionHashCharacter, 64)),
            1234,
            now,
            "image/jpeg",
            640,
            480);
        CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
        FaceCropId cropId = FaceCropId.New();
        CatalogueFaceInspection inspection = new(
            new CatalogueFaceOccurrence(occurrenceId, persistedRevision.Id, 0, now.AddMinutes(index)),
            new CatalogueFaceObservation(
                occurrenceId,
                new ModelId("yunet"),
                new Sha256Digest(new string('b', 64)),
                0.95,
                new NormalizedBoundingBox(0.1, 0.1, 0.4, 0.5),
                CreateLandmarks(),
                now.AddMinutes(index)),
            new CatalogueFaceCrop(
                cropId,
                occurrenceId,
                new AlignmentProtocolId("sface-five-point-v1"),
                new Sha256Digest(new string('c', 64)),
                $"faces/{index:000}/aligned.png",
                112,
                112,
                now.AddMinutes(index)),
            new CatalogueFaceEmbedding(
                cropId,
                EmbeddingModelId,
                EmbeddingModelHash,
                new EmbeddingVector(vector),
                now.AddMinutes(index)));

        CatalogueFaceInspection persisted = await new SqliteFaceCatalogueRepository(database).SaveInspectionAsync(
            inspection.Occurrence,
            inspection.Observation,
            inspection.Crop,
            inspection.Embedding);
        return persisted.Occurrence.Id;
    }

    private static NormalizedFaceLandmarks CreateLandmarks() =>
        new(
            new NormalizedPoint(0.2, 0.25),
            new NormalizedPoint(0.4, 0.25),
            new NormalizedPoint(0.3, 0.35),
            new NormalizedPoint(0.24, 0.48),
            new NormalizedPoint(0.36, 0.48));

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
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
}
