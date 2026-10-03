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

public sealed class SmartCollectionOrientationEndpointTests
{
    [Fact]
    public async Task Query_endpoint_carries_orientation_into_filter_and_response()
    {
        RecordingQueryRepository query = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISmartCollectionQueryRepository>(query);

        await using WebApplication app = builder.Build();
        app.MapSmartCollectionEndpoints();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/smart-collections/query",
            new SmartCollectionQueryRequest(Orientation: "portrait"));
        response.EnsureSuccessStatusCode();

        SmartCollectionPageResponse payload =
            await response.Content.ReadFromJsonAsync<SmartCollectionPageResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal(SmartCollectionOrientations.Portrait, query.LastFilter?.Orientation);
        Assert.Equal(SmartCollectionOrientations.Portrait, payload.Filter.Orientation);

        using HttpResponseMessage invalid = await client.PostAsJsonAsync(
            "/api/smart-collections/query",
            new SmartCollectionQueryRequest(Orientation: "square"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Definition_update_preserves_existing_orientation_when_older_client_omits_field()
    {
        InMemoryDefinitionRepository definitions = new(
            new SmartCollectionFilter(orientation: SmartCollectionOrientations.Landscape));
        WebApplicationBuilder builder = WebApplication.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISmartCollectionRepository>(definitions);

        await using WebApplication app = builder.Build();
        app.MapSmartCollectionEndpoints();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/smart-collections/{definitions.Definition.Id}",
            new SmartCollectionDefinitionRequest("Existing landscape"));
        response.EnsureSuccessStatusCode();

        SmartCollectionDefinitionResponse payload =
            await response.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
            ?? throw new InvalidOperationException();
        Assert.Equal(SmartCollectionOrientations.Landscape, definitions.Definition.Filter.Orientation);
        Assert.Equal(SmartCollectionOrientations.Landscape, payload.Filter.Orientation);
    }

    private sealed class RecordingQueryRepository : ISmartCollectionQueryRepository
    {
        public SmartCollectionFilter? LastFilter { get; private set; }

        public Task<SmartCollectionPhotoPage> QueryAsync(
            SmartCollectionFilter filter,
            int offset = 0,
            int limit = 40,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastFilter = filter;
            return Task.FromResult(new SmartCollectionPhotoPage([], offset, limit, 0, filter));
        }

        public Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(
            SmartCollectionFilter filter,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<SmartCollectionPhoto>>([]);
        }

        public Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
            SmartCollectionId collectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SmartCollectionSlideshowSnapshot?>(null);
    }

    private sealed class InMemoryDefinitionRepository : ISmartCollectionRepository
    {
        private readonly DateTimeOffset _now =
            new(2026, 10, 3, 1, 30, 0, TimeSpan.Zero);

        public InMemoryDefinitionRepository(SmartCollectionFilter filter)
        {
            Definition = new SmartCollectionDefinition(
                SmartCollectionId.New(),
                "Existing landscape",
                filter,
                _now,
                _now);
        }

        public SmartCollectionDefinition Definition { get; private set; }

        public Task<SmartCollectionDefinition> CreateAsync(
            string name,
            SmartCollectionFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SmartCollectionDefinition>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SmartCollectionDefinition>>([Definition]);

        public Task<SmartCollectionDefinition?> GetAsync(
            SmartCollectionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SmartCollectionDefinition?>(id == Definition.Id ? Definition : null);

        public Task<SmartCollectionDefinition?> UpdateAsync(
            SmartCollectionId id,
            string name,
            SmartCollectionFilter filter,
            CancellationToken cancellationToken = default)
        {
            if (id != Definition.Id)
            {
                return Task.FromResult<SmartCollectionDefinition?>(null);
            }

            Definition = Definition with
            {
                Name = SmartCollectionName.Parse(name).DisplayValue,
                Filter = filter,
                UpdatedAtUtc = _now.AddMinutes(1),
            };
            return Task.FromResult<SmartCollectionDefinition?>(Definition);
        }

        public Task<bool> DeleteAsync(
            SmartCollectionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
