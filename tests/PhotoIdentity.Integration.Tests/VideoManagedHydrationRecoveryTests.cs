using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Fully synthetic WI-0207 crash/restart and dependent-original
/// bookkeeping. No private originals, media bytes or OneDrive actions.
/// </summary>
public sealed class VideoManagedHydrationRecoveryTests
{
    [Theory]
    [InlineData(false, false, AssetAvailability.Local, false,
        VideoManagedLocalReuseDisposition.Unowned)]
    [InlineData(true, true, AssetAvailability.Local, false,
        VideoManagedLocalReuseDisposition.Unowned)]
    [InlineData(true, false, AssetAvailability.Local, false,
        VideoManagedLocalReuseDisposition.Reuse)]
    [InlineData(true, false, AssetAvailability.Local, true,
        VideoManagedLocalReuseDisposition.Reuse)]
    [InlineData(true, false, AssetAvailability.Downloading, true,
        VideoManagedLocalReuseDisposition.NoLongerLocal)]
    [InlineData(true, false, AssetAvailability.OnlineOnly, false,
        VideoManagedLocalReuseDisposition.NoLongerLocal)]
    [InlineData(true, false, AssetAvailability.Unavailable, false,
        VideoManagedLocalReuseDisposition.NoLongerLocal)]
    [InlineData(true, true, AssetAvailability.Local, true,
        VideoManagedLocalReuseDisposition.ReleasePending)]
    public void Local_restart_decisions_never_claim_unmanaged_originals(
        bool hasOwnership, bool releasedOrPending, AssetAvailability availability,
        bool pinned, VideoManagedLocalReuseDisposition expected)
    {
        AssetRevisionId id = AssetRevisionId.New();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArchiveManagedHydrationState? lease = !hasOwnership
            ? null
            : new ArchiveManagedHydrationState(
                id, now, releasedOrPending ? now : null,
                releasedOrPending && !pinned ? now : null);
        var state = new OneDriveFilesOnDemandState(
            availability, pinned, false);
        Assert.Equal(expected,
            VideoManagedLocalReusePolicy.Evaluate(lease, state));
    }

    [Theory]
    [InlineData(true, true, AssetAvailability.OnlineOnly, false, true)]
    [InlineData(true, false, AssetAvailability.OnlineOnly, false, false)]
    [InlineData(false, true, AssetAvailability.OnlineOnly, false, false)]
    [InlineData(true, true, AssetAvailability.Local, false, false)]
    [InlineData(true, true, AssetAvailability.Local, true, false)]
    [InlineData(true, true, AssetAvailability.Downloading, true, false)]
    [InlineData(true, true, AssetAvailability.Error, false, false)]
    [InlineData(true, true, AssetAvailability.OnlineOnly, true, false)]
    public void Only_video_release_requested_and_already_online_only_may_close(
        bool video, bool requested, AssetAvailability availability,
        bool pinned, bool expected)
    {
        var lease = MakeLease(
            "unused", "unused.mov", video, requested);
        var state = new OneDriveFilesOnDemandState(
            availability, pinned, false);
        Assert.Equal(expected,
            VideoManagedHydrationRecovery.CanCloseReleaseReceipt(lease, state));
    }

    [Fact]
    public async Task Interrupted_release_reconciles_without_any_cloud_state_changes()
    {
        string root = NewScratchDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2024"));
            var valid = MakeLease(root, "2024/released.mov", true, true);
            var pinned = MakeLease(root, "2024/user-pinned.mov", true, true);
            var missing = MakeLease(root, "2024/missing.mov", true, true);
            var photo = MakeLease(root, "2024/photo.jpg", false, true);
            var unrequested = MakeLease(root, "2024/active.mov", true, false);
            var escape = MakeLease(root, "../private.mov", true, true);

            var repository = new FakeOwnership(
                [valid, pinned, missing, photo, unrequested, escape]);
            var platform = new FakeOneDrive(
                new Dictionary<string, OneDriveFilesOnDemandState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [Path.Combine(root, "2024", "released.mov")] =
                        new(AssetAvailability.OnlineOnly, false, true),
                    [Path.Combine(root, "2024", "user-pinned.mov")] =
                        new(AssetAvailability.Local, true, false),
                    [Path.Combine(root, "2024", "missing.mov")] =
                        new(AssetAvailability.Unavailable, false, false)
                });
            var service = new VideoManagedHydrationRecovery(
                repository, platform, TimeProvider.System);
            VideoManagedHydrationRecoverySummary result =
                await service.ReconcileAsync();

            Assert.Equal(4, result.Inspected); // 3 video release receipts + escape.
            Assert.Equal(1, result.Reconciled);
            Assert.Equal(3, result.Retained);
            Assert.Equal(0, result.Failed);
            Assert.Equal([valid.AssetRevisionId], repository.Released);
            Assert.Equal(3, platform.MetadataChecks);
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal(0, platform.ReleaseRequests);
            Assert.DoesNotContain(photo.AssetRevisionId, repository.Released);
            Assert.DoesNotContain(unrequested.AssetRevisionId, repository.Released);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Recovery_is_bounded_and_broken_metadata_cannot_clear_ownership()
    {
        string root = NewScratchDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2025"));
            ArchiveManagedHydrationLeaseState[] receipts =
                Enumerable.Range(0, 20)
                .Select(i => MakeLease(root, $"2025/{i:D2}.mov", true, true))
                .ToArray();
            var repository = new FakeOwnership(receipts);
            var platform = new FakeOneDrive(
                new Dictionary<string, OneDriveFilesOnDemandState>());
            var recovery = new VideoManagedHydrationRecovery(
                repository, platform, TimeProvider.System);
            VideoManagedHydrationRecoverySummary result =
                await recovery.ReconcileAsync();

            Assert.Equal(VideoManagedHydrationRecovery.MaximumReceiptsPerAdvance,
                result.Inspected);
            Assert.Equal(0, result.Reconciled);
            Assert.Equal(16, result.Retained);
            Assert.Equal(16, platform.MetadataChecks);
            Assert.Empty(repository.Released);
            Assert.Equal(0, platform.ReleaseRequests);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Failed_receipt_update_preserves_ownership_without_one_drive_mutation()
    {
        string root = NewScratchDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026"));
            var lease = MakeLease(root, "2026/synthetic.mov", true, true);
            var repository = new FakeOwnership([lease])
            {
                RejectMarkReleased = true
            };
            var platform = new FakeOneDrive(
                new Dictionary<string, OneDriveFilesOnDemandState>
                {
                    [Path.Combine(root, "2026", "synthetic.mov")] =
                        new(AssetAvailability.OnlineOnly, false, true)
                });
            var recovery = new VideoManagedHydrationRecovery(
                repository, platform, TimeProvider.System);

            VideoManagedHydrationRecoverySummary result =
                await recovery.ReconcileAsync();

            Assert.Equal(1, result.Inspected);
            Assert.Equal(0, result.Reconciled);
            Assert.Equal(0, result.Retained);
            Assert.Equal(1, result.Failed);
            Assert.Empty(repository.Released);
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal(0, platform.ReleaseRequests);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Recovery_rejects_unsafe_roots_traversal_and_linked_ancestors()
    {
        string root = NewScratchDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2024"));
            Assert.Null(VideoManagedHydrationRecovery.TryResolveMetadataPath(
                root, "../secret.mov"));
            Assert.Null(VideoManagedHydrationRecovery.TryResolveMetadataPath(
                root, "/absolute.mov"));
            Assert.Null(VideoManagedHydrationRecovery.TryResolveMetadataPath(
                root, "C:/windows/test.mov"));
            Assert.Null(VideoManagedHydrationRecovery.TryResolveMetadataPath(
                "relative-folder", "relative.mov"));
            Assert.Null(VideoManagedHydrationRecovery.TryResolveMetadataPath(
                @"\\server\share", "remote.mov"));
            Assert.Equal(Path.Combine(root, "2024", "ok.mov"),
                VideoManagedHydrationRecovery.TryResolveMetadataPath(
                    root, "2024/ok.mov"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Worker_reconciles_only_during_operator_advance_and_restores_managed_local()
    {
        string root = RepositoryRoot();
        string worker = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoArchiveProcessingService.cs"));
        string coordinator = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoManagedHydrationCoordinator.cs"));
        string recovery = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoManagedHydrationRecovery.cs"));
        string capacity = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "ArchiveHydrationCapacityService.cs"));
        string program = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "Program.cs"));

        Assert.Contains("await hydrationRecovery.ReconcileAsync(token)", worker);
        Assert.Contains("ResumeIfManagedLocalAsync(", worker);
        Assert.Contains("ownedHydration = await managedHydration", worker);
        Assert.Contains("ownership.GetAsync(revisionId, token)", coordinator);
        Assert.Contains("await capacity.TouchAsync(revisionId, token)", coordinator);
        Assert.Contains("if (stored is not { IsActive: true })", coordinator);
        Assert.Contains("TryMarkVideoReleaseConfirmedAsync(", recovery);
        Assert.Contains("TryMarkVideoReleaseConfirmedAsync(", capacity);
        Assert.Contains("if (lease.IsVideo && state.IsPinned)", capacity);
        Assert.Contains("GetPendingVideoReleasesAsync(", recovery);
        Assert.DoesNotContain("RequestOnlineOnlyAsync(", recovery);
        Assert.DoesNotContain("RequestHydrationAsync(", recovery);
        Assert.DoesNotContain("FileStream(", recovery);
        Assert.Contains("AddSingleton<VideoManagedHydrationRecovery>()", program);
    }

    private static ArchiveManagedHydrationLeaseState MakeLease(
        string root, string key, bool video, bool requested)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ArchiveManagedHydrationLeaseState(
            AssetRevisionId.New(), AssetId.New(), 1024,
            root, key, now, now,
            requested ? now : null, video);
    }

    private static string NewScratchDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "pi-wi0207-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RepositoryRoot()
    {
        foreach (string location in new[]
            { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? folder = new(Path.GetFullPath(location));
            for (int depth = 0; folder is not null && depth < 12;
                 depth++, folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "PhotoIdentity.slnx")))
                    return folder.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Photo Identity repository root not found.");
    }

    private sealed class FakeOwnership(
        IReadOnlyList<ArchiveManagedHydrationLeaseState> leases)
        : IArchiveHydrationRepository
    {
        public List<AssetRevisionId> Released { get; } = [];
        public bool RejectMarkReleased { get; set; }

        public Task<IReadOnlyList<ArchiveManagedHydrationLeaseState>>
            GetActiveLeasesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(leases);
        public Task<ArchiveManagedHydrationState?> GetAsync(
            AssetRevisionId id, CancellationToken token = default) =>
            Task.FromResult<ArchiveManagedHydrationState?>(null);
        public Task<ArchiveManagedHydrationState> ClaimAsync(
            AssetRevisionId id, DateTimeOffset when,
            CancellationToken token = default) =>
            throw new NotSupportedException("No production hydration in synthetic test.");
        public Task TouchAsync(
            AssetRevisionId id, DateTimeOffset when,
            CancellationToken token = default) =>
            throw new NotSupportedException("No production hydration in synthetic test.");
        public Task<ArchiveManagedHydrationState> MarkReleaseRequestedAsync(
            AssetRevisionId id, DateTimeOffset when,
            CancellationToken token = default) =>
            throw new NotSupportedException("No cloud release in synthetic test.");
        public Task<bool> TryMarkVideoReleaseConfirmedAsync(
            AssetRevisionId id, DateTimeOffset when,
            CancellationToken token = default)
        {
            if (RejectMarkReleased)
                throw new InvalidOperationException(
                    "Synthetic database rejected released receipt.");
            if (!leases.Any(x => x.AssetRevisionId == id &&
                x.IsVideo && x.IsReleaseRequested))
                return Task.FromResult(false);
            Released.Add(id);
            return Task.FromResult(true);
        }

        public Task MarkReleasedAsync(
            AssetRevisionId id, DateTimeOffset when,
            CancellationToken token = default) =>
            throw new InvalidOperationException(
                "Recovery must use only atomic video release confirmation.");
    }

    private sealed class FakeOneDrive(
        IReadOnlyDictionary<string, OneDriveFilesOnDemandState> states)
        : IOneDriveFilesOnDemandPlatform
    {
        public int MetadataChecks { get; private set; }
        public int HydrationRequests { get; private set; }
        public int ReleaseRequests { get; private set; }

        public OneDriveFilesOnDemandState GetState(string path)
        {
            MetadataChecks++;
            return states.TryGetValue(
                    path, out OneDriveFilesOnDemandState? state) &&
                    state is not null
                ? state
                : new(AssetAvailability.Unavailable, false, false);
        }

        public Task RequestHydrationAsync(
            string path, CancellationToken token = default)
        {
            HydrationRequests++;
            throw new InvalidOperationException("Forbidden hydration.");
        }

        public Task RequestOnlineOnlyAsync(
            string path, CancellationToken token = default)
        {
            ReleaseRequests++;
            throw new InvalidOperationException("Forbidden release.");
        }
    }
}
