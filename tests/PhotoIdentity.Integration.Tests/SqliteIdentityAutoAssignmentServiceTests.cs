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
    public async Task Default_persisted_policy_leaves_high_confidence_suggestion_pending()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId exemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);
            FaceOccurrenceId decoyExemplar = await SeedFaceAsync(database, [-1f, 0f], 2, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson person = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(exemplar, person.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson decoy = await reviews.CreatePersonAsync("Decoy", now.AddMinutes(2));
            await reviews.AssignAsync(decoyExemplar, decoy.Id, "human:test", now.AddMinutes(3));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);

            IdentityAutoAssignmentSummary summary = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(EmbeddingModelId, EmbeddingModelHash);

            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), summary);
            CatalogueReviewIdentitySuggestion suggestion = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(target),
                candidate => candidate.Rank == 1);
            Assert.Equal("pending", suggestion.Status);
            Assert.Null(await ReadActiveAssignmentAsync(database, target));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Enabled_policy_accepts_score_and_margin_boundaries_with_provenance_and_is_idempotent()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId exemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);
            FaceOccurrenceId decoyExemplar = await SeedFaceAsync(database, [0f, 1f], 2, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson person = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(exemplar, person.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson decoy = await reviews.CreatePersonAsync("Decoy", now.AddMinutes(2));
            await reviews.AssignAsync(decoyExemplar, decoy.Id, "human:test", now.AddMinutes(3));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);

            SqliteIdentityAutoAssignmentService service = new(
                database,
                new FixedTimeProvider(now.AddMinutes(4)));
            IdentitySuggestionPolicy policy = CreatePolicy(
                enabled: true,
                highScore: 1.0,
                highMargin: 1.0,
                mediumScore: 0.50,
                version: 7);
            IdentityAutoAssignmentSummary first = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policy);
            IdentityAutoAssignmentSummary second = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policy);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), first);
            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), second);

            CatalogueReviewIdentitySuggestion suggestion = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(target),
                candidate => candidate.Rank == 1);
            Assert.Equal("accepted", suggestion.Status);
            CatalogueReviewSuggestionAction action = Assert.IsType<CatalogueReviewSuggestionAction>(
                suggestion.LatestAction);
            string note = Assert.IsType<string>(action.Note);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, action.Actor);
            Assert.Contains("model-id=sface", note, StringComparison.Ordinal);
            Assert.Contains($"model-hash={EmbeddingModelHash}", note, StringComparison.Ordinal);
            Assert.Contains("score=1", note, StringComparison.Ordinal);
            Assert.Contains("rank1-rank2-margin=1", note, StringComparison.Ordinal);
            Assert.Contains("policy-version=7", note, StringComparison.Ordinal);
            Assert.Contains("high-score-threshold=1", note, StringComparison.Ordinal);
            Assert.Contains("high-margin-threshold=1", note, StringComparison.Ordinal);

            ActiveAssignment assignment = Assert.IsType<ActiveAssignment>(
                await ReadActiveAssignmentAsync(database, target));
            Assert.Equal(person.Id, assignment.PersonId);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, assignment.Actor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Strong_rank1_score_is_not_high_when_rank2_gap_is_too_small()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
            FaceOccurrenceId firstExemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);
            FaceOccurrenceId secondExemplar = await SeedFaceAsync(database, [0.8f, 0.6f], 2, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson firstPerson = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(firstExemplar, firstPerson.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson secondPerson = await reviews.CreatePersonAsync("Second", now.AddMinutes(2));
            await reviews.AssignAsync(secondExemplar, secondPerson.Id, "human:test", now.AddMinutes(3));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            CatalogueRankedIdentitySuggestion rank1 = Assert.Single(
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash),
                suggestion => suggestion.Rank == 1);
            Assert.True(rank1.Score >= 0.90);
            Assert.NotNull(rank1.ScoreMargin);
            Assert.True(rank1.ScoreMargin < 0.25);

            IdentitySuggestionPolicy policy = CreatePolicy(
                enabled: true,
                highScore: 0.90,
                highMargin: 0.25,
                mediumScore: 0.50);
            Assert.Equal(IdentitySuggestionConfidenceGroups.Medium, policy.Classify(rank1.Score, rank1.ScoreMargin));

            IdentityAutoAssignmentSummary summary = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(EmbeddingModelId, EmbeddingModelHash, policy);

            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), summary);
            Assert.Null(await ReadActiveAssignmentAsync(database, target));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Missing_rank2_gap_does_not_qualify_for_high_auto_assignment()
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
            CatalogueRankedIdentitySuggestion rank1 = Assert.Single(
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(1, rank1.Rank);
            Assert.Equal(1.0, rank1.Score, 10);
            Assert.Null(rank1.ScoreMargin);

            IdentitySuggestionPolicy policy = CreatePolicy(
                enabled: true,
                highScore: 0.90,
                highMargin: 0,
                mediumScore: 0.50);
            Assert.Equal(IdentitySuggestionConfidenceGroups.Medium, policy.Classify(rank1.Score, rank1.ScoreMargin));

            IdentityAutoAssignmentSummary summary = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(EmbeddingModelId, EmbeddingModelHash, policy);

            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), summary);
            Assert.Null(await ReadActiveAssignmentAsync(database, target));
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
            FaceOccurrenceId decoyExemplar = await SeedFaceAsync(database, [-1f, 0f], 2, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson suggestedPerson = await reviews.CreatePersonAsync("Suggested", now);
            await reviews.AssignAsync(exemplar, suggestedPerson.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson decoy = await reviews.CreatePersonAsync("Decoy", now.AddMinutes(2));
            await reviews.AssignAsync(decoyExemplar, decoy.Id, "human:test", now.AddMinutes(3));
            CatalogueReviewPerson manualPerson = await reviews.CreatePersonAsync("Manual", now.AddMinutes(4));

            SqliteIdentityMatcher matcher = new(database);
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            await reviews.AssignAsync(target, manualPerson.Id, "human:manual", now.AddMinutes(5));

            IdentityAutoAssignmentSummary summary = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(
                    EmbeddingModelId,
                    EmbeddingModelHash,
                    CreatePolicy(enabled: true, highScore: 0.70, highMargin: 0.10, mediumScore: 0.50));

            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), summary);
            ActiveAssignment assignment = Assert.IsType<ActiveAssignment>(
                await ReadActiveAssignmentAsync(database, target));
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
            FaceOccurrenceId decoyExemplar = await SeedFaceAsync(database, [-1f, 0f], 1, now);
            FaceOccurrenceId firstTarget = await SeedFaceAsync(database, [0.8f, 0.6f], 2, now);
            FaceOccurrenceId secondTarget = await SeedFaceAsync(database, [0f, 1f], 3, now);

            SqliteReviewRepository reviews = new(database);
            CatalogueReviewPerson person = await reviews.CreatePersonAsync("First", now);
            await reviews.AssignAsync(originalExemplar, person.Id, "human:test", now.AddMinutes(1));
            CatalogueReviewPerson decoy = await reviews.CreatePersonAsync("Decoy", now.AddMinutes(2));
            await reviews.AssignAsync(decoyExemplar, decoy.Id, "human:test", now.AddMinutes(3));

            SqliteIdentityMatcher matcher = new(database);
            SqliteIdentityAutoAssignmentService service = new(database);
            IdentitySuggestionPolicy policy = CreatePolicy(
                enabled: true,
                highScore: 0.50,
                highMargin: 0.50,
                mediumScore: 0.25);

            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IdentityAutoAssignmentSummary firstPass = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policy);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), firstPass);
            Assert.NotNull(await ReadActiveAssignmentAsync(database, firstTarget));
            Assert.Null(await ReadActiveAssignmentAsync(database, secondTarget));

            CatalogueReviewIdentitySuggestion secondTargetFirstSuggestion = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(secondTarget),
                suggestion => suggestion.Rank == 1);
            Assert.True(secondTargetFirstSuggestion.Score < policy.HighScoreThreshold);

            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            IdentityAutoAssignmentSummary secondPass = await service.ApplyAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policy);

            Assert.Equal(new IdentityAutoAssignmentSummary(1, 1, 0), secondPass);
            ActiveAssignment propagated = Assert.IsType<ActiveAssignment>(
                await ReadActiveAssignmentAsync(database, secondTarget));
            Assert.Equal(person.Id, propagated.PersonId);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, propagated.Actor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static IdentitySuggestionPolicy CreatePolicy(
        bool enabled,
        double highScore,
        double highMargin,
        double mediumScore,
        int version = 1) =>
        new(
            version,
            enabled,
            highScore,
            highMargin,
            mediumScore,
            "test:policy",
            new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.Zero));

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
