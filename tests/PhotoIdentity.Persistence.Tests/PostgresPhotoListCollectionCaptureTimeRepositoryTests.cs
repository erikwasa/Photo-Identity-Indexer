using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoListCollectionCaptureTimeRepositoryTests
{
    [Fact]
    public async Task Repository_resolves_effective_capture_times_in_one_ordered_batch()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_manual_sort_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);

            AssetRevisionId extractedOnly = AssetRevisionId.New();
            AssetRevisionId manualOverride = AssetRevisionId.New();
            AssetRevisionId undated = AssetRevisionId.New();
            await SeedAsync(database, extractedOnly, manualOverride, undated);

            PostgresPhotoListCollectionCaptureTimeRepository repository = new(database);
            IReadOnlyList<PhotoListCollectionCaptureTime> values =
                await repository.GetCaptureTimesAsync([undated, extractedOnly, manualOverride]);

            Assert.Equal([undated, extractedOnly, manualOverride], values.Select(item => item.RevisionId).ToArray());
            Assert.Null(values[0].EffectiveTakenAtLocal);
            Assert.Equal(
                new DateTime(2020, 6, 15, 14, 30, 0, DateTimeKind.Unspecified),
                values[1].EffectiveTakenAtLocal);
            Assert.Equal(
                new DateTime(2018, 5, 1, 0, 0, 0, DateTimeKind.Unspecified),
                values[2].EffectiveTakenAtLocal);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAsync(
        PostgresCatalogueDatabase database,
        AssetRevisionId extractedOnly,
        AssetRevisionId manualOverride,
        AssetRevisionId undated)
    {
        Guid source = Guid.NewGuid();
        Guid firstAsset = Guid.NewGuid();
        Guid secondAsset = Guid.NewGuid();
        Guid thirdAsset = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'local-folder', @root, @now);

            INSERT INTO assets (
                id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES
                (@asset1, @source, 'one.jpg', @now, @now),
                (@asset2, @source, 'two.jpg', @now, @now),
                (@asset3, @source, 'three.jpg', @now, @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES
                (@revision1, @asset1, repeat('1', 64), 100, @now, 'image/jpeg', 100, 100),
                (@revision2, @asset2, repeat('2', 64), 100, @now, 'image/jpeg', 100, 100),
                (@revision3, @asset3, repeat('3', 64), 100, @now, 'image/jpeg', 100, 100);

            INSERT INTO photo_capture_metadata (
                asset_revision_id,
                taken_at_local,
                utc_offset_minutes,
                extracted_at_utc)
            VALUES
                (@revision1, @extracted1, NULL, @now),
                (@revision2, @extracted2, NULL, @now);

            INSERT INTO photo_capture_date_actions (
                asset_revision_id,
                action_kind,
                precision,
                capture_year,
                capture_month,
                capture_day,
                actor,
                created_at_utc)
            VALUES (
                @revision2,
                'set',
                'month',
                2018,
                5,
                NULL,
                'test',
                @now);
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"manual-sort-{source:N}");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("asset1", NpgsqlDbType.Uuid, firstAsset);
        command.Parameters.AddWithValue("asset2", NpgsqlDbType.Uuid, secondAsset);
        command.Parameters.AddWithValue("asset3", NpgsqlDbType.Uuid, thirdAsset);
        command.Parameters.AddWithValue("revision1", NpgsqlDbType.Uuid, extractedOnly.Value);
        command.Parameters.AddWithValue("revision2", NpgsqlDbType.Uuid, manualOverride.Value);
        command.Parameters.AddWithValue("revision3", NpgsqlDbType.Uuid, undated.Value);
        command.Parameters.AddWithValue(
            "extracted1",
            NpgsqlDbType.Timestamp,
            new DateTime(2020, 6, 15, 14, 30, 0, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue(
            "extracted2",
            NpgsqlDbType.Timestamp,
            new DateTime(2024, 2, 1, 8, 0, 0, DateTimeKind.Unspecified));
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
