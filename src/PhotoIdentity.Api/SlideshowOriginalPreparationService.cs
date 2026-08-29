using System.Collections.Concurrent;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public static class SlideshowOriginalPreparationStates
{
    public const string Preparing = "preparing";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed record SlideshowOriginalPreparationSnapshot(
    Guid SessionId,
    string State,
    int Ready,
    int Total,
    long RequiredAdditionalBytes,
    long AvailableManagedCapacity,
    string? Message,
    bool CanContinueWithAvailable);

public sealed class SlideshowOriginalPreparationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly SqliteLocalBatchRepository _catalogue;
    private readonly CollectionOriginalAccessService _originals;
    private readonly ArchiveHydrationCapacityService _capacity;
    private readonly ArchiveHydrationPolicyConfiguration _policyConfiguration;
    private readonly SlideshowOriginalLeaseRegistry _leases;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public SlideshowOriginalPreparationService(
        SqliteLocalBatchRepository catalogue,
        CollectionOriginalAccessService originals,
        ArchiveHydrationCapacityService capacity,
        ArchiveHydrationPolicyConfiguration policyConfiguration,
        SlideshowOriginalLeaseRegistry leases,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(policyConfiguration);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _catalogue = catalogue;
        _originals = originals;
        _capacity = capacity;
        _policyConfiguration = policyConfiguration;
        _leases = leases;
        _timeProvider = timeProvider;
    }

    public async Task<SlideshowOriginalPreparationSnapshot> StartAsync(
        IReadOnlyCollection<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        CleanupTerminalSessions();

        AssetRevisionId[] requested = revisionIds.Distinct().ToArray();
        List<CatalogueProcessingAssetRevision> revisions = new(requested.Length);
        foreach (AssetRevisionId revisionId in requested)
        {
            CatalogueProcessingAssetRevision? revision = await _catalogue.GetAssetRevisionAsync(
                revisionId,
                cancellationToken);
            if (revision is null)
            {
                throw new KeyNotFoundException(
                    "One or more slideshow originals no longer exist in the catalogue.");
            }

            if (!string.Equals(revision.SourceKind, "local-folder", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "One or more slideshow originals are not backed by the supported local-folder archive source.");
            }

            revisions.Add(revision);
        }

        Guid sessionId = Guid.NewGuid();
        CancellationTokenSource cancellation = new();
        Session session = new(
            sessionId,
            revisions.ToArray(),
            cancellation,
            _timeProvider.GetUtcNow());

        if (!_sessions.TryAdd(sessionId, session))
        {
            cancellation.Dispose();
            throw new InvalidOperationException("The slideshow preparation session could not be created.");
        }

        _leases.Protect(
            sessionId,
            revisions.Select(revision => new SlideshowOriginalLeaseMember(
                revision.RevisionId,
                revision.AssetId)));

        session.RunTask = RunPreparationAsync(session);
        return session.Snapshot();
    }

    public SlideshowOriginalPreparationSnapshot? GetStatus(Guid sessionId)
    {
        CleanupTerminalSessions();

        if (!_sessions.TryGetValue(sessionId, out Session? session))
        {
            return null;
        }

        session.Touch(_timeProvider.GetUtcNow());
        if (!_leases.Touch(sessionId) &&
            session.State is SlideshowOriginalPreparationStates.Preparing or
                SlideshowOriginalPreparationStates.Ready)
        {
            session.Fail(
                "The best-quality slideshow lease expired. Prepare originals again or continue with available/proxy images.");
        }

        return session.Snapshot();
    }

    public async Task<VerifiedCollectionOriginal?> OpenPreparedOriginalAsync(
        Guid sessionId,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        CleanupTerminalSessions();

        if (!_sessions.TryGetValue(sessionId, out Session? session) ||
            session.State != SlideshowOriginalPreparationStates.Ready ||
            !session.Contains(revisionId))
        {
            return null;
        }

        session.Touch(_timeProvider.GetUtcNow());
        if (!_leases.Touch(sessionId))
        {
            session.Fail(
                "The best-quality slideshow lease expired. Prepare originals again or continue with available/proxy images.");
            return null;
        }

        VerifiedCollectionOriginal? original = await _originals.OpenVerifiedAsync(
            revisionId,
            cancellationToken);
        if (original is null)
        {
            session.Fail(
                "A prepared original became unavailable or no longer matches its immutable catalogue revision.");
        }

        return original;
    }

    public bool End(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out Session? session))
        {
            _leases.Release(sessionId);
            return false;
        }

        session.Cancel();
        _leases.Release(sessionId);
        return true;
    }

    private async Task RunPreparationAsync(Session session)
    {
        try
        {
            ArchiveHydrationSetAdmission admission;
            while (true)
            {
                session.Cancellation.Token.ThrowIfCancellationRequested();
                _leases.Touch(session.Id);

                admission = await _capacity.PreflightHydrationSetAsync(
                    session.Revisions,
                    session.Cancellation.Token);
                session.UpdateAdmission(admission);

                if (admission.Allowed)
                {
                    break;
                }

                if (!admission.WaitingForRelease)
                {
                    session.Fail(BuildAdmissionFailure(admission));
                    return;
                }

                await Task.Delay(PollInterval, session.Cancellation.Token);
            }

            int maximumConcurrent = admission.MaximumConcurrentOperations;
            if (maximumConcurrent <= 0 &&
                _policyConfiguration.TryGetPolicy(out ArchiveHydrationPolicy? policy, out _) &&
                policy is not null)
            {
                maximumConcurrent = policy.MaximumConcurrentOperations;
            }

            maximumConcurrent = Math.Max(1, maximumConcurrent);
            HashSet<AssetRevisionId> ready = [];

            if (session.Revisions.Count == 0)
            {
                session.MarkReady();
                return;
            }

            while (ready.Count < session.Revisions.Count)
            {
                session.Cancellation.Token.ThrowIfCancellationRequested();
                _leases.Touch(session.Id);

                int managedDownloading = 0;
                List<CatalogueProcessingAssetRevision> onlineOnly = [];
                bool observedProgress = false;

                foreach (CatalogueProcessingAssetRevision revision in session.Revisions)
                {
                    if (ready.Contains(revision.RevisionId))
                    {
                        continue;
                    }

                    CollectionOriginalAccessSnapshot? status = await _originals.GetStatusAsync(
                        revision.RevisionId,
                        session.Cancellation.Token);
                    if (status is null)
                    {
                        session.Fail(
                            "One or more slideshow originals are no longer available in the catalogue.");
                        return;
                    }

                    switch (status.State)
                    {
                        case CollectionOriginalAccessService.ReadyState:
                            if (!status.CanView)
                            {
                                session.Fail(
                                    "One or more verified originals cannot be rendered directly by this browser. Continue with available/proxy images instead.");
                                return;
                            }

                            ready.Add(revision.RevisionId);
                            session.SetReadyCount(ready.Count);
                            observedProgress = true;
                            break;

                        case CollectionOriginalAccessService.OnlineOnlyState:
                            onlineOnly.Add(revision);
                            break;

                        case CollectionOriginalAccessService.DownloadingState:
                            if (status.ManagedHydration)
                            {
                                managedDownloading++;
                            }
                            break;

                        case CollectionOriginalAccessService.ReleasingState:
                            // An earlier release request cannot be revoked safely. Wait until
                            // OneDrive reports online-only, then hydrate it under this session.
                            break;

                        case CollectionOriginalAccessService.HashMismatchState:
                            session.Fail(
                                "A local original does not match its immutable catalogue revision. Best-quality playback cannot be promised.");
                            return;

                        case CollectionOriginalAccessService.UnavailableState:
                            session.Fail(
                                "One or more authoritative originals are unavailable. Continue with available/proxy images or cancel.");
                            return;

                        default:
                            session.Fail(
                                "One or more authoritative originals could not be prepared. Continue with available/proxy images or cancel.");
                            return;
                    }
                }

                if (ready.Count >= session.Revisions.Count)
                {
                    session.MarkReady();
                    return;
                }

                int slots = Math.Max(0, maximumConcurrent - managedDownloading);
                foreach (CatalogueProcessingAssetRevision revision in onlineOnly.Take(slots))
                {
                    try
                    {
                        CollectionOriginalAccessSnapshot? requested = await _originals.RequestHydrationAsync(
                            revision.RevisionId,
                            session.Cancellation.Token);
                        if (requested is null)
                        {
                            session.Fail(
                                "One or more slideshow originals could not be resolved for hydration.");
                            return;
                        }

                        observedProgress = true;
                    }
                    catch (InvalidOperationException exception)
                        when (exception.Message.Contains("concurrency limit", StringComparison.OrdinalIgnoreCase) ||
                              exception.Message.Contains("retry after", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    catch (Exception exception)
                        when (exception is InvalidOperationException or
                            IOException or
                            PlatformNotSupportedException)
                    {
                        session.Fail(
                            $"Best-quality original preparation failed: {PathFreeMessage(exception.Message)}");
                        return;
                    }
                }

                session.SetPreparingMessage(observedProgress
                    ? null
                    : "Waiting for OneDrive to make the remaining originals local.");
                await Task.Delay(PollInterval, session.Cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
            session.MarkCancelled();
        }
        catch (Exception exception)
        {
            session.Fail(
                $"Best-quality original preparation failed: {PathFreeMessage(exception.Message)}");
        }
    }

    private void CleanupTerminalSessions()
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().Subtract(TimeSpan.FromHours(1));
        foreach ((Guid id, Session session) in _sessions)
        {
            bool terminal =
                session.State == SlideshowOriginalPreparationStates.Failed ||
                session.State == SlideshowOriginalPreparationStates.Cancelled;
            if (terminal &&
                session.LastTouchedAtUtc < cutoff &&
                _sessions.TryRemove(id, out Session? removed))
            {
                removed.Cancel();
                _leases.Release(id);
            }
        }
    }

    private static string BuildAdmissionFailure(ArchiveHydrationSetAdmission admission) =>
        $"{admission.Message ?? "Best-quality slideshow cannot prepare all originals under the current storage policy."} " +
        $"Required additional bytes: {admission.RequiredAdditionalBytes}. " +
        $"Available managed capacity: {admission.AvailableManagedCapacity}.";

    private static string PathFreeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "The operation could not be completed.";
        }

        // Service/platform messages used here are designed to be path-free. Keep the public
        // preparation contract aggregate and avoid relaying quoted/drive-qualified details.
        return message.Contains(":\\", StringComparison.Ordinal) ||
            message.Contains(":/", StringComparison.Ordinal)
            ? "The operation could not be completed under the current archive/storage state."
            : message;
    }

    private sealed class Session
    {
        private readonly object _gate = new();
        private int _ready;
        private string _state = SlideshowOriginalPreparationStates.Preparing;
        private string? _message;
        private long _requiredAdditionalBytes;
        private long _availableManagedCapacity;
        private DateTimeOffset _lastTouchedAtUtc;

        public Session(
            Guid id,
            IReadOnlyList<CatalogueProcessingAssetRevision> revisions,
            CancellationTokenSource cancellation,
            DateTimeOffset createdAtUtc)
        {
            Id = id;
            Revisions = revisions;
            Cancellation = cancellation;
            _lastTouchedAtUtc = createdAtUtc;
        }

        public Guid Id { get; }
        public IReadOnlyList<CatalogueProcessingAssetRevision> Revisions { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task? RunTask { get; set; }

        public string State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public DateTimeOffset LastTouchedAtUtc
        {
            get
            {
                lock (_gate)
                {
                    return _lastTouchedAtUtc;
                }
            }
        }

        public bool Contains(AssetRevisionId revisionId) =>
            Revisions.Any(revision => revision.RevisionId == revisionId);

        public void Touch(DateTimeOffset now)
        {
            lock (_gate)
            {
                _lastTouchedAtUtc = now;
            }
        }

        public void UpdateAdmission(ArchiveHydrationSetAdmission admission)
        {
            lock (_gate)
            {
                _requiredAdditionalBytes = admission.RequiredAdditionalBytes;
                _availableManagedCapacity = admission.AvailableManagedCapacity;
                _message = admission.WaitingForRelease ? admission.Message : null;
            }
        }

        public void SetReadyCount(int ready)
        {
            lock (_gate)
            {
                _ready = ready;
                _message = null;
            }
        }

        public void SetPreparingMessage(string? message)
        {
            lock (_gate)
            {
                if (_state == SlideshowOriginalPreparationStates.Preparing)
                {
                    _message = message;
                }
            }
        }

        public void MarkReady()
        {
            lock (_gate)
            {
                _ready = Revisions.Count;
                _state = SlideshowOriginalPreparationStates.Ready;
                _message = null;
            }
        }

        public void Fail(string message)
        {
            lock (_gate)
            {
                if (_state == SlideshowOriginalPreparationStates.Cancelled)
                {
                    return;
                }

                _state = SlideshowOriginalPreparationStates.Failed;
                _message = message;
            }
        }

        public void MarkCancelled()
        {
            lock (_gate)
            {
                _state = SlideshowOriginalPreparationStates.Cancelled;
                _message = "Best-quality original preparation was cancelled.";
            }
        }

        public void Cancel()
        {
            if (!Cancellation.IsCancellationRequested)
            {
                Cancellation.Cancel();
            }

            MarkCancelled();
        }

        public SlideshowOriginalPreparationSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new SlideshowOriginalPreparationSnapshot(
                    Id,
                    _state,
                    _ready,
                    Revisions.Count,
                    _requiredAdditionalBytes,
                    _availableManagedCapacity,
                    _message,
                    _state == SlideshowOriginalPreparationStates.Failed);
            }
        }
    }
}
