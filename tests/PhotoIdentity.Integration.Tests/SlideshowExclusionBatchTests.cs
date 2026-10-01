using System.Reflection;
using Microsoft.AspNetCore.Http;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowExclusionBatchTests
{
    [Fact]
    public async Task Smart_catalogue_and_snapshot_filter_in_batches_and_preserve_order()
    {
        AssetRevisionId[] ids = Enumerable.Range(0, 1000).Select(_ => AssetRevisionId.New()).ToArray();
        var exclusions = DispatchProxy.Create<ISourceCopyExclusionRepository, BatchExclusions>();
        BatchExclusions recording = (BatchExclusions)exclusions;
        recording.Excluded.Add(ids[500]);
        ExclusionAwareSmartCollectionQueryRepository query = new(new SmartQuery(ids), exclusions);
        using CancellationTokenSource cancellation = new();

        var all = await query.QueryAllAsync(new SmartCollectionFilter(), cancellation.Token);
        Assert.Equal(ids.Where(id => id != ids[500]), all.Select(photo => photo.RevisionId));
        Assert.Single(recording.Batches);
        Assert.Equal(1000, recording.Batches[0].Length);
        Assert.Equal(cancellation.Token, recording.LastToken);

        var snapshot = await query.CreateSlideshowSnapshotAsync(SmartCollectionId.New(), cancellation.Token);
        Assert.Equal(all.Select(photo => photo.RevisionId), snapshot!.RevisionIds);
        Assert.Equal(2, recording.Batches.Count);

        var page = await query.QueryAsync(new SmartCollectionFilter(), 0, 1000, cancellation.Token);
        Assert.Equal(1000, page.Total);
        Assert.Equal(snapshot.RevisionIds, page.Items.Select(photo => photo.RevisionId));
        Assert.Equal(3, recording.Batches.Count);
    }

    [Fact]
    public async Task Manual_listing_batches_across_collections_and_snapshot_preserves_privacy()
    {
        AssetRevisionId[] ids = Enumerable.Range(0, 100).Select(_ => AssetRevisionId.New()).ToArray();
        var exclusions = DispatchProxy.Create<ISourceCopyExclusionRepository, BatchExclusions>();
        BatchExclusions recording = (BatchExclusions)exclusions;
        recording.Excluded.Add(ids[5]);
        var repository = DispatchProxy.Create<IPhotoListCollectionRepository, ManualRepository>();
        ManualRepository manual = (ManualRepository)repository;
        manual.Definitions = [Definition(ids), Definition([ids[5], ids[2]])];

        IResult result = await Invoke("ListAsync", repository, exclusions, CancellationToken.None);
        var responses = Assert.IsType<PhotoListCollectionResponse[]>(((IValueHttpResult)result).Value);
        Assert.Equal(ids.Where(id => id != ids[5]).Select(id => id.ToString()), responses[0].RevisionIds);
        Assert.Equal(new[] { ids[2].ToString() }, responses[1].RevisionIds);
        Assert.Single(recording.Batches);
        Assert.Equal(100, recording.Batches[0].Length);

        IResult snapshot = await Invoke("CreateSlideshowSnapshotAsync", manual.Definitions[0].Id.Value,
            repository, exclusions, CancellationToken.None);
        var response = Assert.IsType<SmartCollectionSlideshowSnapshotResponse>(((IValueHttpResult)snapshot).Value);
        Assert.Equal(responses[0].RevisionIds, response.Items.Select(item => item.RevisionId));
        Assert.Equal(99, response.Total);
        Assert.Equal(2, recording.Batches.Count);
    }

    private static Task<IResult> Invoke(string method, params object[] args) =>
        (Task<IResult>)typeof(PhotoListCollectionEndpoints).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    private static PhotoListCollectionDefinition Definition(AssetRevisionId[] ids) =>
        new(PhotoListCollectionId.New(), "Example", ids, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    // A per-revision lookup fails deliberately: these tests guard against reintroducing N+1 work.
    public class BatchExclusions : DispatchProxy
    {
        public HashSet<AssetRevisionId> Excluded { get; } = [];
        public List<AssetRevisionId[]> Batches { get; } = [];
        public CancellationToken LastToken { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal(nameof(ISourceCopyExclusionRepository.GetExcludedRevisionIdsAsync), method!.Name);
            var ids = (IReadOnlyCollection<AssetRevisionId>)args![0]!;
            Batches.Add(ids.ToArray());
            LastToken = (CancellationToken)args[1]!;
            LastToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlySet<AssetRevisionId>>(ids.Where(Excluded.Contains).ToHashSet());
        }
    }

    public class ManualRepository : DispatchProxy
    {
        public PhotoListCollectionDefinition[] Definitions { get; set; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(IPhotoListCollectionRepository.ListAsync) => Task.FromResult<IReadOnlyList<PhotoListCollectionDefinition>>(Definitions),
            nameof(IPhotoListCollectionRepository.CreateSlideshowSnapshotAsync) => Task.FromResult<PhotoListCollectionSlideshowSnapshot?>(
                new(Definitions[0].Id, Definitions[0].Name, DateTimeOffset.UnixEpoch, Definitions[0].RevisionIds)),
            _ => throw new NotSupportedException(),
        };
    }

    private sealed class SmartQuery(AssetRevisionId[] ids) : ISmartCollectionQueryRepository
    {
        private SmartCollectionPhoto[] Photos => ids.Select(id => new SmartCollectionPhoto(
            id, AssetId.New(), DateTimeOffset.UnixEpoch, "image/jpeg", 100, 100, null, null, null)).ToArray();
        public Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(SmartCollectionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SmartCollectionPhoto>>(Photos);
        public Task<SmartCollectionPhotoPage> QueryAsync(SmartCollectionFilter filter, int offset = 0, int limit = 40, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmartCollectionPhotoPage(Photos, offset, limit, ids.Length, filter));
        public Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(SmartCollectionId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SmartCollectionSlideshowSnapshot?>(new(id, "Example", DateTimeOffset.UnixEpoch, ids));
    }
}
