using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class SourceCopyPurgeReviewHistoryTests
{
    [Fact]
    public async Task Purge_deletes_undo_history_without_violating_restrictive_review_links()
    {
        string root = Path.Combine(Path.GetTempPath(), $"photoidentity-purge-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(root, "catalogue.db"));
            await database.InitializeAsync();

            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            PersonId personId = PersonId.New();
            DateTimeOffset now = new(2026, 9, 26, 1, 0, 0, TimeSpan.Zero);
            const string sourceKey = "Private/review-history.jpg";

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES ($source_id, 'local-folder', 'private-root', $now);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES ($asset_id, $source_id, $source_key, $now, $now);
                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES ($revision_id, $asset_id, $hash, 4, $now, 'image/jpeg');
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($face_id, $revision_id, 0, $now);
                    INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                    VALUES ($person_id, 'Shared Person', $now, NULL);
                    INSERT INTO person_labels (
                        person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note)
                    VALUES ($person_id, $face_id, 'manual', 'purge-test', $now, NULL);
                    INSERT INTO review_actions (
                        face_occurrence_id, action_kind, person_id, person_label_id,
                        actor, note, created_at_utc, reversed_at_utc, reverses_action_id)
                    VALUES (
                        $face_id,
                        'assign',
                        $person_id,
                        (SELECT id FROM person_labels
                         WHERE person_id = $person_id AND face_occurrence_id = $face_id),
                        'purge-test', NULL, $now, $now, NULL);
                    INSERT INTO review_actions (
                        face_occurrence_id, action_kind, person_id, person_label_id,
                        actor, note, created_at_utc, reversed_at_utc, reverses_action_id)
                    VALUES (
                        $face_id,
                        'undo',
                        NULL,
                        NULL,
                        'purge-test', NULL, $now, NULL,
                        (SELECT id FROM review_actions
                         WHERE face_occurrence_id = $face_id AND action_kind = 'assign'));
                    """;
                seed.Parameters.AddWithValue("$source_id", sourceId.ToString());
                seed.Parameters.AddWithValue("$asset_id", assetId.ToString());
                seed.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                seed.Parameters.AddWithValue("$face_id", faceId.ToString());
                seed.Parameters.AddWithValue("$person_id", personId.ToString());
                seed.Parameters.AddWithValue("$source_key", sourceKey);
                seed.Parameters.AddWithValue("$hash", new string('a', 64));
                seed.Parameters.AddWithValue("$now", Format(now));
                await seed.ExecuteNonQueryAsync();
            }

            SqliteSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(sourceId, sourceKey, now.AddMinutes(1));
            SourceCopyPurgeService service = new(
                exclusions,
                new SqliteSourceCopyPurgeRepository(database),
                new SourceCopyPurgeRoots(
                    Path.Combine(root, "analysis"),
                    Path.Combine(root, "review"),
                    Path.Combine(root, "detector")),
                new SourceCopyPurgeFileSystem(),
                TimeProvider.System);

            Assert.True(await service.PurgeAsync(sourceId, sourceKey));

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                Assert.Equal(0L, await CountAsync(connection, "assets"));
                Assert.Equal(0L, await CountAsync(connection, "face_occurrences"));
                Assert.Equal(0L, await CountAsync(connection, "person_labels"));
                Assert.Equal(0L, await CountAsync(connection, "review_actions"));
                Assert.Equal(1L, await CountAsync(connection, "people"));
            }

            SourceCopyExclusionState state = Assert.IsType<SourceCopyExclusionState>(
                await exclusions.GetAsync(sourceId, sourceKey));
            Assert.Equal(SourceCopyPurgeStates.Completed, state.PurgeState);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
