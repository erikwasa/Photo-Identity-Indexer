using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CreativeCollectionMaterializationQueryTests
{
    [Fact]
    public async Task Preview_uses_snapshot_membership_and_one_shot_catalogue_query()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SmartCollectionId collectionId = SmartCollectionId.New();
            DateTimeOffset now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
            AssetRevisionId anchor = AssetRevisionId.New();
            AssetRevisionId context = AssetRevisionId.New();
            AssetRevisionId unrelated = AssetRevisionId.New();
            SmartCollectionDefinition definition = new(
                collectionId,
                "Query-path regression",
                new SmartCollectionFilter(),
                now,
                now);
            FakeDefinitions definitions = new(definition);
            RecordingQueryRepository query = new(
                collectionId,
                anchor,
                [
                    Photo(anchor, new DateTime(2026, 9, 17, 10, 0, 0), now),
                    Photo(context, new DateTime(2026, 9, 17, 10, 10, 0), now),
                    Photo(unrelated, new DateTime(2026, 9, 17, 12, 0, 0), now),
                ]);

            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISmartCollectionRepository>();
                    services.RemoveAll<ISmartCollectionQueryRepository>();
                    services.AddSingleton<ISmartCollectionRepository>(definitions);
                    services.AddSingleton<ISmartCollectionQueryRepository>(query);
                }));
            using HttpClient client = factory.CreateClient();

            CreativeCollectionPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{collectionId}/creative-preview?targetCount=1&momentGapMinutes=30")
                ?? throw new InvalidOperationException();

            Assert.Equal(0, query.PagedQueryCalls);
            Assert.Equal(1, query.QueryAllCalls);
            Assert.Equal(1, query.SnapshotCalls);
            Assert.Equal(1, preview.DirectAnchorCount);
            Assert.Equal(1, preview.AddedContextCount);
            Assert.Contains(preview.Candidates, candidate => candidate.RevisionId == anchor.ToString());
            Assert.Contains(preview.Candidates, candidate => candidate.RevisionId == context.ToString());
            Assert.DoesNotContain(preview.Candidates, candidate => candidate.RevisionId == unrelated.ToString());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SmartCollectionPhoto Photo(
        AssetRevisionId revisionId,
        DateTime takenAtLocal,
        DateTimeOffset observedAtUtc) => new(
        revisionId,
        AssetId.New(),
        observedAtUtc,
        "image/jpeg",
        1200,
        800,
        takenAtLocal,
        null,
        null,
        []);

    private sealed class FakeDefinitions(SmartCollectionDefinition definition) : ISmartCollectionRepository
    {
        public Task<SmartCollectionDefinition?> GetAsync(
            SmartCollectionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SmartCollectionDefinition?>(id == definition.Id ? definition : null);

        public Task<SmartCollectionDefinition> CreateAsync(
            string name,
            SmartCollectionFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SmartCollectionDefinition>> ListAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SmartCollectionDefinition?> UpdateAsync(
            SmartCollectionId id,
            string name,
            SmartCollectionFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            SmartCollectionId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingQueryRepository(
        SmartCollectionId collectionId,
        AssetRevisionId anchorRevisionId,
        IReadOnlyList<SmartCollectionPhoto> catalogue) : ISmartCollectionQueryRepository
    {
        public int PagedQueryCalls { get; private set; }
        public int QueryAllCalls { get; private set; }
        public int SnapshotCalls { get; private set; }

        public Task<SmartCollectionPhotoPage> QueryAsync(
            SmartCollectionFilter filter,
            int offset = 0,
            int limit = 40,
            CancellationToken cancellationToken = default)
        {
            PagedQueryCalls++;
            throw new InvalidOperationException("Creative materialization must not use paged Smart Collection queries.");
        }

        public Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(
            SmartCollectionFilter filter,
            CancellationToken cancellationToken = default)
        {
            QueryAllCalls++;
            return Task.FromResult(catalogue);
        }

        public Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
            SmartCollectionId requestedCollectionId,
            CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            SmartCollectionSlideshowSnapshot? snapshot = requestedCollectionId == collectionId
                ? new SmartCollectionSlideshowSnapshot(
                    collectionId,
                    "Query-path regression",
                    new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero),
                    [anchorRevisionId])
                : null;
            return Task.FromResult(snapshot);
        }
    }
}
