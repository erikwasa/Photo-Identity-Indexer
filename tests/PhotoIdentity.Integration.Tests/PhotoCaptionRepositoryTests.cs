using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoCaptionRepositoryTests
{
    [Fact]
    public async Task Settings_default_off_and_caption_evidence_round_trips()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "photoidentity-caption-repository-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            SqliteCatalogueDatabase database =
                new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            SqlitePhotoCaptionRepository repository = new(database);
            PhotoCaptionEnrichmentSettings defaults =
                await repository.GetSettingsAsync();

            Assert.False(defaults.Enabled);
            Assert.Equal(PhotoCaptionLanguages.Swedish, defaults.Language);

            PhotoCaptionEnrichmentSettings updated =
                await repository.UpdateSettingsAsync(
                    true,
                    PhotoCaptionLanguages.English,
                    new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero));
            Assert.True(updated.Enabled);
            Assert.Equal(PhotoCaptionLanguages.English, updated.Language);

            AssetRevisionId revisionId = AssetRevisionId.New();
            await SeedRevisionAsync(database, revisionId);

            PhotoGeneratedCaption evidence = new(
                revisionId,
                PhotoCaptionLanguages.Swedish,
                PhotoCaptionGenerationVersion,
                "qwen2.5vl:3b",
                new string('a', 64),
                "prompt-sv-v1",
                "thumbnail-480x320",
                1024,
                "Två personer sitter vid ett bord.",
                [],
                1234.5,
                new DateTimeOffset(2026, 9, 20, 18, 5, 0, TimeSpan.Zero));

            await repository.SaveAsync(evidence);

            PhotoGeneratedCaption? persisted =
                await repository.GetLatestAsync(
                    revisionId,
                    PhotoCaptionLanguages.Swedish);

            Assert.NotNull(persisted);
            Assert.Equal(evidence.RevisionId, persisted.RevisionId);
            Assert.Equal(evidence.Content, persisted.Content);
            Assert.Equal(evidence.ModelDigest, persisted.ModelDigest);
            Assert.Equal(evidence.PromptVersion, persisted.PromptVersion);
            Assert.True(persisted.IsDisplayable);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Legacy_blocked_caption_without_text_is_retried_and_regenerated_text_is_retained()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "photoidentity-caption-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            SqliteCatalogueDatabase database =
                new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            AssetRevisionId revisionId = AssetRevisionId.New();
            await SeedRevisionAsync(database, revisionId);
            await SeedReviewProxyAsync(database, revisionId);

            SqlitePhotoCaptionRepository repository = new(database);
            string modelDigest = new('a', 64);
            const string promptVersion = "prompt-sv-v1";

            PhotoGeneratedCaption legacyBlocked = new(
                revisionId,
                PhotoCaptionLanguages.Swedish,
                PhotoCaptionGenerationVersion,
                "qwen2.5vl:3b",
                modelDigest,
                promptVersion,
                "thumbnail-480x320",
                1024,
                null,
                [GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation],
                130_000,
                new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.Zero));
            await repository.SaveAsync(legacyBlocked);

            IReadOnlyList<AssetRevisionId> retryCandidates =
                await repository.GetCandidatesAsync(
                    ReviewProxyProfileId,
                    PhotoCaptionLanguages.Swedish,
                    PhotoCaptionGenerationVersion,
                    "qwen2.5vl:3b",
                    modelDigest,
                    promptVersion,
                    "thumbnail-480x320",
                    1024,
                    8);

            Assert.Contains(revisionId, retryCandidates);

            const string blockedText = "En person står bredvid Volvo.";
            PhotoGeneratedCaption regeneratedBlocked = legacyBlocked with
            {
                Content = blockedText,
                GenerationMilliseconds = 129_500,
                GeneratedAtUtc = new DateTimeOffset(
                    2026, 9, 20, 21, 5, 0, TimeSpan.Zero),
            };
            await repository.SaveAsync(regeneratedBlocked);

            PhotoGeneratedCaption? persisted =
                await repository.GetLatestAsync(
                    revisionId,
                    PhotoCaptionLanguages.Swedish);

            Assert.NotNull(persisted);
            Assert.Equal(blockedText, persisted.Content);
            Assert.Contains(
                GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation,
                persisted.RiskFlags);
            Assert.False(persisted.IsDisplayable);
            Assert.Null(persisted.DisplayableContent);

            IReadOnlyList<AssetRevisionId> completedCandidates =
                await repository.GetCandidatesAsync(
                    ReviewProxyProfileId,
                    PhotoCaptionLanguages.Swedish,
                    PhotoCaptionGenerationVersion,
                    "qwen2.5vl:3b",
                    modelDigest,
                    promptVersion,
                    "thumbnail-480x320",
                    1024,
                    8);

            Assert.DoesNotContain(revisionId, completedCandidates);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private const string PhotoCaptionGenerationVersion = "wi-0128-photo-caption-v1";
    private const string ReviewProxyProfileId = "jpeg-1600-q78";

    private static async Task SeedRevisionAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection =
            await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (
                id, kind, root_locator, created_at_utc)
            VALUES (
                $source_id, 'local-folder', $root, $now);

            INSERT INTO assets (
                id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES (
                $asset_id, $source_id, 'caption-test.jpg', $now, $now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
            VALUES (
                $revision_id, $asset_id, $hash, 1024, $now,
                'image/jpeg', 640, 480);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$root", directoryPath(sourceId));
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$hash", new string('b', 64));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedReviewProxyAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId)
    {
        DateTimeOffset now = new(2026, 9, 20, 20, 55, 0, TimeSpan.Zero);
        SqliteArchiveReviewProxyRepository repository = new(database);
        ReviewProxyProfile profile = new(ReviewProxyProfileId, 1600, 78);
        await repository.RegisterProfileAsync(profile, now);
        await repository.RecordCompletionAsync(
            new ArchiveReviewProxyRecord(
                revisionId,
                profile.Id,
                120_000,
                new Sha256Digest(new string('c', 64)),
                640,
                480,
                now.AddMinutes(1),
                $"review/{profile.Id}/{revisionId}.jpg"));
    }

    private static string directoryPath(string sourceId) =>
        Path.Combine(Path.GetTempPath(), sourceId);
}
