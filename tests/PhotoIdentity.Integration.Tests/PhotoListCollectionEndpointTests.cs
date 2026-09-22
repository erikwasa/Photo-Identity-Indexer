using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoListCollectionEndpointTests
{
    [Fact]
    public async Task Api_covers_lifecycle_membership_and_playback_compatible_snapshot()
    {
        InMemoryRepository repository = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPhotoListCollectionRepository>(repository);

        await using WebApplication app = builder.Build();
        app.MapPhotoListCollectionEndpoints();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        string first = Guid.NewGuid().ToString("D");
        string second = Guid.NewGuid().ToString("D");

        using HttpResponseMessage createdResponse = await client.PostAsJsonAsync(
            "/api/photo-list-collections",
            new PhotoListCollectionRequest("  Family   picks  ", [second, first]));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        PhotoListCollectionResponse created =
            await createdResponse.Content.ReadFromJsonAsync<PhotoListCollectionResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal("Family picks", created.Name);
        Assert.Equal([second, first], created.RevisionIds);

        PhotoListCollectionResponse[] listed =
            await client.GetFromJsonAsync<PhotoListCollectionResponse[]>(
                "/api/photo-list-collections")
            ?? throw new InvalidOperationException();
        Assert.Equal(created.Id, Assert.Single(listed).Id);

        using HttpResponseMessage updatedResponse = await client.PutAsJsonAsync(
            $"/api/photo-list-collections/{created.Id}",
            new PhotoListCollectionRequest("Family picks", [first, second]));
        updatedResponse.EnsureSuccessStatusCode();
        PhotoListCollectionResponse updated =
            await updatedResponse.Content.ReadFromJsonAsync<PhotoListCollectionResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal([first, second], updated.RevisionIds);

        using HttpResponseMessage snapshotResponse = await client.PostAsync(
            $"/api/photo-list-collections/{created.Id}/slideshow-snapshot",
            content: null);
        snapshotResponse.EnsureSuccessStatusCode();
        SmartCollectionSlideshowSnapshotResponse snapshot =
            await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal(created.Id, snapshot.CollectionId);
        Assert.Equal("Family picks", snapshot.CollectionName);
        Assert.Equal([first, second], snapshot.Items.Select(item => item.RevisionId).ToArray());

        using HttpResponseMessage duplicateResponse = await client.PutAsJsonAsync(
            $"/api/photo-list-collections/{created.Id}",
            new PhotoListCollectionRequest("Family picks", [first, first]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        using HttpResponseMessage deleted =
            await client.DeleteAsync($"/api/photo-list-collections/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using HttpResponseMessage missing =
            await client.GetAsync($"/api/photo-list-collections/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private sealed class InMemoryRepository : IPhotoListCollectionRepository
    {
        private readonly Dictionary<PhotoListCollectionId, PhotoListCollectionDefinition> _items = [];
        private readonly DateTimeOffset _now =
            new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

        public Task<PhotoListCollectionDefinition> CreateAsync(
            string name,
            IReadOnlyList<AssetRevisionId> revisionIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PhotoListCollectionName parsedName = PhotoListCollectionName.Parse(name);
            AssetRevisionId[] parsedIds =
                PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
            PhotoListCollectionDefinition definition = new(
                PhotoListCollectionId.New(),
                parsedName.DisplayValue,
                parsedIds,
                _now,
                _now);
            _items.Add(definition.Id, definition);
            return Task.FromResult(definition);
        }

        public Task<IReadOnlyList<PhotoListCollectionDefinition>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<PhotoListCollectionDefinition>>(
                _items.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray());
        }

        public Task<PhotoListCollectionDefinition?> GetAsync(
            PhotoListCollectionId id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items.TryGetValue(id, out PhotoListCollectionDefinition? definition);
            return Task.FromResult(definition);
        }

        public Task<PhotoListCollectionDefinition?> UpdateAsync(
            PhotoListCollectionId id,
            string name,
            IReadOnlyList<AssetRevisionId> revisionIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_items.TryGetValue(id, out PhotoListCollectionDefinition? existing))
            {
                return Task.FromResult<PhotoListCollectionDefinition?>(null);
            }

            PhotoListCollectionName parsedName = PhotoListCollectionName.Parse(name);
            AssetRevisionId[] parsedIds =
                PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
            PhotoListCollectionDefinition updated = existing with
            {
                Name = parsedName.DisplayValue,
                RevisionIds = parsedIds,
                UpdatedAtUtc = _now.AddMinutes(1),
            };
            _items[id] = updated;
            return Task.FromResult<PhotoListCollectionDefinition?>(updated);
        }

        public Task<bool> DeleteAsync(
            PhotoListCollectionId id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_items.Remove(id));
        }

        public Task<PhotoListCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
            PhotoListCollectionId id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_items.TryGetValue(id, out PhotoListCollectionDefinition? definition))
            {
                return Task.FromResult<PhotoListCollectionSlideshowSnapshot?>(null);
            }

            return Task.FromResult<PhotoListCollectionSlideshowSnapshot?>(
                new PhotoListCollectionSlideshowSnapshot(
                    definition.Id,
                    definition.Name,
                    _now.AddMinutes(2),
                    definition.RevisionIds.ToArray()));
        }
    }
}
