using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoCaptureDateRepositoryTests
{
    [Fact]
    public async Task Manual_precision_survives_metadata_refresh_drives_query_range_and_clears_back_to_extracted_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_capture_date_{Guid.NewGuid():N}";
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
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, initialization.Health.SchemaVersion);

            AssetRevisionId revisionId = AssetRevisionId.New();
            await SeedRevisionAsync(testBuilder.ConnectionString, revisionId);

            PostgresPhotoCaptureMetadataRepository extracted = new(database);
            DateTime firstExtracted = new(2024, 8, 9, 14, 30, 0, DateTimeKind.Unspecified);
            await extracted.SavePhotoMetadataAsync(
                revisionId,
                new PhotoCaptureMetadata(takenAtLocal: firstExtracted),
                new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero));

            TimeProvider time = new FixedTimeProvider(
                new DateTimeOffset(2026, 9, 20, 19, 0, 0, TimeSpan.Zero));
            PostgresPhotoCaptureDateRepository dates = new(database, time);

            PhotoCaptureDateState initial = await dates.GetStateAsync(revisionId);
            Assert.Null(initial.ManualDate);
            Assert.Equal(PhotoCaptureDateSources.Extracted, initial.EffectiveSource);
            Assert.Equal(new DateOnly(2024, 8, 9), initial.EffectiveRange!.From);
            Assert.Empty(initial.History);

            PhotoCaptureDateState year = await dates.SetManualAsync(
                revisionId,
                new PhotoCaptureDateValue(1999),
                "test");
            Assert.Equal(PhotoCaptureDateSources.Manual, year.EffectiveSource);
            Assert.Equal(PhotoCaptureDatePrecisions.Year, year.ManualDate!.Precision);
            Assert.Equal(new DateOnly(1999, 1, 1), year.EffectiveRange!.From);
            Assert.Equal(new DateOnly(1999, 12, 31), year.EffectiveRange.To);
            Assert.Single(year.History);

            PostgresSmartCollectionRepository definitions = new(database, TimeProvider.System);
            PostgresSmartCollectionQueryRepository query = new(database, definitions, TimeProvider.System);
            SmartCollectionPhotoPage yearOverlap = await query.QueryAsync(
                new SmartCollectionFilter(
                    taken: new SmartCollectionDateRange(
                        new DateOnly(1999, 6, 15),
                        new DateOnly(1999, 6, 15))));
            SmartCollectionPhoto yearPhoto = Assert.Single(yearOverlap.Items);
            Assert.Equal(PhotoCaptureDateSources.Manual, yearPhoto.CaptureDateSource);
            Assert.Equal(year.ManualDate.InclusiveRange, yearPhoto.EffectiveCaptureDate);

            DateTime refreshedExtracted = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
            await extracted.SavePhotoMetadataAsync(
                revisionId,
                new PhotoCaptureMetadata(takenAtLocal: refreshedExtracted),
                new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero));

            PhotoCaptureDateState afterRefresh = await dates.GetStateAsync(revisionId);
            Assert.Equal(refreshedExtracted, afterRefresh.ExtractedTakenAtLocal);
            Assert.Equal(new PhotoCaptureDateValue(1999), afterRefresh.ManualDate);
            Assert.Equal(new DateOnly(1999, 1, 1), afterRefresh.EffectiveRange!.From);

            PhotoCaptureDateState month = await dates.SetManualAsync(
                revisionId,
                new PhotoCaptureDateValue(2000, 2),
                "test");
            Assert.Equal(new DateOnly(2000, 2, 1), month.EffectiveRange!.From);
            Assert.Equal(new DateOnly(2000, 2, 29), month.EffectiveRange.To);
            Assert.Equal(2, month.History.Count);

            PhotoCaptureDateValue exactValue = new(2001, 3, 4);
            PhotoCaptureDateState exact = await dates.SetManualAsync(revisionId, exactValue, "test");
            Assert.Equal(PhotoCaptureDatePrecisions.Day, exact.ManualDate!.Precision);
            Assert.Equal(new DateOnly(2001, 3, 4), exact.EffectiveRange!.From);
            Assert.Equal(3, exact.History.Count);

            PhotoCaptureDateState duplicate = await dates.SetManualAsync(revisionId, exactValue, "test");
            Assert.Equal(3, duplicate.History.Count);

            SmartCollectionPhotoPage manualMatch = await query.QueryAsync(
                new SmartCollectionFilter(
                    taken: new SmartCollectionDateRange(
                        new DateOnly(2001, 3, 4),
                        new DateOnly(2001, 3, 4))));
            SmartCollectionPhoto manualPhoto = Assert.Single(manualMatch.Items);
            Assert.Equal(revisionId, manualPhoto.RevisionId);
            Assert.Equal(PhotoCaptureDateSources.Manual, manualPhoto.CaptureDateSource);
            Assert.Equal(exactValue.InclusiveRange, manualPhoto.EffectiveCaptureDate);

            SmartCollectionPhotoPage extractedDoesNotMatchWhileManual = await query.QueryAsync(
                new SmartCollectionFilter(
                    taken: new SmartCollectionDateRange(
                        new DateOnly(2025, 1, 2),
                        new DateOnly(2025, 1, 2))));
            Assert.Empty(extractedDoesNotMatchWhileManual.Items);

            PhotoCaptureDateState cleared = await dates.ClearManualAsync(revisionId, "test");
            Assert.Null(cleared.ManualDate);
            Assert.Equal(PhotoCaptureDateSources.Extracted, cleared.EffectiveSource);
            Assert.Equal(new DateOnly(2025, 1, 2), cleared.EffectiveRange!.From);
            Assert.Equal(4, cleared.History.Count);

            PhotoCaptureDateState duplicateClear = await dates.ClearManualAsync(revisionId, "test");
            Assert.Equal(4, duplicateClear.History.Count);

            SmartCollectionPhotoPage extractedMatch = await query.QueryAsync(
                new SmartCollectionFilter(
                    taken: new SmartCollectionDateRange(
                        new DateOnly(2025, 1, 2),
                        new DateOnly(2025, 1, 2))));
            SmartCollectionPhoto extractedPhoto = Assert.Single(extractedMatch.Items);
            Assert.Equal(PhotoCaptureDateSources.Extracted, extractedPhoto.CaptureDateSource);
            Assert.Equal(
                new PhotoCaptureDateRange(new DateOnly(2025, 1, 2), new DateOnly(2025, 1, 2)),
                extractedPhoto.EffectiveCaptureDate);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRevisionAsync(
        string connectionString,
        AssetRevisionId revisionId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', @root_locator, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'capture-date/photo.jpg', @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type)
            VALUES (
                @revision_id,
                @asset_id,
                @content_sha256,
                100,
                @now,
                'image/jpeg');
            """;
        command.Parameters.AddWithValue("source_id", NpgsqlDbType.Uuid, sourceId);
        command.Parameters.AddWithValue("root_locator", NpgsqlDbType.Text, $"capture-date-root-{sourceId:N}");
        command.Parameters.AddWithValue("asset_id", NpgsqlDbType.Uuid, assetId);
        command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        command.Parameters.AddWithValue("content_sha256", NpgsqlDbType.Text, new string('a', 64));
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        """ + identifier.Replace(""", """", StringComparison.Ordinal) + """;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
