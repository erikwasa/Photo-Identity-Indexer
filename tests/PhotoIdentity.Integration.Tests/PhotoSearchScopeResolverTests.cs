using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoSearchScopeResolverTests
{
    [Fact]
    public async Task Resolving_saved_collection_materializes_one_server_side_revision_set()
    {
        SmartCollectionId collectionId = SmartCollectionId.From(
            Guid.Parse("00000000-0000-0000-0000-000000000186"));
        SmartCollectionFilter filter = new();
        SmartCollectionDefinition definition = new(
            collectionId,
            "Fideli 2019-2024",
            filter,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        AssetRevisionId first = AssetRevisionId.From(
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        AssetRevisionId second = AssetRevisionId.From(
            Guid.Parse("00000000-0000-0000-0000-000000000002"));
        FakeDefinitions definitions = new(definition);
        FakeQuery query = new(
        [
            Photo(first),
            Photo(second),
            Photo(second),
        ]);
        PhotoSearchScopeResolver resolver = new(definitions, query);

        PhotoSearchScope? scope = await resolver.ResolveAsync(collectionId.ToString());

        Assert.NotNull(scope);
        Assert.Equal(collectionId, scope.CollectionId);
        Assert.Equal("Fideli 2019-2024", scope.CollectionName);
        Assert.Equal(2, scope.EligiblePhotoCount);
        Assert.Contains(first, scope.RevisionIds);
        Assert.Contains(second, scope.RevisionIds);
        Assert.Same(filter, query.LastFilter);
        Assert.Equal(1, query.QueryCount);
    }

    [Fact]
    public async Task Empty_saved_collection_is_a_valid_empty_scope()
    {
        SmartCollectionId collectionId = SmartCollectionId.From(
            Guid.Parse("00000000-0000-0000-0000-000000000187"));
        SmartCollectionDefinition definition = new(
            collectionId,
            "Empty",
            new SmartCollectionFilter(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        PhotoSearchScopeResolver resolver = new(
            new FakeDefinitions(definition),
            new FakeQuery([]));

        PhotoSearchScope? scope = await resolver.ResolveAsync(collectionId.ToString());

        Assert.NotNull(scope);
        Assert.Equal(0, scope.EligiblePhotoCount);
        Assert.Empty(scope.RevisionIds);
    }

    [Fact]
    public async Task Missing_saved_collection_is_not_treated_as_unscoped_search()
    {
        SmartCollectionId collectionId = SmartCollectionId.From(
            Guid.Parse("00000000-0000-0000-0000-000000000188"));
        PhotoSearchScopeResolver resolver = new(
            new FakeDefinitions(null),
            new FakeQuery([]));

        PhotoSearchScopeNotFoundException exception =
            await Assert.ThrowsAsync<PhotoSearchScopeNotFoundException>(
                () => resolver.ResolveAsync(collectionId.ToString()));

        Assert.Equal(collectionId, exception.CollectionId);
    }

    [Fact]
    public async Task Blank_scope_means_full_library_without_querying_smart_collections()
    {
        FakeDefinitions definitions = new(null);
        FakeQuery query = new([]);
        PhotoSearchScopeResolver resolver = new(definitions, query);

        PhotoSearchScope? scope = await resolver.ResolveAsync(null);

        Assert.Null(scope);
        Assert.Equal(0, definitions.GetCount);
        Assert.Equal(0, query.QueryCount);
    }

    private static SmartCollectionPhoto Photo(AssetRevisionId revisionId) =>
        new(
            revisionId,
            AssetId.New(),
            DateTimeOffset.UnixEpoch,
            "image/jpeg",
            100,
            100,
            null,
            null,
            null);

    private sealed class FakeDefinitions(SmartCollectionDefinition? definition)
        : ISmartCollectionRepository
    {
        public int GetCount { get; private set; }

        public Task<SmartCollectionDefinition?> GetAsync(
            SmartCollectionId id,
            CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(
                definition is not null && definition.Id == id ? definition : null);
        }

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

    private sealed class FakeQuery(IReadOnlyList<SmartCollectionPhoto> photos)
        : ISmartCollectionQueryRepository
    {
        public int QueryCount { get; private set; }

        public SmartCollectionFilter? LastFilter { get; private set; }

        public Task<SmartCollectionPhotoPage> QueryAsync(
            SmartCollectionFilter filter,
            int offset = 0,
            int limit = 40,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            LastFilter = filter;
            SmartCollectionPhoto[] page = photos
                .Skip(offset)
                .Take(limit)
                .ToArray();
            return Task.FromResult(new SmartCollectionPhotoPage(
                page,
                offset,
                limit,
                photos.Count,
                filter));
        }

        public Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
            SmartCollectionId collectionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
