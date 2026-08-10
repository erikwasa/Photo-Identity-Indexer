using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class IdentityAutoAssignmentManualSupersessionTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Manual_reassignment_supersedes_automatic_assignment_for_later_matching()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 18, 0, 0, TimeSpan.Zero);

            FaceOccurrenceId firstExemplar = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId secondExemplar = await SeedFaceAsync(database, [0f, 1f], 1, now);
            FaceOccurrenceId automaticTarget = await SeedFaceAsync(database, [0.8f, 0.6f], 2, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson firstPerson = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(firstExemplar, firstPerson.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson secondPerson = await reviews.CreatePersonAsync("Second", now.AddMinutes(2));
            await reviews.AssignAsync(secondExemplar, secondPerson.Id, "human:test", now.AddMinutes(3));

            SqliteIdentitySuggestionPolicyRepository policies = new(database);
            IdentitySuggestionPolicy enabledPolicy = await policies.UpdateAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                autoAssignEnabled: true,
                highScoreThreshold: 0.75,
                highMarginThreshold: 0.10,
                mediumScoreThreshold: 0.50,
                actor: "test:policy");

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IdentityAutoAssignmentSummary automatic = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(EmbeddingModelId, EmbeddingModelHash);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), automatic);
            CatalogueReviewAction automaticAction = Assert.Single(
                await reviews.GetActionsAsync(automaticTarget),
                action => action.Kind == CatalogueReviewActionKinds.Assign);
            Assert.Equal(firstPerson.Id, automaticAction.PersonId);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, automaticAction.Actor);
            string automaticNote = Assert.IsType<string>(automaticAction.Note);
            Assert.Contains($"policy-version={enabledPolicy.Version}", automaticNote, StringComparison.Ordinal);

            IdentitySuggestionPolicy tightenedPolicy = await policies.UpdateAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                autoAssignEnabled: false,
                highScoreThreshold: 0.95,
                highMarginThreshold: 0.50,
                mediumScoreThreshold: 0.80,
                actor: "human:policy-change");
            Assert.True(tightenedPolicy.Version > enabledPolicy.Version);

            IReadOnlyList<CatalogueReviewAction> afterPolicyChange = await reviews.GetActionsAsync(automaticTarget);
            CatalogueReviewAction retainedAutomatic = Assert.Single(
                afterPolicyChange,
                action => action.Kind == CatalogueReviewActionKinds.Assign);
            Assert.Equal(automaticAction.Id, retainedAutomatic.Id);
            Assert.Equal(firstPerson.Id, retainedAutomatic.PersonId);

            CatalogueReviewAction manualAction = await reviews.AssignAsync(
                automaticTarget,
                secondPerson.Id,
                "human:manual",
                now.AddMinutes(5),
                "Correct automatic identity.");
            Assert.Equal(secondPerson.Id, manualAction.PersonId);

            IReadOnlyList<CatalogueReviewAction> history = await reviews.GetActionsAsync(automaticTarget);
            Assert.Contains(
                history,
                action => action.PersonId == firstPerson.Id
                    && action.Actor == SqliteIdentityAutoAssignmentService.AutomaticActor);
            Assert.Contains(
                history,
                action => action.PersonId == secondPerson.Id
                    && action.Actor == "human:manual");

            FaceOccurrenceId laterProbe = await SeedFaceAsync(database, [0.8f, 0.6f], 3, now.AddMinutes(6));
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);

            CatalogueRankedIdentitySuggestion top = Assert.Single(
                await matcher.GetRankedSuggestionsAsync(laterProbe, EmbeddingModelId, EmbeddingModelHash),
                suggestion => suggestion.Rank == 1);
            Assert.Equal(secondPerson.Id, top.SuggestedPersonId);
            Assert.True(top.Score > 0.99);
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
        CatalogueAsset asset = new(assetId, sourceId, $"manual-supersession-{index:000}.jpg", now);
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
                $"faces/manual-supersession/{index:000}/aligned.png",
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
