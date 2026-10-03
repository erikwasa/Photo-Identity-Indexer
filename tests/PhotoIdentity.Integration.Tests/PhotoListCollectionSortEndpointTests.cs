using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoListCollectionSortEndpointTests
{
    [Fact]
    public async Task Sort_endpoint_batches_capture_times_persists_order_and_does_not_mutate_existing_snapshot()
    {
        InMemoryCollectionRepository collections = new();
        InMemoryCaptureTimeRepository captureTimes = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPhotoListCollectionRepository>(collections);
        builder.Services.AddSingleton<IPhotoListCollectionCaptureTimeRepository>(captureTimes);
        builder.Services.AddSingleton<ISourceCopyExclusionRepository>(new AllowAllExclusionRepository());

        await using WebApplication app = builder.Build();
        app.MapPhotoListCollectionEndpoints();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        AssetRevisionId undatedFirst = AssetRevisionId.New();
        AssetRevisionId tiedFirst = AssetRevisionId.New();
        AssetRevisionId newest = AssetRevisionId.New();
        AssetRevisionId tiedSecond = AssetRevisionId.New();
        AssetRevisionId oldest = AssetRevisionId.New();
        AssetRevisionId undatedSecond = AssetRevisionId.New();
        AssetRevisionId[] initial =
            [undatedFirst, tiedFirst, newest, tiedSecond, oldest, undatedSecond];

        captureTimes.Set(tiedFirst, new DateTime(2020, 5, 1, 12, 0, 0));
        captureTimes.Set(newest, new DateTime(2024, 8, 4, 9, 30, 0));
        captureTimes.Set(tiedSecond, new DateTime(2020, 5, 1, 12, 0, 0));
        captureTimes.Set(oldest, new DateTime(2016, 1, 2, 8, 0, 0));

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/photo-list-collections",
            new PhotoListCollectionRequest(
                "Chronology",
                initial.Select(id => id.ToString()).ToArray()));
        createResponse.EnsureSuccessStatusCode();
        PhotoListCollectionResponse created =
            await createResponse.Content.ReadFromJsonAsync<PhotoListCollectionResponse>()
            ?? throw new InvalidOperationException();
        PhotoListCollectionId collectionId = PhotoListCollectionId.From(Guid.Parse(created.Id));

        PhotoListCollectionSlideshowSnapshot beforeSort =
            await collections.CreateSlideshowSnapshotAsync(collectionId)
            ?? throw new InvalidOperationException();

        using HttpResponseMessage oldestResponse = await client.PostAsJsonAsync(
            $"/api/photo-list-collections/{created.Id}/sort",
            new PhotoListCollectionSortRequest("oldest-first"));
        oldestResponse.EnsureSuccessStatusCode();
        PhotoListCollectionResponse oldestFirst =
            await oldestResponse.Content.ReadFromJsonAsync<PhotoListCollectionResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal(
            [oldest, tiedFirst, tiedSecond, newest, undatedFirst, undatedSecond],
            oldestFirst.RevisionIds.Select(ParseRevisionId).ToArray());
        Assert.Equal(1, captureTimes.CallCount);
        Assert.Equal(initial, captureTimes.LastRequested);
        Assert.Equal(initial, beforeSort.RevisionIds);

        using HttpResponseMessage newestResponse = await client.PostAsJsonAsync(
            $"/api/photo-list-collections/{created.Id}/sort",
            new PhotoListCollectionSortRequest("newest-first"));
        newestResponse.EnsureSuccessStatusCode();
        PhotoListCollectionResponse newestFirst =
            await newestResponse.Content.ReadFromJsonAsync<PhotoListCollectionResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal(
            [newest, tiedFirst, tiedSecond, oldest, undatedFirst, undatedSecond],
            newestFirst.RevisionIds.Select(ParseRevisionId).ToArray());
        Assert.Equal(2, captureTimes.CallCount);

        PhotoListCollectionSlideshowSnapshot afterSort =
            await collections.CreateSlideshowSnapshotAsync(collectionId)
            ?? throw new InvalidOperationException();
        Assert.Equal(
            [newest, tiedFirst, tiedSecond, oldest, undatedFirst, undatedSecond],
            afterSort.RevisionIds);

        using HttpResponseMessage invalid = await client.PostAsJsonAsync(
            $"/api/photo-list-collections/{created.Id}/sort",
            new PhotoListCollectionSortRequest("random"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static AssetRevisionId ParseRevisionId(string value) =>
        AssetRevisionId.From(Guid.Parse(value));

    private sealed class InMemoryCaptureTimeRepository : IPhotoListCollectionCaptureTimeRepository
    {
        private readonly Dictionary<AssetRevisionId, DateTime?> _values = [];

        public int CallCount { get; private set; }
        public AssetRevisionId[] LastRequested { get; private set; } = [];

        public void Set(AssetRevisionId revisionId, DateTime? value) =>
            _values[revisionId] = value;

        public Task<IReadOnlyList<PhotoListCollectionCaptureTime>> GetCaptureTimesAsync(
            IReadOnlyList<AssetRevisionId> revisionIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequested = revisionIds.ToArray();
            return Task.FromResult<IReadOnlyList<PhotoListCollectionCaptureTime>>(
                revisionIds
                    .Select(id => new PhotoListCollectionCaptureTime(
                        id,
                        _values.GetValueOrDefault(id)))
                    .ToArray());
        }
    }

    private sealed class InMemoryCollectionRepository : IPhotoListCollectionRepository
    {
        private readonly Dictionary<PhotoListCollectionId, PhotoListCollectionDefinition> _items = [];
        private readonly DateTimeOffset _now =
            new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        public Task<PhotoListCollectionDefinition> CreateAsync(
            string name,
            IReadOnlyList<AssetRevisionId> revisionIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PhotoListCollectionDefinition definition = new(
                PhotoListCollectionId.New(),
                PhotoListCollectionName.Parse(name).DisplayValue,
                PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds),
                _now,
                _now);
            _items.Add(definition.Id, definition);
            return Task.FromResult(definition);
        }

        public Task<IReadOnlyList<PhotoListCollectionDefinition>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhotoListCollectionDefinition>>(_items.Values.ToArray());

        public Task<PhotoListCollectionDefinition?> GetAsync(
            PhotoListCollectionId id,
            CancellationToken cancellationToken = default)
        {
            _items.TryGetValue(id, out PhotoListCollectionDefinition? definition);
            return Task.FromResult(definition);
        }

        public Task<PhotoListCollectionDefinition?> UpdateAsync(
            PhotoListCollectionId id,
            string name,
            IReadOnlyList<AssetRevisionId> revisionIds,
            CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(id, out PhotoListCollectionDefinition? existing))
            {
                return Task.FromResult<PhotoListCollectionDefinition?>(null);
            }

            PhotoListCollectionDefinition updated = existing with
            {
                Name = PhotoListCollectionName.Parse(name).DisplayValue,
                RevisionIds = PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds),
                UpdatedAtUtc = existing.UpdatedAtUtc.AddSeconds(1),
            };
            _items[id] = updated;
            return Task.FromResult<PhotoListCollectionDefinition?>(updated);
        }

        public Task<bool> DeleteAsync(
            PhotoListCollectionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.Remove(id));

        public Task<PhotoListCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
            PhotoListCollectionId id,
            CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(id, out PhotoListCollectionDefinition? definition))
            {
                return Task.FromResult<PhotoListCollectionSlideshowSnapshot?>(null);
            }

            return Task.FromResult<PhotoListCollectionSlideshowSnapshot?>(
                new PhotoListCollectionSlideshowSnapshot(
                    definition.Id,
                    definition.Name,
                    _now,
                    definition.RevisionIds.ToArray()));
        }
    }

    private sealed class AllowAllExclusionRepository : ISourceCopyExclusionRepository
    {
        public Task<SourceCopyExclusionState?> GetAsync(
            SourceId sourceId,
            string sourceKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SourceCopyExclusionState?>(null);

        public Task<IReadOnlyList<SourceCopyExclusionState>> ListAsync(
            SourceId? sourceId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceCopyExclusionState>>([]);

        public Task<SourceCopyExclusionState> ExcludeAsync(
            SourceId sourceId,
            string sourceKey,
            DateTimeOffset excludedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RestoreAsync(
            SourceId sourceId,
            string sourceKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RecordObservedIfExcludedAsync(
            SourceId sourceId,
            string sourceKey,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task SetPurgeStateAsync(
            SourceId sourceId,
            string sourceKey,
            string purgeState,
            string? errorCode,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> IsAssetExcludedAsync(
            AssetId assetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsRevisionExcludedAsync(
            AssetRevisionId revisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsFaceOccurrenceExcludedAsync(
            FaceOccurrenceId faceOccurrenceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
