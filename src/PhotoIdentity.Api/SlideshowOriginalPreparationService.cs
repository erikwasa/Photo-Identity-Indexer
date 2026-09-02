using System.Collections.Concurrent;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;

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
    int Downloading,
    int Queued,
    int WaitingForRelease,
    int HydrationRequests,
    string Phase,
    DateTimeOffset LastProgressAtUtc,
    long NoProgressSeconds,
    bool NoProgressWarning,
    bool CanRetry,
    long RequiredAdditionalBytes,
    long AvailableManagedCapacity,
    string? Message,
    bool CanContinueWithAvailable);

public sealed class SlideshowOriginalPreparationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static TimeSpan NoProgressWarningThreshold { get; } = TimeSpan.FromMinutes(2);

    private readonly IAssetRevisionLookupRepository _catalogue;
    private readonly CollectionOriginalAccessService _originals;
    private readonly ArchiveHydrationCapacityService _capacity;
    private readonly ArchiveHydrationPolicyConfiguration _policyConfiguration;
    private readonly SlideshowOriginalLeaseRegistry _leases;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public SlideshowOriginalPreparationService(
        IAssetRevisionLookupRepository catalogue,
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
        List<AssetRevisionLookup> revisions = new(requested.Length);
        foreach (AssetRevisionId revisionId in requested)
        {
            AssetRevisionLookup? revision = await _catalogue.GetRevisionAsync(
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
        return session.Snapshot(_timeProvider.GetUtcNow());
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

        return session.Snapshot(_timeProvider.GetUtcNow());
    }

    public SlideshowOriginalPreparationSnapshot? Retry(Guid sessionId)
    {
        CleanupTerminalSessions();

        if (!_sessions.TryGetValue(sessionId, out Session? session))
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        session.Touch(now);
        if (!_leases.Touch(sessionId) &&
            session.State is SlideshowOriginalPreparationStates.Preparing or
                SlideshowOriginalPreparationStates.Ready)
        {
            session.Fail(
                "The best-quality slideshow lease expired. Prepare originals again or continue with available/proxy images.");
            return session.Snapshot(now);
        }

        _ = session.RequestRetry(now);
        return session.Snapshot(now);
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

    public async Task<bool> EndAsync(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out Session? session))
        {
            _leases.Release(sessionId);
            return false;
        }

        session.Cancel();
        _leases.Release(sessionId);

        if (session.RunTask is not null)
        {
            await session.RunTask.ConfigureAwait(false);
        }

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
                session.UpdateAdmission(admission, _timeProvider.GetUtcNow());

                if (admission.Allowed)
                {
                    break;
                }

                if (!admission.WaitingForRelease)
                {
                    session.Fail(BuildAdmissionFailure(admission));
                    return;
                }

                await session.WaitForNextPollAsync(PollInterval);
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
                session.MarkReady(_timeProvider.GetUtcNow());
                return;
            }

            while (ready.Count < session.Revisions.Count)
            {
                session.Cancellation.Token.ThrowIfCancellationRequested();
                _leases.Touch(session.Id);

                int downloading = 0;
                int managedDownloading = 0;
                int waitingForRelease = 0;
                List<AssetRevisionLookup> onlineOnly = [];

                foreach (AssetRevisionLookup revision in session.Revisions)
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
                            break;

                        case CollectionOriginalAccessService.OnlineOnlyState:
                            onlineOnly.Add(revision);
                            break;

                        case CollectionOriginalAccessService.DownloadingState:
                            downloading++;
                            if (status.ManagedHydration)
                            {
                                managedDownloading++;
                            }
                            break;

                        case CollectionOriginalAccessService.ReleasingState:
                            waitingForRelease++;
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
                    session.MarkReady(_timeProvider.GetUtcNow());
                    return;
                }

                int queued = onlineOnly.Count;
                session.UpdateProgress(
                    ready.Count,
                    downloading,
                    queued,
                    waitingForRelease,
                    ProgressPhase(downloading, queued, waitingForRelease),
                    ProgressMessage(downloading, queued, waitingForRelease),
                    _timeProvider.GetUtcNow());

                int slots = Math.Max(0, maximumConcurrent - managedDownloading);
                foreach (AssetRevisionLookup revision in onlineOnly.Take(slots))
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

                        session.RecordHydrationRequest(_timeProvider.GetUtcNow());
                        queued = Math.Max(0, queued - 1);
                        if (requested.State == CollectionOriginalAccessService.DownloadingState)
                        {
                            downloading++;
                        }
                        else if (requested.State == CollectionOriginalAccessService.ReadyState &&
                                 requested.CanView)
                        {
                            ready.Add(revision.RevisionId);
                        }
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

                if (ready.Count >= session.Revisions.Count)
                {
                    session.MarkReady(_timeProvider.GetUtcNow());
                    return;
                }

                session.UpdateProgress(
                    ready.Count,
                    downloading,
                    queued,
                    waitingForRelease,
                    ProgressPhase(downloading, queued, waitingForRelease),
                    ProgressMessage(downloading, queued, waitingForRelease),
                    _timeProvider.GetUtcNow());
                await session.WaitForNextPollAsync(PollInterval);
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

    private static string ProgressPhase(int downloading, int queued, int waitingForRelease) =>
        waitingForRelease > 0
            ? "waiting-release"
            : downloading > 0
                ? "downloading"
                : queued > 0
                    ? "queued"
                    : "verifying";

    private static string? ProgressMessage(int downloading, int queued, int waitingForRelease)
    {
        if (waitingForRelease > 0)
        {
            return "Waiting for OneDrive to finish releasing originals before they can be prepared.";
        }

        if (downloading > 0)
        {
            return "Waiting for OneDrive to download the requested originals.";
        }

        if (queued > 0)
        {
            return "Preparing the next originals for OneDrive download.";
        }

        return "Verifying the remaining local originals.";
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
        private readonly SemaphoreSlim _retrySignal = new(0, 1);
        private int _ready;
        private int _downloading;
        private int _queued;
        private int _waitingForRelease;
        private int _hydrationRequests;
        private string _state = SlideshowOriginalPreparationStates.Preparing;
        private string _phase = "preflight";
        private string? _message = "Preflighting the complete slideshow against the configured storage policy.";
        private long _requiredAdditionalBytes;
        private long _availableManagedCapacity;
        private DateTimeOffset _lastTouchedAtUtc;
        private DateTimeOffset _lastProgressAtUtc;

        public Session(
            Guid id,
            IReadOnlyList<AssetRevisionLookup> revisions,
            CancellationTokenSource cancellation,
            DateTimeOffset createdAtUtc)
        {
            Id = id;
            Revisions = revisions;
            Cancellation = cancellation;
            _lastTouchedAtUtc = createdAtUtc;
            _lastProgressAtUtc = createdAtUtc;
        }

        public Guid Id { get; }
        public IReadOnlyList<AssetRevisionLookup> Revisions { get; }
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

        public void UpdateAdmission(
            ArchiveHydrationSetAdmission admission,
            DateTimeOffset now)
        {
            lock (_gate)
            {
                string phase = admission.WaitingForRelease ? "waiting-release" : "preflight";
                string? message = admission.WaitingForRelease
                    ? admission.Message ?? "Waiting for managed storage to be released before preparation can continue."
                    : "Preflighting the complete slideshow against the configured storage policy.";

                bool changed =
                    _requiredAdditionalBytes != admission.RequiredAdditionalBytes ||
                    _availableManagedCapacity != admission.AvailableManagedCapacity ||
                    !string.Equals(_phase, phase, StringComparison.Ordinal) ||
                    !string.Equals(_message, message, StringComparison.Ordinal);

                _requiredAdditionalBytes = admission.RequiredAdditionalBytes;
                _availableManagedCapacity = admission.AvailableManagedCapacity;
                _phase = phase;
                _message = message;
                if (changed)
                {
                    _lastProgressAtUtc = now;
                }
            }
        }

        public void UpdateProgress(
            int ready,
            int downloading,
            int queued,
            int waitingForRelease,
            string phase,
            string? message,
            DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_state != SlideshowOriginalPreparationStates.Preparing)
                {
                    return;
                }

                bool changed =
                    _ready != ready ||
                    _downloading != downloading ||
                    _queued != queued ||
                    _waitingForRelease != waitingForRelease ||
                    !string.Equals(_phase, phase, StringComparison.Ordinal) ||
                    !string.Equals(_message, message, StringComparison.Ordinal);

                _ready = ready;
                _downloading = downloading;
                _queued = queued;
                _waitingForRelease = waitingForRelease;
                _phase = phase;
                _message = message;
                if (changed)
                {
                    _lastProgressAtUtc = now;
                }
            }
        }

        public void RecordHydrationRequest(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_state != SlideshowOriginalPreparationStates.Preparing)
                {
                    return;
                }

                _hydrationRequests++;
                _lastProgressAtUtc = now;
            }
        }

        public bool RequestRetry(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_state != SlideshowOriginalPreparationStates.Preparing)
                {
                    return false;
                }

                _phase = "retrying";
                _message = "Retry requested; rechecking OneDrive and the same slideshow snapshot.";
                _lastProgressAtUtc = now;
            }

            if (_retrySignal.CurrentCount == 0)
            {
                try
                {
                    _retrySignal.Release();
                }
                catch (SemaphoreFullException)
                {
                }
            }

            return true;
        }

        public async Task WaitForNextPollAsync(TimeSpan delay)
        {
            _ = await _retrySignal.WaitAsync(delay, Cancellation.Token);
        }

        public void MarkReady(DateTimeOffset now)
        {
            lock (_gate)
            {
                _ready = Revisions.Count;
                _downloading = 0;
                _queued = 0;
                _waitingForRelease = 0;
                _state = SlideshowOriginalPreparationStates.Ready;
                _phase = "ready";
                _message = null;
                _lastProgressAtUtc = now;
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
                _phase = "failed";
                _message = message;
            }
        }

        public void MarkCancelled()
        {
            lock (_gate)
            {
                _state = SlideshowOriginalPreparationStates.Cancelled;
                _phase = "cancelled";
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

        public SlideshowOriginalPreparationSnapshot Snapshot(DateTimeOffset now)
        {
            lock (_gate)
            {
                long noProgressSeconds = _state == SlideshowOriginalPreparationStates.Preparing
                    ? Math.Max(0L, (long)(now - _lastProgressAtUtc).TotalSeconds)
                    : 0L;
                bool noProgressWarning =
                    _state == SlideshowOriginalPreparationStates.Preparing &&
                    _ready < Revisions.Count &&
                    now - _lastProgressAtUtc >= NoProgressWarningThreshold;

                return new SlideshowOriginalPreparationSnapshot(
                    Id,
                    _state,
                    _ready,
                    Revisions.Count,
                    _downloading,
                    _queued,
                    _waitingForRelease,
                    _hydrationRequests,
                    _phase,
                    _lastProgressAtUtc,
                    noProgressSeconds,
                    noProgressWarning,
                    noProgressWarning,
                    _requiredAdditionalBytes,
                    _availableManagedCapacity,
                    _message,
                    _state == SlideshowOriginalPreparationStates.Failed);
            }
        }
    }

}
