using Microsoft.Extensions.Configuration;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Api;

public static class IdentityMatchRegenerationEndpoints
{
    public static IEndpointRouteBuilder MapIdentityMatchRegenerationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/match-regeneration");
        group.MapGet("/models", ListModelsAsync);
        group.MapGet("", GetAsync);
        group.MapPost("", StartAsync);
        return endpoints;
    }

    private static async Task<IResult> ListModelsAsync(
        IIdentityMatchModelRepository models,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewIdentityMatchModelRevision> revisions = await models.ListAsync(cancellationToken);
        return Results.Ok(revisions.Select(model => new
        {
            ModelId = model.ModelId.ToString(),
            ModelHash = model.ModelHash.ToString(),
            model.FaceCount,
        }));
    }

    private static async Task<IResult> GetAsync(
        IIdentityMatchRegenerationRepository repository,
        IIdentitySuggestionPolicyRepository policyRepository,
        IIdentityMatchEvidenceVersionReader evidenceReader,
        IConfiguration configuration,
        string? modelId,
        string? modelHash,
        CancellationToken cancellationToken)
    {
        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact suggestion model revision is required.");
        }

        ReviewIdentitySuggestionPolicy policy = await policyRepository.GetAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
        ReviewIdentityMatchRegenerationRun? run = await repository.GetLatestAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
        ReviewIdentityMatchEvidenceVersion currentEvidence = await evidenceReader.ReadAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
        IdentityMatchFollowUpConfiguration followUp =
            IdentityMatchFollowUpConfiguration.FromConfiguration(configuration);
        FollowUpSummary followUpSummary = SummarizeFollowUp(run, currentEvidence, followUp);

        if (run is null)
        {
            return Results.Ok(new
            {
                ModelId = parsedModelId.ToString(),
                ModelHash = parsedModelHash.ToString(),
                PolicyVersion = policy.Version,
                Status = "not-run",
                IsActive = false,
                IsStale = true,
                TargetCount = 0,
                ProcessedTargetCount = 0,
                SuggestedTargetCount = 0,
                SuggestionCount = 0,
                AutomaticallyAssignedCount = 0,
                ErrorCount = 0,
                RequestedAtUtc = (DateTimeOffset?)null,
                StartedAtUtc = (DateTimeOffset?)null,
                CompletedAtUtc = (DateTimeOffset?)null,
                UpdatedAtUtc = (DateTimeOffset?)null,
                Error = (string?)null,
                AutomaticFollowUpEnabled = followUp.Enabled,
                AutomaticFollowUpStatus = followUpSummary.Status,
                AutomaticFollowUpQueuedAfterActiveRun = followUpSummary.QueuedAfterActiveRun,
                AutomaticFollowUpDelayMilliseconds = (int)followUp.CoalesceDelay.TotalMilliseconds,
            });
        }

        ReviewIdentityMatchEvidenceVersion expectedEvidence = ExpectedEvidence(run);
        bool evidenceMatches = currentEvidence == expectedEvidence;
        bool stale = string.Equals(run.Status, ReviewIdentityMatchRegenerationStatuses.Stale, StringComparison.Ordinal)
            || string.Equals(run.Status, ReviewIdentityMatchRegenerationStatuses.Failed, StringComparison.Ordinal)
            || !evidenceMatches
            || (!run.IsActive && run.PolicyVersion != policy.Version);

        return Results.Ok(ToResponse(run, stale, followUp, followUpSummary));
    }

    private static async Task<IResult> StartAsync(
        StartIdentityMatchRegenerationRequest request,
        IIdentityMatchRegenerationRepository repository,
        IIdentitySuggestionPolicyRepository policyRepository,
        TimeProvider timeProvider,
        string? modelId,
        string? modelHash,
        CancellationToken cancellationToken)
    {
        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact suggestion model revision is required.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Actor))
        {
            return BadRequest("A regeneration actor is required.");
        }

        ReviewIdentitySuggestionPolicy policy = await policyRepository.GetAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);

        try
        {
            ReviewIdentityMatchRegenerationRun run = await repository.StartAsync(
                parsedModelId,
                parsedModelHash,
                policy.Version,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Accepted(value: ToResponse(
                run,
                stale: false,
                new IdentityMatchFollowUpConfiguration(enabled: false),
                new FollowUpSummary("running", QueuedAfterActiveRun: false)));
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static object ToResponse(
        ReviewIdentityMatchRegenerationRun run,
        bool stale,
        IdentityMatchFollowUpConfiguration followUp,
        FollowUpSummary followUpSummary) => new
    {
        RunId = run.Id,
        ModelId = run.ModelId.ToString(),
        ModelHash = run.ModelHash.ToString(),
        run.PolicyVersion,
        run.Status,
        run.IsActive,
        IsStale = stale,
        run.TargetCount,
        run.ProcessedTargetCount,
        run.SuggestedTargetCount,
        run.SuggestionCount,
        run.AutomaticallyAssignedCount,
        run.ErrorCount,
        run.RequestedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.UpdatedAtUtc,
        run.Error,
        AutomaticFollowUpEnabled = followUp.Enabled,
        AutomaticFollowUpStatus = followUpSummary.Status,
        AutomaticFollowUpQueuedAfterActiveRun = followUpSummary.QueuedAfterActiveRun,
        AutomaticFollowUpDelayMilliseconds = (int)followUp.CoalesceDelay.TotalMilliseconds,
    };

    private static FollowUpSummary SummarizeFollowUp(
        ReviewIdentityMatchRegenerationRun? run,
        ReviewIdentityMatchEvidenceVersion currentEvidence,
        IdentityMatchFollowUpConfiguration configuration)
    {
        if (!configuration.Enabled)
        {
            return new("disabled", QueuedAfterActiveRun: false);
        }

        ReviewIdentityMatchEvidenceVersion expected = run is null
            ? new ReviewIdentityMatchEvidenceVersion(0, 0, 0, 0)
            : ExpectedEvidence(run);
        bool changed = currentEvidence != expected;
        if (run?.IsActive == true)
        {
            return new("running", QueuedAfterActiveRun: changed);
        }

        return new(changed ? "queued" : "current", QueuedAfterActiveRun: false);
    }

    private static ReviewIdentityMatchEvidenceVersion ExpectedEvidence(
        ReviewIdentityMatchRegenerationRun run) =>
        string.Equals(run.Status, ReviewIdentityMatchRegenerationStatuses.Completed, StringComparison.Ordinal)
            ? ReviewIdentityMatchEvidenceVersions.ExpectedAfterAutomaticAssignments(
                run.EvidenceVersion,
                run.AutomaticallyAssignedCount)
            : run.EvidenceVersion;

    private static bool TryModelRevision(
        string? modelId,
        string? modelHash,
        out ModelId parsedModelId,
        out Sha256Digest parsedModelHash)
    {
        parsedModelId = default;
        parsedModelHash = default;
        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(modelHash))
        {
            return false;
        }

        try
        {
            parsedModelId = new ModelId(modelId);
            parsedModelHash = new Sha256Digest(modelHash);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    private sealed record FollowUpSummary(string Status, bool QueuedAfterActiveRun);

    public sealed record StartIdentityMatchRegenerationRequest(string Actor);
}
