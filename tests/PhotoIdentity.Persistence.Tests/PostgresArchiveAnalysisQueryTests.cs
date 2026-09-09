using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveAnalysisQueryTests
{
    [Fact]
    public async Task Queries_preserve_availability_verification_and_current_revision_scope_when_live_postgres_is_configured()
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString)) return;
        string name = $"photoidentity_analysis_query_{Guid.NewGuid():N}";
        await using NpgsqlConnection admin = new(new NpgsqlConnectionStringBuilder(adminString) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = new($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            await using PostgresCatalogueDatabase database = new(new NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false }.ConnectionString);
            await database.InitializeAsync();
            await database.InitializeAsync();
            SourceId source = SourceId.New();
            Guid asset = Guid.NewGuid();
            AssetRevisionId revision = AssetRevisionId.New();
            AssetRevisionId emptyRevision = AssetRevisionId.New();
            FaceOccurrenceId first = FaceOccurrenceId.New();
            FaceOccurrenceId second = FaceOccurrenceId.New();
            DateTimeOffset now = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            await using NpgsqlConnection connection = await database.OpenConnectionAsync();
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc) VALUES (@source, 'test', 'synthetic', @now);
                    INSERT INTO assets(id, source_id, source_key, created_at_utc) VALUES (@asset, @source, 'photo.jpg', @now);
                    INSERT INTO asset_revisions(id, asset_id, content_sha256, size_bytes, observed_at_utc, width, height)
                    VALUES (@revision, @asset, @hash, 123, @now, 1000, 500),
                           (@empty, @asset, @otherhash, 123, @now - interval '1 minute', 1000, 500);
                    INSERT INTO face_occurrences(id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@first, @revision, 0, @now), (@second, @revision, 1, @now);
                    INSERT INTO face_observations(face_occurrence_id, detector_model_id, detector_model_hash, confidence, bounding_box_json, landmarks_json, observed_at_utc)
                    VALUES (@first, 'test', @hash, 0.9, '[100,100,200,100]', '{}', @now),
                           (@second, 'test', @hash, 0.8, '{"x":0.2,"y":0.1,"width":0.2,"height":0.2}', '{}', @now);
                    """;
                seed.Parameters.AddWithValue("source", Guid.Parse(source.ToString()));
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", Guid.Parse(revision.ToString()));
                seed.Parameters.AddWithValue("empty", Guid.Parse(emptyRevision.ToString()));
                seed.Parameters.AddWithValue("first", Guid.Parse(first.ToString()));
                seed.Parameters.AddWithValue("second", Guid.Parse(second.ToString()));
                seed.Parameters.AddWithValue("hash", new string('a', 64));
                seed.Parameters.AddWithValue("otherhash", new string('b', 64));
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }
            ProcessingRunId run = ProcessingRunId.New();
            await using (NpgsqlCommand seedRun = connection.CreateCommand())
            {
                seedRun.CommandText = "INSERT INTO processing_runs(id, status, configuration_json, started_at_utc) VALUES (@id, 'running', '{}', @now);";
                seedRun.Parameters.AddWithValue("id", Guid.Parse(run.ToString()));
                seedRun.Parameters.AddWithValue("now", now);
                await seedRun.ExecuteNonQueryAsync();
            }
            IArchiveAnalysisStateRepository repository = new PostgresArchiveAnalysisStateRepository(database);
            AnalysisProfileDefinition profile = new(new Sha256Digest(new string('c', 64)), new ModelId("detector"),
                new Sha256Digest(new string('d', 64)), new ModelId("embedder"), new Sha256Digest(new string('e', 64)), new AlignmentProtocolId("alignment"));
            await repository.RegisterRunAsync(run, profile, now);
            Sha256Digest hash = profile.ComputeHash();
            Assert.Equal(revision, Assert.Single(await repository.GetPendingCurrentRevisionIdsAsync(source, hash)));
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(SourceId.New(), hash));
            Assert.Equal(0, await repository.CountCompletedCurrentRevisionsAsync(source, hash));
            PostgresArchiveAvailabilityRepository availability = new(database);
            await availability.RecordAsync(AssetId.From(asset), AssetAvailability.OnlineOnly, now);
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(source, hash));
            Assert.Single(await repository.GetPendingCurrentRevisionIdsAsync(source, hash, includeHydratable: true));
            await availability.RecordAsync(AssetId.From(asset), AssetAvailability.Downloading, now.AddSeconds(1));
            Assert.Single(await repository.GetPendingCurrentRevisionIdsAsync(source, hash, includeHydratable: true));
            await availability.RecordAsync(AssetId.From(asset), AssetAvailability.Unavailable, now.AddSeconds(2));
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(source, hash, includeHydratable: true));
            await availability.RecordAsync(AssetId.From(asset), AssetAvailability.Local, now.AddSeconds(3));
            await using (NpgsqlCommand observation = connection.CreateCommand())
            {
                observation.CommandText = """
                    INSERT INTO archive_source_observations(asset_id, observed_size_bytes, observed_last_write_utc, observed_media_type, observed_at_utc, verification_state)
                    VALUES (@asset, 123, @now, 'image/jpeg', @now, 'needs-source-verification');
                    """;
                observation.Parameters.AddWithValue("asset", asset);
                observation.Parameters.AddWithValue("now", now);
                await observation.ExecuteNonQueryAsync();
            }
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(source, hash, includeHydratable: true));
            IArchiveHydrationRepository hydration = new PostgresArchiveHydrationRepository(database);
            IArchiveSourceHydrationRepository sourceHydration = new PostgresArchiveSourceHydrationRepository(database);
            await hydration.ClaimAsync(revision, now);
            IArchiveSourceVerificationStateRepository verification = new PostgresArchiveSourceVerificationStateRepository(database);
            await verification.MarkNeedsVerificationAsync(AssetId.From(asset), now.AddSeconds(4));
            await verification.MarkNeedsVerificationAsync(AssetId.From(asset), now.AddSeconds(5));
            Assert.Empty(await hydration.GetActiveLeasesAsync());
            Assert.Equal(AssetId.From(asset), Assert.Single(await sourceHydration.GetActiveLeasesAsync()).AssetId);
            Assert.False((await hydration.GetAsync(revision))!.IsActive);
            Assert.True((await sourceHydration.GetAsync(AssetId.From(asset)))!.IsActive);
            await Assert.ThrowsAsync<InvalidOperationException>(() => verification.MarkNeedsVerificationAsync(AssetId.New(), now));
            await using (NpgsqlCommand verified = connection.CreateCommand())
            {
                verified.CommandText = "UPDATE archive_source_observations SET verification_state = 'verified' WHERE asset_id = @id;";
                verified.Parameters.AddWithValue("id", asset);
                await verified.ExecuteNonQueryAsync();
            }
            Assert.Single(await repository.GetPendingCurrentRevisionIdsAsync(source, hash));
            await repository.RecordCompletionAsync(run, revision, hash, now);
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(source, hash));
            Assert.Equal(1, await repository.CountCompletedCurrentRevisionsAsync(source, hash));
            await using (NpgsqlCommand newer = connection.CreateCommand())
            {
                newer.CommandText = "UPDATE asset_revisions SET observed_at_utc = @now WHERE id = @id;";
                newer.Parameters.AddWithValue("now", now.AddMinutes(1));
                newer.Parameters.AddWithValue("id", Guid.Parse(emptyRevision.ToString()));
                await newer.ExecuteNonQueryAsync();
            }
            Assert.Equal(emptyRevision, Assert.Single(await repository.GetPendingCurrentRevisionIdsAsync(source, hash)));
            Assert.Equal(0, await repository.CountCompletedCurrentRevisionsAsync(source, hash));
            await using (NpgsqlCommand deleted = connection.CreateCommand())
            {
                deleted.CommandText = "UPDATE assets SET deleted_at_utc = @now WHERE id = @id;";
                deleted.Parameters.AddWithValue("now", now.AddMinutes(2));
                deleted.Parameters.AddWithValue("id", asset);
                await deleted.ExecuteNonQueryAsync();
            }
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(source, hash));
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetPendingCurrentRevisionIdsAsync(source, hash, cancelled.Token));
        }
        finally
        {
            await using NpgsqlCommand drop = new($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
