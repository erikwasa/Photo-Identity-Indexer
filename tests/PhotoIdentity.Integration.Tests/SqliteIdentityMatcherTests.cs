using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteIdentityMatcherTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Regenerate_records_best_second_and_score_margin_from_confirmed_exemplars()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f, 0f], 0, now);
            FaceOccurrenceId firstExemplar = await SeedFaceAsync(database, [1f, 0f, 0f], 1, now);
            FaceOccurrenceId secondExemplar = await SeedFaceAsync(database, [0.8f, 0.6f, 0f], 2, now);

            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson firstPerson = await reviewRepository.CreatePersonAsync("Ada Lovelace", now);
            await reviewRepository.AssignAsync(
                firstExemplar,
                firstPerson.Id,
                "human:test",
                now.AddMinutes(1));

            SqliteIdentityCatalogueRepository identityRepository = new(database);
            CataloguePerson secondPerson = new(PersonId.New(), "Grace Hopper", now);
            await identityRepository.SaveHumanLabelAsync(
                secondPerson,
                new HumanLabelAssignment(
                    secondPerson.Id,
                    secondExemplar,
                    "confirmed",
                    "human:test",
                    now.AddMinutes(2)));

            SqliteIdentityMatcher matcher = new(
                database,
                new FixedTimeProvider(now.AddMinutes(3)));

            IdentityMatchSummary summary = await matcher.RegenerateAsync(
                EmbeddingModelId,
                EmbeddingModelHash);
            IReadOnlyList<CatalogueRankedIdentitySuggestion> ranked =
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash);

            Assert.Equal(new IdentityMatchSummary(1, 1, 2), summary);
            Assert.Equal(2, ranked.Count);
            Assert.Equal(1, ranked[0].Rank);
            Assert.Equal(firstPerson.Id, ranked[0].SuggestedPersonId);
            Assert.Equal(1d, ranked[0].Score, 6);
            Assert.Equal(0.2d, Assert.IsType<double>(ranked[0].ScoreMargin), 6);
            Assert.Equal(2, ranked[1].Rank);
            Assert.Equal(secondPerson.Id, ranked[1].SuggestedPersonId);
            Assert.Equal(0.8d, ranked[1].Score, 6);
            Assert.Equal(ranked[0].ScoreMargin, ranked[1].ScoreMargin);
            Assert.All(ranked, suggestion => Assert.Equal("pending", suggestion.Status));
            Assert.Empty(await identityRepository.GetHumanLabelsAsync(target));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(2, await CountAsync(connection, "person_labels"));
            Assert.Equal(2, await CountAsync(connection, "identity_suggestions"));
            Assert.Equal(2, await CountAsync(connection, "identity_suggestion_rankings"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Regenerate_filters_rejected_pairs_and_ignores_undone_or_non_confirmed_labels()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f, 0f], 0, now);
            FaceOccurrenceId firstExemplar = await SeedFaceAsync(database, [1f, 0f, 0f], 1, now);
            FaceOccurrenceId secondExemplar = await SeedFaceAsync(database, [0.8f, 0.6f, 0f], 2, now);
            FaceOccurrenceId undoneExemplar = await SeedFaceAsync(database, [0.99f, 0.14106736f, 0f], 3, now);
            FaceOccurrenceId nonConfirmedExemplar = await SeedFaceAsync(database, [0.95f, 0.3122499f, 0f], 4, now);

            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson firstPerson = await reviewRepository.CreatePersonAsync("First", now);
            await reviewRepository.AssignAsync(firstExemplar, firstPerson.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson secondPerson = await reviewRepository.CreatePersonAsync("Second", now);
            await reviewRepository.AssignAsync(secondExemplar, secondPerson.Id, "human:test", now.AddMinutes(2));
            CatalogueReviewPerson undonePerson = await reviewRepository.CreatePersonAsync("Undone", now);
            await reviewRepository.AssignAsync(undoneExemplar, undonePerson.Id, "human:test", now.AddMinutes(3));
            _ = await reviewRepository.UndoLatestAsync(undoneExemplar, "human:test", now.AddMinutes(4));

            SqliteIdentityCatalogueRepository identityRepository = new(database);
            CataloguePerson nonConfirmedPerson = new(PersonId.New(), "Not confirmed", now);
            await identityRepository.SaveHumanLabelAsync(
                nonConfirmedPerson,
                new HumanLabelAssignment(
                    nonConfirmedPerson.Id,
                    nonConfirmedExemplar,
                    "model",
                    "system:test",
                    now.AddMinutes(5)));

            SqliteIdentityMatcher matcher = new(
                database,
                new FixedTimeProvider(now.AddMinutes(6)));
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IReadOnlyList<CatalogueRankedIdentitySuggestion> firstRun =
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash);
            Assert.Equal([firstPerson.Id, secondPerson.Id], firstRun.Select(item => item.SuggestedPersonId));

            CatalogueIdentitySuggestion rejected = Assert.Single(
                (await identityRepository.GetSuggestionsAsync(target))
                    .Where(suggestion => suggestion.SuggestedPersonId == firstPerson.Id));
            _ = await identityRepository.UpdateSuggestionStatusAsync(rejected.Id, "rejected");

            await using SqliteConnection beforeConnection = await database.OpenConnectionAsync();
            long labelsBefore = await CountAsync(beforeConnection, "person_labels");
            long actionsBefore = await CountAsync(beforeConnection, "review_actions");

            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IReadOnlyList<CatalogueRankedIdentitySuggestion> rerun =
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash);
            IReadOnlyList<CatalogueIdentitySuggestion> persisted =
                await identityRepository.GetSuggestionsAsync(target);

            CatalogueRankedIdentitySuggestion remaining = Assert.Single(rerun);
            Assert.Equal(1, remaining.Rank);
            Assert.Equal(secondPerson.Id, remaining.SuggestedPersonId);
            Assert.Null(remaining.ScoreMargin);
            Assert.Contains(
                persisted,
                suggestion => suggestion.SuggestedPersonId == firstPerson.Id && suggestion.Status == "rejected");
            Assert.DoesNotContain(persisted, suggestion => suggestion.SuggestedPersonId == undonePerson.Id);
            Assert.DoesNotContain(persisted, suggestion => suggestion.SuggestedPersonId == nonConfirmedPerson.Id);
            Assert.Empty(await identityRepository.GetHumanLabelsAsync(target));

            await using SqliteConnection afterConnection = await database.OpenConnectionAsync();
            Assert.Equal(labelsBefore, await CountAsync(afterConnection, "person_labels"));
            Assert.Equal(actionsBefore, await CountAsync(afterConnection, "review_actions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
