using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class IdentityMatchRegenerationTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Durable_run_blocks_duplicate_reclaims_running_target_and_stales_after_identity_change()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            DateTimeOffset now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            Seed seed = await SeedThreeFacesAsync(database, now);
            SqliteIdentityMatchRegenerationRepository repository = new(database);

            CatalogueIdentityMatchRegenerationRun run = await repository.StartAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policyVersion: 1,
                requestedBy: "test:regeneration",
                requestedAtUtc: now.AddMinutes(10));
            Assert.Equal(1, run.TargetCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StartAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policyVersion: 1,
                requestedBy: "test:duplicate",
                requestedAtUtc: now.AddMinutes(11)));

            CatalogueIdentityMatchRegenerationTarget first = Assert.IsType<CatalogueIdentityMatchRegenerationTarget>(
                await repository.ClaimNextTargetAsync(run.Id, now.AddMinutes(12)));
            Assert.Equal(seed.Target, first.FaceOccurrenceId);
            Assert.Equal("running", first.Status);

            // Simulate an application restart: a durable running target is reclaimed rather than lost.
            CatalogueIdentityMatchRegenerationTarget reclaimed = Assert.IsType<CatalogueIdentityMatchRegenerationTarget>(
                await repository.ClaimNextTargetAsync(run.Id, now.AddMinutes(13)));
            Assert.Equal(first.FaceOccurrenceId, reclaimed.FaceOccurrenceId);
            Assert.Equal(first.Ordinal, reclaimed.Ordinal);

            CatalogueReviewPerson correction = await new SqliteReviewRepository(database)
                .CreatePersonAsync("Correction", now.AddMinutes(14));
            await new SqliteReviewRepository(database).AssignAsync(
                seed.Target,
                correction.Id,
                "human:test",
                now.AddMinutes(15));

            Assert.Null(await repository.ClaimNextTargetAsync(run.Id, now.AddMinutes(16)));
            CatalogueIdentityMatchRegenerationRun stale = Assert.IsType<CatalogueIdentityMatchRegenerationRun>(
                await repository.GetLatestAsync(EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(IdentityMatchRegenerationStatuses.Stale, stale.Status);
            Assert.False(stale.IsActive);
            Assert.NotNull(stale.CompletedAtUtc);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Worker_scores_short_targets_then_completes_with_persisted_counts()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            DateTimeOffset now = new(2026, 8, 11, 1, 0, 0, TimeSpan.Zero);
            FixedTimeProvider clock = new(now.AddMinutes(10));
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            Seed seed = await SeedThreeFacesAsync(database, now);
            SqliteIdentityMatchRegenerationRepository repository = new(database);
            IdentitySuggestionPolicy policy = await new SqliteIdentitySuggestionPolicyRepository(database, clock)
                .GetAsync(EmbeddingModelId, EmbeddingModelHash);
            CatalogueIdentityMatchRegenerationRun run = await repository.StartAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policy.Version,
                "test:worker",
                clock.GetUtcNow());

            IdentityMatchRegenerationHostedService worker = new(
                repository,
                new SqliteIdentityMatchRegenerationScorer(database, clock),
                new SqliteIdentitySuggestionPolicyRepository(database, clock),
                new SqliteIdentityAutoAssignmentService(database, clock),
                clock);

            Assert.True(await worker.AdvanceOnceAsync());
            CatalogueIdentityMatchRegenerationRun progressed = Assert.IsType<CatalogueIdentityMatchRegenerationRun>(
                await repository.GetLatestAsync(EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(1, progressed.ProcessedTargetCount);
            Assert.Equal(1, progressed.SuggestedTargetCount);
            Assert.Equal(2, progressed.SuggestionCount);
            Assert.Equal(IdentityMatchRegenerationStatuses.Running, progressed.Status);

            Assert.True(await worker.AdvanceOnceAsync());
            CatalogueIdentityMatchRegenerationRun completed = Assert.IsType<CatalogueIdentityMatchRegenerationRun>(
                await repository.GetLatestAsync(EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(run.Id, completed.Id);
            Assert.Equal(IdentityMatchRegenerationStatuses.Completed, completed.Status);
            Assert.Equal(1, completed.TargetCount);
            Assert.Equal(1, completed.ProcessedTargetCount);
            Assert.Equal(1, completed.SuggestedTargetCount);
            Assert.Equal(2, completed.SuggestionCount);
            Assert.Equal(0, completed.AutomaticallyAssignedCount);
            Assert.Equal(0, completed.ErrorCount);

            CatalogueReviewIdentitySuggestion rank1 = Assert.Single(
                await new SqliteReviewSuggestionRepository(database).GetSuggestionsAsync(seed.Target),
                suggestion => suggestion.Rank == 1);
            Assert.Equal(seed.PrimaryPerson.Id, rank1.Person.Id);
            Assert.Equal("pending", rank1.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Worker_applies_captured_high_policy_only_after_all_targets_are_scored()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            DateTimeOffset now = new(2026, 8, 11, 2, 0, 0, TimeSpan.Zero);
            FixedTimeProvider clock = new(now.AddMinutes(10));
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            Seed seed = await SeedThreeFacesAsync(database, now);
            SqliteIdentitySuggestionPolicyRepository policies = new(database, clock);
            IdentitySuggestionPolicy policy = await policies.UpdateAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                autoAssignEnabled: true,
                highScoreThreshold: 0.90,
                highMarginThreshold: 0.50,
                mediumScoreThreshold: 0.50,
                updatedBy: "test:policy");

            SqliteIdentityMatchRegenerationRepository repository = new(database);
            CatalogueIdentityMatchRegenerationRun run = await repository.StartAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                policy.Version,
                "test:auto-worker",
                clock.GetUtcNow());
            IdentityMatchRegenerationHostedService worker = new(
                repository,
                new SqliteIdentityMatchRegenerationScorer(database, clock),
                policies,
                new SqliteIdentityAutoAssignmentService(database, clock),
                clock);

            Assert.True(await worker.AdvanceOnceAsync());
            Assert.Null(await ReadActiveAssignmentAsync(database, seed.Target));

            Assert.True(await worker.AdvanceOnceAsync());
            CatalogueIdentityMatchRegenerationRun completed = Assert.IsType<CatalogueIdentityMatchRegenerationRun>(
                await repository.GetLatestAsync(EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(run.Id, completed.Id);
            Assert.Equal(IdentityMatchRegenerationStatuses.Completed, completed.Status);
            Assert.Equal(1, completed.AutomaticallyAssignedCount);

            ActiveAssignment assignment = Assert.IsType<ActiveAssignment>(
                await ReadActiveAssignmentAsync(database, seed.Target));
            Assert.Equal(seed.PrimaryPerson.Id, assignment.PersonId);
            Assert.Equal(SqliteIdentityAutoAssignmentService.AutomaticActor, assignment.Actor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<Seed> SeedThreeFacesAsync(SqliteCatalogueDatabase database, DateTimeOffset now)
    {
        FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f], 0, now);
        FaceOccurrenceId primaryExemplar = await SeedFaceAsync(database, [1f, 0f], 1, now);
        FaceOccurrenceId secondaryExemplar = await SeedFaceAsync(database, [-1f, 0f], 2, now);

        SqliteReviewRepository reviews = new(database);
        CatalogueReviewPerson primary = await reviews.CreatePersonAsync("Primary", now.AddMinutes(3));
        await reviews.AssignAsync(primaryExemplar, primary.Id, "human:test", now.AddMinutes(4));
        CatalogueReviewPerson secondary = await reviews.CreatePersonAsync("Secondary", now.AddMinutes(5));
        await reviews.AssignAsync(secondaryExemplar, secondary.Id, "human:test", now.AddMinutes(6));
        return new Seed(target, primary, secondary);
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
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string("abcdef"[index % 6], 64)),
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
        return await reader.ReadAsync()
            ? new ActiveAssignment(PersonId.From(Guid.Parse(reader.GetString(0))), reader.GetString(1))
            : null;
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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Seed(
        FaceOccurrenceId Target,
        CatalogueReviewPerson PrimaryPerson,
        CatalogueReviewPerson SecondaryPerson);

    private sealed record ActiveAssignment(PersonId PersonId, string Actor);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
