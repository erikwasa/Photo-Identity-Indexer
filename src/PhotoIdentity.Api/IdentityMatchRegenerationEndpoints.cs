using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

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
        SqliteIdentityMatchRegenerationModelRepository models,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogueIdentityMatchModelRevision> revisions = await models.ListAsync(cancellationToken);
        return Results.Ok(revisions.Select(model => new
        {
            ModelId = model.ModelId.ToString(),
            ModelHash = model.ModelHash.ToString(),
            model.FaceCount,
        }));
    }

    private static async Task<IResult> GetAsync(
        SqliteIdentityMatchRegenerationRepository repository,
        SqliteIdentitySuggestionPolicyRepository policyRepository,
        SqliteIdentityMatchEvidenceVersionReader evidenceReader,
        string? modelId,
        string? modelHash,
        CancellationToken cancellationToken)
    {
        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact suggestion model revision is required.");
        }

        IdentitySuggestionPolicy policy = await policyRepository.GetAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
        CatalogueIdentityMatchRegenerationRun? run = await repository.GetLatestAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
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
            });
        }

        IdentityMatchEvidenceVersion currentEvidence = await evidenceReader.ReadAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
        IdentityMatchEvidenceVersion expectedEvidence =
            string.Equals(run.Status, IdentityMatchRegenerationStatuses.Completed, StringComparison.Ordinal)
                ? SqliteIdentityMatchEvidenceVersionReader.ExpectedAfterAutomaticAssignments(
                    run.EvidenceVersion,
                    run.AutomaticallyAssignedCount)
                : run.EvidenceVersion;
        bool evidenceMatches = currentEvidence == expectedEvidence;
        bool stale = string.Equals(run.Status, IdentityMatchRegenerationStatuses.Stale, StringComparison.Ordinal)
            || string.Equals(run.Status, IdentityMatchRegenerationStatuses.Failed, StringComparison.Ordinal)
            || !evidenceMatches
            || (!run.IsActive && run.PolicyVersion != policy.Version);

        return Results.Ok(ToResponse(run, stale));
    }

    private static async Task<IResult> StartAsync(
        StartIdentityMatchRegenerationRequest request,
        SqliteIdentityMatchRegenerationRepository repository,
        SqliteIdentitySuggestionPolicyRepository policyRepository,
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

        IdentitySuggestionPolicy policy = await policyRepository.GetAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);

        try
        {
            CatalogueIdentityMatchRegenerationRun run = await repository.StartAsync(
                parsedModelId,
                parsedModelHash,
                policy.Version,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Accepted(value: ToResponse(run, stale: false));
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

    private static object ToResponse(CatalogueIdentityMatchRegenerationRun run, bool stale) => new
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
    };

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

    public sealed record StartIdentityMatchRegenerationRequest(string Actor);
}
