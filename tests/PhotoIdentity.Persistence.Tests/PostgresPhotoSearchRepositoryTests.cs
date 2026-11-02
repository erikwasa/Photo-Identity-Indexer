using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoSearchRepositoryTests
{
    private const string ProfileId = "jpeg-1600-q78";
    private const string ModelId = "openai/clip-vit-base-patch32";
    private const string ModelSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PreprocessingVersion = "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1";
    private const string Encoding = PhotoEmbeddingEvidence.Float32L2NormalizedEncoding;

    [Fact]
    public async Task Semantic_evidence_and_latest_displayable_captions_remain_versioned_and_searchable()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_search_{Guid.NewGuid():N}";
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

            AssetRevisionId first = AssetRevisionId.New();
            AssetRevisionId second = AssetRevisionId.New();
            DateTimeOffset now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
            AssetRevisionId videoRevision = await SeedAsync(database, first, second, now);

            PostgresPhotoCaptionRepository captions = new(database);
            await captions.SaveAsync(Caption(
                first,
                "sv",
                "safe-v1",
                "Barn leker i en park.",
                [],
                now));
            await captions.SaveAsync(Caption(
                first,
                "sv",
                "blocked-v2",
                "Barn leker i en park.",
                ["unsafe-claim"],
                now.AddMinutes(1)));
            await captions.SaveAsync(Caption(
                second,
                "sv",
                "safe-v1",
                "En bil står på vägen.",
                [],
                now));

            PostgresPhotoSearchRepository search = new(database);

            IReadOnlyList<AssetRevisionId> candidates = await search.GetEmbeddingCandidatesAsync(
                ProfileId,
                ModelId,
                ModelSha,
                PreprocessingVersion,
                Encoding,
                10);
            Assert.Equal(2, candidates.Count);
            Assert.Contains(first, candidates);
            Assert.Contains(second, candidates);

            PhotoEmbeddingEvidence evidence = new(
                first,
                ModelId,
                ModelSha,
                PreprocessingVersion,
                [1f, 0f]);
            await search.SaveEmbeddingAsync(evidence, 12.5, now);

            IReadOnlyList<PhotoSearchEmbeddingRecord> persisted =
                await search.GetCurrentEmbeddingsAsync(
                    ModelId,
                    ModelSha,
                    PreprocessingVersion,
                    Encoding);
            PhotoSearchEmbeddingRecord stored = Assert.Single(persisted);
            Assert.Equal(first, stored.Evidence.RevisionId);
            Assert.Equal([1f, 0f], stored.Evidence.Values);
            Assert.Equal(12.5, stored.GenerationMilliseconds);

            IReadOnlyList<AssetRevisionId> remaining = await search.GetEmbeddingCandidatesAsync(
                ProfileId,
                ModelId,
                ModelSha,
                PreprocessingVersion,
                Encoding,
                10);
            Assert.Equal([second], remaining);

            Assert.Empty(await search.SearchCaptionsAsync("barn", 10));
            PhotoSearchCaptionHit car = Assert.Single(await search.SearchCaptionsAsync("bil", 10));
            Assert.Equal(second, car.RevisionId);
            Assert.Equal("sv", car.Language);

            Assert.Empty(await search.SearchCaptionsAsync(
                "bil",
                10,
                new HashSet<AssetRevisionId> { first }));
            PhotoSearchCaptionHit scopedCar = Assert.Single(await search.SearchCaptionsAsync(
                "bil",
                10,
                new HashSet<AssetRevisionId> { second }));
            Assert.Equal(second, scopedCar.RevisionId);
            Assert.Empty(await search.SearchCaptionsAsync(
                "bil",
                10,
                new HashSet<AssetRevisionId>()));

            PhotoSearchCatalogueStatistics catalogue = await search.GetCatalogueStatisticsAsync();
            Assert.Equal(2, catalogue.CurrentPhotoCount);
            Assert.Equal(1, catalogue.DisplayableCaptionCount);

            PhotoSearchStorageStatistics statistics = await search.GetStatisticsAsync(
                ModelId,
                ModelSha,
                PreprocessingVersion,
                Encoding);
            Assert.Equal(2, statistics.CurrentPhotoCount);
            Assert.Equal(1, statistics.EmbeddingCount);
            Assert.Equal(1, statistics.DisplayableCaptionCount);
            Assert.Equal(2, statistics.EmbeddingDimensions);
            Assert.Equal(8, statistics.RawEmbeddingBytes);
            Assert.Equal(12.5, statistics.AverageEmbeddingGenerationMilliseconds);

            // WI-0191: coexistence regression. Snapshot real photo search
            // ranks, counts and saved photo collection ordering, publish
            // independent video FrameEvidence, and compare exactly again.
            var photoCollections = new PostgresPhotoListCollectionRepository(
                database, TimeProvider.System);
            PhotoListCollectionDefinition photoCollection =
                await photoCollections.CreateAsync(
                    "Stable photo selection", [second, first]);
            PhotoListCollectionSlideshowSnapshot photoSnapshotBefore =
                Assert.IsType<PhotoListCollectionSlideshowSnapshot>(
                    await photoCollections.CreateSlideshowSnapshotAsync(photoCollection.Id));

            async Task<PhotoSearchRankedHit[]> PhotoResultsAsync()
            {
                IReadOnlyList<PhotoSearchEmbeddingRecord> records =
                    await search.GetCurrentEmbeddingsAsync(
                        ModelId, ModelSha, PreprocessingVersion, Encoding);
                IReadOnlyList<PhotoSearchSemanticHit> semantic = PhotoSearchSemanticRanker.Find(
                    records.ToDictionary(item => item.Evidence.RevisionId,
                        item => item.Evidence),
                    [1f, 0f], 10);
                IReadOnlyList<PhotoSearchCaptionHit> caption =
                    await search.SearchCaptionsAsync("bil", 10);
                return PhotoSearchRanker.Fuse(semantic, caption, "combined", 10).ToArray();
            }

            PhotoSearchRankedHit[] before = await PhotoResultsAsync();
            PhotoSearchCatalogueStatistics photoCountsBefore =
                await search.GetCatalogueStatisticsAsync();
            PhotoSearchStorageStatistics photoStatsBefore =
                await search.GetStatisticsAsync(
                    ModelId, ModelSha, PreprocessingVersion, Encoding);

            VideoFrameSamplingPlan videoPlan = VideoFrameSamplingPlanner.Plan(
                new VideoFrameSamplingRequest(8m, MaximumFrames: 2));
            var videoBudget = new VideoProcessingBudget(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 5000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            var videoCandidate = new VideoProcessingCandidate(
                true, false, true, true, 4000, 16000, 2);
            var jobs = new PostgresVideoProcessingJobRepository(database);
            Guid job = await jobs.EnqueueAsync(
                new VideoDerivativeJobIdentity(
                    videoRevision, VideoDerivativeKind.FrameEvidence,
                    videoPlan.PolicyVersion, "wi0190-sdr-frame-jpeg-v1",
                    new string('c', 64)),
                videoBudget, videoCandidate);
            VideoProcessingJobLease lease = Assert.IsType<VideoProcessingJobLease>(
                await jobs.TryClaimNextAsync(videoBudget));
            var provenance = new VideoFrameEvidenceProvenance(
                videoRevision, videoPlan.PolicyVersion, "wi0190-sdr-frame-jpeg-v1",
                ModelId, ModelSha, PreprocessingVersion)
            {
                CaptionModelId = "synthetic-caption-model",
                CaptionModelSha256 = new string('d', 64),
                CaptionPromptVersion = "synthetic-caption-prompt-v1"
            };
            var videoEvidence = new VideoFrameEvidenceBatch(
                provenance,
                videoPlan.Samples.Select((frame, index) => new VideoFrameEvidenceItem(
                    frame.TimestampMilliseconds, frame.SceneIndex,
                    index == 0 ? "A car parked in the street." : "Children playing near a car.",
                    [0.2f, 0.8f])).ToArray());
            Assert.True(await new PostgresVideoFrameEvidenceRepository(database)
                .TryPublishAsync(lease, videoPlan, videoEvidence));
            Assert.Equal("succeeded", (await jobs.GetAsync(job))?.Status);
            Assert.Equal(videoRevision.Value,
                Assert.Single(await new PostgresVideoSearchRepository(database)
                    .ReadCurrentAsync()).RevisionId);

            PhotoSearchRankedHit[] after = await PhotoResultsAsync();
            Assert.Equal(
                before.Select(item => (item.RevisionId, item.CombinedScore,
                    item.SemanticScore, item.CaptionScore, item.CaptionLanguage,
                    item.Caption)).ToArray(),
                after.Select(item => (item.RevisionId, item.CombinedScore,
                    item.SemanticScore, item.CaptionScore, item.CaptionLanguage,
                    item.Caption)).ToArray());
            Assert.DoesNotContain(after, item => item.RevisionId == videoRevision);
            Assert.Equal(photoCountsBefore, await search.GetCatalogueStatisticsAsync());
            Assert.Equal(photoStatsBefore, await search.GetStatisticsAsync(
                ModelId, ModelSha, PreprocessingVersion, Encoding));
            PhotoListCollectionSlideshowSnapshot photoSnapshotAfter =
                Assert.IsType<PhotoListCollectionSlideshowSnapshot>(
                    await photoCollections.CreateSlideshowSnapshotAsync(photoCollection.Id));
            Assert.Equal(photoSnapshotBefore.RevisionIds, photoSnapshotAfter.RevisionIds);
            await Assert.ThrowsAsync<PhotoListCollectionRevisionUnavailableException>(() =>
                photoCollections.UpdateAsync(
                    photoCollection.Id, photoCollection.Name, [first, videoRevision]));
            Assert.Equal([second, first],
                (await photoCollections.CreateSlideshowSnapshotAsync(photoCollection.Id))!
                    .RevisionIds);

        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static PhotoGeneratedCaption Caption(
        AssetRevisionId revisionId,
        string language,
        string generationVersion,
        string content,
        IReadOnlyList<string> riskFlags,
        DateTimeOffset generatedAtUtc) =>
        new(
            revisionId,
            language,
            generationVersion,
            "caption-model",
            new string('b', 64),
            "prompt-v1",
            "thumbnail",
            1024,
            content,
            riskFlags,
            100,
            generatedAtUtc);

    private static async Task<AssetRevisionId> SeedAsync(
        PostgresCatalogueDatabase database,
        AssetRevisionId first,
        AssetRevisionId second,
        DateTimeOffset now)
    {
        Guid source = Guid.NewGuid();
        Guid firstAsset = Guid.NewGuid();
        Guid secondAsset = Guid.NewGuid();
        Guid videoAsset = Guid.NewGuid();
        Guid videoRevision = Guid.NewGuid();

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'local-folder', @root, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@first_asset, @source, 'a-first.jpg', @now),
                (@second_asset, @source, 'b-second.jpg', @now);
            INSERT INTO assets (id, source_id, source_key, media_kind, created_at_utc)
            VALUES (@video_asset, @source, 'c-video.mov', 'video', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES
                (@first_revision, @first_asset, @first_hash, 123, @now, 'image/jpeg', 1000, 700),
                (@second_revision, @second_asset, @second_hash, 123, @now, 'image/jpeg', 1000, 700);
            INSERT INTO asset_revisions (id, asset_id, content_sha256,
                size_bytes, observed_at_utc, media_type, width, height, media_kind)
            VALUES (@video_revision, @video_asset, repeat('f',64),
                123, @now, 'video/quicktime', 1000, 700, 'video');

            INSERT INTO archive_review_proxy_profiles (
                profile_id,
                protocol_version,
                encoder,
                format,
                jpeg_quality,
                maximum_long_edge,
                resize_policy,
                canonical_definition,
                recorded_at_utc)
            VALUES (
                @profile_id,
                'v1',
                'test',
                'jpeg',
                78,
                1600,
                'fit',
                'semantic search acceptance proxy',
                @now);

            INSERT INTO asset_revision_review_proxies (
                asset_revision_id,
                profile_id,
                encoded_byte_length,
                content_sha256,
                width,
                height,
                generated_at_utc,
                relative_path)
            VALUES
                (@first_revision, @profile_id, 42, @proxy_hash_1, 100, 80, @now, 'review-proxies/first.jpg'),
                (@second_revision, @profile_id, 42, @proxy_hash_2, 100, 80, @now, 'review-proxies/second.jpg'),
                (@video_revision, @profile_id, 42, @proxy_hash_3, 100, 80, @now, 'review-proxies/legacy-video.jpg');

            INSERT INTO video_revision_metadata (
                asset_revision_id, inspection_status, duration_seconds,
                color_transfer, inspected_at_utc, tool_sha256, inspection_profile_id)
            VALUES (@video_revision, 'supported', 8, 'bt709', @now, repeat('c', 64), 'synthetic');

            UPDATE video_processing_control SET enabled=true, paused=false WHERE singleton_id=1;
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"search-{source:N}");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("first_asset", NpgsqlDbType.Uuid, firstAsset);
        command.Parameters.AddWithValue("second_asset", NpgsqlDbType.Uuid, secondAsset);
        command.Parameters.AddWithValue("video_asset", NpgsqlDbType.Uuid, videoAsset);
        command.Parameters.AddWithValue("video_revision", NpgsqlDbType.Uuid, videoRevision);
        command.Parameters.AddWithValue("first_revision", NpgsqlDbType.Uuid, first.Value);
        command.Parameters.AddWithValue("second_revision", NpgsqlDbType.Uuid, second.Value);
        command.Parameters.AddWithValue("first_hash", NpgsqlDbType.Text, new string('1', 64));
        command.Parameters.AddWithValue("second_hash", NpgsqlDbType.Text, new string('2', 64));
        command.Parameters.AddWithValue("profile_id", NpgsqlDbType.Text, ProfileId);
        command.Parameters.AddWithValue("proxy_hash_1", NpgsqlDbType.Text, new string('3', 64));
        command.Parameters.AddWithValue("proxy_hash_2", NpgsqlDbType.Text, new string('4', 64));
        command.Parameters.AddWithValue("proxy_hash_3", NpgsqlDbType.Text, new string('5', 64));
        await command.ExecuteNonQueryAsync();
        return AssetRevisionId.From(videoRevision);
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
