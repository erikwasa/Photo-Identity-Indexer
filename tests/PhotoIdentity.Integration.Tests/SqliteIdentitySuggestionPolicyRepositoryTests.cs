using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteIdentitySuggestionPolicyRepositoryTests
{
    private static readonly ModelId ModelA = new("sface-a");
    private static readonly Sha256Digest ModelHashA = new(new string('a', 64));
    private static readonly ModelId ModelB = new("sface-b");
    private static readonly Sha256Digest ModelHashB = new(new string('b', 64));

    [Fact]
    public async Task Default_policy_is_persisted_with_auto_assignment_disabled()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteIdentitySuggestionPolicyRepository repository = new(
                database,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));

            IdentitySuggestionPolicy first = await repository.GetAsync(ModelA, ModelHashA);
            IdentitySuggestionPolicy second = await repository.GetAsync(ModelA, ModelHashA);

            Assert.Equal(1, first.Version);
            Assert.False(first.AutoAssignEnabled);
            Assert.Equal(IdentitySuggestionPolicy.DefaultHighScoreThreshold, first.HighScoreThreshold);
            Assert.Equal(IdentitySuggestionPolicy.DefaultHighMarginThreshold, first.HighMarginThreshold);
            Assert.Equal(IdentitySuggestionPolicy.DefaultMediumScoreThreshold, first.MediumScoreThreshold);
            Assert.Equal(SqliteIdentitySuggestionPolicyRepository.DefaultActor, first.UpdatedBy);
            Assert.Equal(first, second);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Policy_update_persists_thresholds_and_increments_version_only_when_values_change()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteIdentitySuggestionPolicyRepository repository = new(
                database,
                new FixedTimeProvider(now));

            IdentitySuggestionPolicy updated = await repository.UpdateAsync(
                ModelA,
                ModelHashA,
                autoAssignEnabled: true,
                highScoreThreshold: 0.82,
                highMarginThreshold: 0.14,
                mediumScoreThreshold: 0.55,
                actor: "human:test");
            IdentitySuggestionPolicy unchanged = await repository.UpdateAsync(
                ModelA,
                ModelHashA,
                autoAssignEnabled: true,
                highScoreThreshold: 0.82,
                highMarginThreshold: 0.14,
                mediumScoreThreshold: 0.55,
                actor: "human:other");
            IdentitySuggestionPolicy persisted = await repository.GetAsync(ModelA, ModelHashA);

            Assert.Equal(2, updated.Version);
            Assert.True(updated.AutoAssignEnabled);
            Assert.Equal(0.82, updated.HighScoreThreshold, 10);
            Assert.Equal(0.14, updated.HighMarginThreshold, 10);
            Assert.Equal(0.55, updated.MediumScoreThreshold, 10);
            Assert.Equal("human:test", updated.UpdatedBy);
            Assert.Equal(updated, unchanged);
            Assert.Equal(updated, persisted);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Policy_values_and_versions_are_isolated_by_exact_model_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteIdentitySuggestionPolicyRepository repository = new(database);

            IdentitySuggestionPolicy updatedA = await repository.UpdateAsync(
                ModelA,
                ModelHashA,
                autoAssignEnabled: true,
                highScoreThreshold: 0.86,
                highMarginThreshold: 0.18,
                mediumScoreThreshold: 0.61,
                actor: "human:model-a");
            IdentitySuggestionPolicy defaultB = await repository.GetAsync(ModelB, ModelHashB);
            IdentitySuggestionPolicy persistedA = await repository.GetAsync(ModelA, ModelHashA);

            Assert.Equal(2, updatedA.Version);
            Assert.True(updatedA.AutoAssignEnabled);
            Assert.Equal(updatedA, persistedA);

            Assert.Equal(1, defaultB.Version);
            Assert.False(defaultB.AutoAssignEnabled);
            Assert.Equal(IdentitySuggestionPolicy.DefaultHighScoreThreshold, defaultB.HighScoreThreshold);
            Assert.Equal(IdentitySuggestionPolicy.DefaultHighMarginThreshold, defaultB.HighMarginThreshold);
            Assert.Equal(IdentitySuggestionPolicy.DefaultMediumScoreThreshold, defaultB.MediumScoreThreshold);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Policy_rejects_medium_threshold_above_high_threshold()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteIdentitySuggestionPolicyRepository repository = new(database);

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                () => repository.UpdateAsync(
                    ModelA,
                    ModelHashA,
                    autoAssignEnabled: false,
                    highScoreThreshold: 0.70,
                    highMarginThreshold: 0.10,
                    mediumScoreThreshold: 0.80,
                    actor: "human:test"));

            Assert.Contains("Medium score threshold", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Theory]
    [InlineData(0.90, 0.20, "high")]
    [InlineData(0.90, 0.09, "medium")]
    [InlineData(0.90, null, "medium")]
    [InlineData(0.60, 0.50, "medium")]
    [InlineData(0.49, 0.50, "low")]
    public void Classification_requires_both_high_score_and_rank_gap(
        double score,
        double? margin,
        string expected)
    {
        IdentitySuggestionPolicy policy = new(
            Version: 3,
            AutoAssignEnabled: true,
            HighScoreThreshold: 0.80,
            HighMarginThreshold: 0.10,
            MediumScoreThreshold: 0.50,
            UpdatedBy: "human:test",
            UpdatedAtUtc: new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(expected, policy.Classify(score, margin));
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
