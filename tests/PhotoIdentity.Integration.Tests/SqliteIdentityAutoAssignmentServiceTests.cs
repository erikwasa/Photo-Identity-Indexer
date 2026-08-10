using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteIdentityAutoAssignmentServiceTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Disabled_options_leave_high_confidence_suggestion_pending()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId exemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson person = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(exemplar, person.Id, "human:test", now.AddMinutes(1));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);

            SqliteIdentityAutoAssignmentService service = new(database);
            IdentityAutoAssignmentSummary summary = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                new IdentityAutoAssignmentOptions());

            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), summary);
            CatalogueReviewIdentitySuggestion suggestion = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(target));
            Assert.Equal("pending", suggestion.Status);
            Assert.Null(await ReadActiveAssignmentAsync(database, target));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Enabled_options_accept_threshold_boundary_with_provenance_and_are_idempotent()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId exemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson person = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(exemplar, person.Id, "human:test", now.AddMinutes(1));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);

            SqliteIdentityAutoAssignmentService service = new(
                database,
                new FixedTimeProvider(now.AddMinutes(2)));
            IdentityAutoAssignmentOptions options = new(Enabled: true, HighConfidenceThreshold: 1.0);
            IdentityAutoAssignmentSummary first = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                options);
            IdentityAutoAssignmentSummary second = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                options);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), first);
            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), second);

            CatalogueReviewIdentitySuggestion suggestion = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(target));
            Assert.Equal("accepted", suggestion.Status);
            Assert.NotNull(suggestion.LatestAction);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, suggestion.LatestAction.Actor);
            Assert.Contains("model-id=sface", suggestion.LatestAction.Note, StringComparison.Ordinal);
            Assert.Contains($"model-hash={EmbeddingModelHash}", suggestion.LatestAction.Note, StringComparison.Ordinal);
            Assert.Contains("high-confidence-threshold=1", suggestion.LatestAction.Note, StringComparison.Ordinal);

            ActiveAssignment? assignment = await ReadActiveAssignmentAsync(database, target);
            Assert.NotNull(assignment);
            Assert.Equal(person.Id, assignment.PersonId);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, assignment.Actor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Manual_assignment_is_never_overwritten()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId exemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson suggestedPerson = await reviews.CreatePersonAsync("Suggested", now);
            await reviews.AssignAsync(exemplar, suggestedPerson.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson manualPerson = await reviews.CreatePersonAsync("Manual", now.AddMinutes(2));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            await reviews.AssignAsync(target, manualPerson.Id, "human:manual", now.AddMinutes(3));

            IdentityAutoAssignmentSummary summary = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(
                    EmbeddingModelId,
                    EmbeddingModelHash,
                    new IdentityAutoAssignmentOptions(Enabled: true, HighConfidenceThreshold: 0.70));

            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), summary);
            ActiveAssignment? assignment = await ReadActiveAssignmentAsync(database, target);
            Assert.NotNull(assignment);
            Assert.Equal(manualPerson.Id, assignment.PersonId);
            Assert.Equal("human:manual", assignment.Actor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Auto_assignments_only_become_exemplars_on_later_regeneration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId originalExemplar = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId firstTarget = await SeedFaceAsync(database, [0.8f, 0.6f], 1, now);
            FaceOccurrenceId secondTarget = await SeedFaceAsync(database, [0f, 1f], 2, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson person = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(originalExemplar, person.Id, "human:test", now.AddMinutes(1));

            SqliteIdentityMatcher matcher = new(database);
            SqliteIdentityAutoAssignmentService service = new(database);
            IdentityAutoAssignmentOptions options = new(Enabled: true, HighConfidenceThreshold: 0.50);

            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IdentityAutoAssignmentSummary firstPass = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                options);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), firstPass);
            Assert.NotNull(await ReadActiveAssignmentAsync(database, firstTarget));
            Assert.Null(await ReadActiveAssignmentAsync(database, secondTarget));

            CatalogueReviewIdentitySuggestion secondTargetFirstSuggestion = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(secondTarget));
            Assert.True(secondTargetFirstSuggestion.Score < options.HighConfidenceThreshold);

            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IdentityAutoAssignmentSummary secondPass = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                options);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), secondPass);
            ActiveAssignment? propagated = await ReadActiveAssignmentAsync(database, secondTarget);
            Assert.NotNull(propagated);
            Assert.Equal(person.Id, propagated.PersonId);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, propagated.Actor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<ActiveAssignment?> ReadActiveAssignmentAsync(
        SqliteCatalogueDatabase database,
        FaceOccurrenceId faceOccurrenceId)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT person_id, actor
            FROM review_actions
            WHERE face_occurrence_id = $face_occurrence_id
              AND action_kind = 'assign'
              AND reversed_at_utc IS NULL
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ActiveAssignment(
            PersonId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1));
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

    private sealed record ActiveAssignment(PersonId PersonId, string Actor);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
