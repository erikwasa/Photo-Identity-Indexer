using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class IdentityMatchFollowUpPlannerTests
{
    private static readonly ModelId ModelId = new("sface-test");
    private static readonly Sha256Digest ModelHash = new(new string('a', 64));
    private static readonly ReviewIdentityMatchEvidenceVersion Baseline = new(10, 4, 2, 100);

    [Fact]
    public async Task Multiple_evidence_changes_coalesce_into_one_later_run()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        FakeEvidenceReader evidence = new(Baseline with { ReviewActionId = 11 });
        FakeRunRepository runs = new(CreateRun(ReviewIdentityMatchRegenerationStatuses.Completed, Baseline))
        {
            EvidenceAtStart = () => evidence.Current,
        };
        IdentityMatchFollowUpPlanner planner = CreatePlanner(clock, evidence, runs, enabled: true, delayMilliseconds: 30_000);

        Assert.False(await planner.TryStartDueAsync());
        IdentityMatchFollowUpState initial = await planner.GetStateAsync(ModelId, ModelHash);
        Assert.Equal("queued", initial.Status);
        Assert.Equal(clock.GetUtcNow().AddSeconds(30), initial.DueAtUtc);

        clock.Advance(TimeSpan.FromSeconds(20));
        evidence.Current = evidence.Current with { ReviewActionId = 12 };
        Assert.False(await planner.TryStartDueAsync());
        IdentityMatchFollowUpState reset = await planner.GetStateAsync(ModelId, ModelHash);
        Assert.Equal(clock.GetUtcNow().AddSeconds(30), reset.DueAtUtc);

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(await planner.TryStartDueAsync());
        Assert.Equal(0, runs.StartCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await planner.TryStartDueAsync());
        Assert.Equal(1, runs.StartCount);
        Assert.Equal(IdentityMatchFollowUpPlanner.RequestedBy, runs.Latest?.RequestedBy);

        Assert.False(await planner.TryStartDueAsync());
        Assert.Equal(1, runs.StartCount);
    }

    [Fact]
    public async Task Durable_evidence_mismatch_is_rediscovered_after_planner_restart()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        FakeEvidenceReader evidence = new(Baseline with { ReviewActionId = 11 });
        FakeRunRepository runs = new(CreateRun(ReviewIdentityMatchRegenerationStatuses.Completed, Baseline))
        {
            EvidenceAtStart = () => evidence.Current,
        };

        IdentityMatchFollowUpPlanner beforeRestart = CreatePlanner(clock, evidence, runs, enabled: true, delayMilliseconds: 30_000);
        Assert.False(await beforeRestart.TryStartDueAsync());

        clock.Advance(TimeSpan.FromSeconds(10));
        IdentityMatchFollowUpPlanner afterRestart = CreatePlanner(clock, evidence, runs, enabled: true, delayMilliseconds: 30_000);
        IdentityMatchFollowUpState recovered = await afterRestart.GetStateAsync(ModelId, ModelHash);
        Assert.Equal("queued", recovered.Status);
        Assert.Equal(clock.GetUtcNow().AddSeconds(30), recovered.DueAtUtc);

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await afterRestart.TryStartDueAsync());
        Assert.Equal(1, runs.StartCount);
    }

    [Fact]
    public async Task Completed_automatic_assignments_do_not_schedule_recursive_follow_up()
    {
        ReviewIdentityMatchEvidenceVersion current =
            ReviewIdentityMatchEvidenceVersions.ExpectedAfterAutomaticAssignments(Baseline, automaticallyAssignedCount: 2);
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        FakeEvidenceReader evidence = new(current);
        FakeRunRepository runs = new(CreateRun(
            ReviewIdentityMatchRegenerationStatuses.Completed,
            Baseline,
            automaticallyAssignedCount: 2));
        IdentityMatchFollowUpPlanner planner = CreatePlanner(clock, evidence, runs, enabled: true, delayMilliseconds: 0);

        Assert.False(await planner.TryStartDueAsync());
        IdentityMatchFollowUpState state = await planner.GetStateAsync(ModelId, ModelHash);
        Assert.Equal("current", state.Status);
        Assert.Equal(0, runs.StartCount);
    }

    [Fact]
    public async Task Change_during_active_run_waits_for_later_follow_up()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        FakeEvidenceReader evidence = new(Baseline with { ReviewActionId = 11 });
        FakeRunRepository runs = new(CreateRun(ReviewIdentityMatchRegenerationStatuses.Running, Baseline));
        IdentityMatchFollowUpPlanner planner = CreatePlanner(clock, evidence, runs, enabled: true, delayMilliseconds: 0);

        Assert.False(await planner.TryStartDueAsync());
        IdentityMatchFollowUpState state = await planner.GetStateAsync(ModelId, ModelHash);
        Assert.Equal("running", state.Status);
        Assert.True(state.QueuedAfterActiveRun);
        Assert.Equal(0, runs.StartCount);
    }

    [Fact]
    public async Task Disabled_follow_up_never_starts_automatic_run()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        FakeEvidenceReader evidence = new(Baseline with { ReviewActionId = 11 });
        FakeRunRepository runs = new(CreateRun(ReviewIdentityMatchRegenerationStatuses.Completed, Baseline));
        IdentityMatchFollowUpPlanner planner = CreatePlanner(clock, evidence, runs, enabled: false, delayMilliseconds: 0);

        Assert.False(await planner.TryStartDueAsync());
        IdentityMatchFollowUpState state = await planner.GetStateAsync(ModelId, ModelHash);
        Assert.False(state.Enabled);
        Assert.Equal("disabled", state.Status);
        Assert.Equal(0, runs.StartCount);
    }

    private static IdentityMatchFollowUpPlanner CreatePlanner(
        TimeProvider clock,
        IIdentityMatchEvidenceVersionReader evidence,
        IIdentityMatchRegenerationRepository runs,
        bool enabled,
        int delayMilliseconds) => new(
            new FakeModelRepository(),
            runs,
            evidence,
            new FakePolicyRepository(clock),
            clock,
            new IdentityMatchFollowUpConfiguration(enabled, delayMilliseconds));

    private static ReviewIdentityMatchRegenerationRun CreateRun(
        string status,
        ReviewIdentityMatchEvidenceVersion evidence,
        int automaticallyAssignedCount = 0) => new(
            Guid.NewGuid(),
            ModelId,
            ModelHash,
            PolicyVersion: 1,
            status,
            evidence,
            TargetCount: 10,
            ProcessedTargetCount: status == ReviewIdentityMatchRegenerationStatuses.Completed ? 10 : 0,
            SuggestedTargetCount: 0,
            SuggestionCount: 0,
            automaticallyAssignedCount,
            ErrorCount: 0,
            RequestedBy: "test",
            RequestedAtUtc: new DateTimeOffset(2026, 9, 13, 14, 0, 0, TimeSpan.Zero),
            StartedAtUtc: status == ReviewIdentityMatchRegenerationStatuses.Pending ? null : new DateTimeOffset(2026, 9, 13, 14, 0, 1, TimeSpan.Zero),
            CompletedAtUtc: status == ReviewIdentityMatchRegenerationStatuses.Completed ? new DateTimeOffset(2026, 9, 13, 14, 1, 0, TimeSpan.Zero) : null,
            UpdatedAtUtc: new DateTimeOffset(2026, 9, 13, 14, 1, 0, TimeSpan.Zero),
            Error: null);

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _utcNow = initial;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class FakeModelRepository : IIdentityMatchModelRepository
    {
        public Task<IReadOnlyList<ReviewIdentityMatchModelRevision>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReviewIdentityMatchModelRevision>>(
                [new ReviewIdentityMatchModelRevision(ModelId, ModelHash, 100)]);
    }

    private sealed class FakeEvidenceReader(ReviewIdentityMatchEvidenceVersion current) : IIdentityMatchEvidenceVersionReader
    {
        public ReviewIdentityMatchEvidenceVersion Current { get; set; } = current;

        public Task<ReviewIdentityMatchEvidenceVersion> ReadAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            CancellationToken cancellationToken = default) => Task.FromResult(Current);
    }

    private sealed class FakePolicyRepository(TimeProvider clock) : IIdentitySuggestionPolicyRepository
    {
        public Task<ReviewIdentitySuggestionPolicy> GetAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewIdentitySuggestionPolicy(
                Version: 1,
                AutoAssignEnabled: false,
                ReviewIdentitySuggestionPolicy.DefaultHighScoreThreshold,
                ReviewIdentitySuggestionPolicy.DefaultHighMarginThreshold,
                ReviewIdentitySuggestionPolicy.DefaultMediumScoreThreshold,
                UpdatedBy: "test",
                UpdatedAtUtc: clock.GetUtcNow()));

        public Task<ReviewIdentitySuggestionPolicy> UpdateAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            bool autoAssignEnabled,
            double highScoreThreshold,
            double highMarginThreshold,
            double mediumScoreThreshold,
            string actor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRunRepository(ReviewIdentityMatchRegenerationRun? latest) : IIdentityMatchRegenerationRepository
    {
        public ReviewIdentityMatchRegenerationRun? Latest { get; private set; } = latest;
        public Func<ReviewIdentityMatchEvidenceVersion>? EvidenceAtStart { get; init; }
        public int StartCount { get; private set; }

        public Task<ReviewIdentityMatchRegenerationRun> StartAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            int policyVersion,
            string requestedBy,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (Latest?.IsActive == true)
            {
                throw new InvalidOperationException("A regeneration run is already active.");
            }

            StartCount++;
            Latest = new ReviewIdentityMatchRegenerationRun(
                Guid.NewGuid(),
                modelId,
                modelHash,
                policyVersion,
                ReviewIdentityMatchRegenerationStatuses.Pending,
                EvidenceAtStart?.Invoke() ?? Baseline,
                TargetCount: 10,
                ProcessedTargetCount: 0,
                SuggestedTargetCount: 0,
                SuggestionCount: 0,
                AutomaticallyAssignedCount: 0,
                ErrorCount: 0,
                requestedBy,
                requestedAtUtc,
                StartedAtUtc: null,
                CompletedAtUtc: null,
                UpdatedAtUtc: requestedAtUtc,
                Error: null);
            return Task.FromResult(Latest);
        }

        public Task<ReviewIdentityMatchRegenerationRun?> GetLatestAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            CancellationToken cancellationToken = default) => Task.FromResult(Latest);

        public Task<ReviewIdentityMatchRegenerationRun?> GetNextActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Latest?.IsActive == true ? Latest : null);

        public Task<ReviewIdentityMatchRegenerationTarget?> ClaimNextTargetAsync(Guid runId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteTargetAsync(Guid runId, FaceOccurrenceId faceOccurrenceId, int suggestionCount, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task FailTargetAsync(Guid runId, FaceOccurrenceId faceOccurrenceId, string error, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteRunAsync(Guid runId, int automaticallyAssignedCount, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkFailedAsync(Guid runId, string error, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> EvidenceStillMatchesAsync(ReviewIdentityMatchRegenerationRun run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
