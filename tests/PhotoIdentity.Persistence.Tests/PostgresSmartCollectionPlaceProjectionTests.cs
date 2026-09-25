using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSmartCollectionPlaceProjectionTests
{
    [Fact]
    public async Task Smart_collection_photo_uses_current_effective_place_with_manual_precedence_and_clear_semantics()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_smart_place_{Guid.NewGuid():N}";
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

            AssetRevisionId revision = AssetRevisionId.New();
            await SeedRevisionAsync(database, revision);

            PostgresSmartCollectionRepository definitions = new(database, TimeProvider.System);
            PostgresSmartCollectionQueryRepository query = new(database, definitions, TimeProvider.System);
            PostgresPhotoPlaceRepository places = new(database, TimeProvider.System);

            SmartCollectionPhoto initial = Assert.Single((await query.QueryAsync(new SmartCollectionFilter())).Items);
            Assert.Null(initial.EffectivePlace);

            await places.TrySetAsync(
                revision,
                "Sweden/Stockholms län/Stockholm",
                "geonames",
                "test",
                CancellationToken.None);
            SmartCollectionPhoto automatic = Assert.Single((await query.QueryAsync(new SmartCollectionFilter())).Items);
            Assert.Equal("Sweden/Stockholms län/Stockholm", automatic.EffectivePlace);

            await places.SetManualPlaceAsync(
                revision,
                "France/Île-de-France/Paris",
                "test",
                CancellationToken.None);
            SmartCollectionPhoto manual = Assert.Single((await query.QueryAsync(new SmartCollectionFilter())).Items);
            Assert.Equal("France/Île-de-France/Paris", manual.EffectivePlace);

            var blockedAutomatic = await places.TrySetAsync(
                revision,
                "Germany/Berlin",
                "geonames",
                "test",
                CancellationToken.None);
            Assert.True(blockedAutomatic.BlockedByManual);
            SmartCollectionPhoto stillManual = Assert.Single((await query.QueryAsync(new SmartCollectionFilter())).Items);
            Assert.Equal("France/Île-de-France/Paris", stillManual.EffectivePlace);

            await places.ClearManualPlaceAsync(
                revision,
                "test",
                CancellationToken.None);
            SmartCollectionPhoto cleared = Assert.Single((await query.QueryAsync(new SmartCollectionFilter())).Items);
            Assert.Null(cleared.EffectivePlace);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRevisionAsync(
        PostgresCatalogueDatabase database,
        AssetRevisionId revision)
    {
        Guid source = Guid.NewGuid();
        Guid asset = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'test', @root, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset, @source, 'placed-photo.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES (
                @revision, @asset, @hash, 123, @now, 'image/jpeg', 1000, 700);
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"smart-place-{source:N}");
        command.Parameters.AddWithValue("asset", NpgsqlDbType.Uuid, asset);
        command.Parameters.AddWithValue("revision", NpgsqlDbType.Uuid, revision.Value);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Text, new string('3', 64));
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
