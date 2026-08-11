using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class IdentitySuggestionPolicyEndpoints
{
    public static IEndpointRouteBuilder MapIdentitySuggestionPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/suggestion-policy");
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        SqliteIdentitySuggestionPolicyRepository repository,
        string? modelId,
        string? modelHash,
        CancellationToken cancellationToken)
    {
        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact suggestion model revision is required.");
        }

        IdentitySuggestionPolicy policy = await repository.GetAsync(
            parsedModelId,
            parsedModelHash,
            cancellationToken);
        return Results.Ok(ToResponse(parsedModelId, parsedModelHash, policy));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateIdentitySuggestionPolicyRequest request,
        SqliteIdentitySuggestionPolicyRepository repository,
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
            return BadRequest("A policy update actor is required.");
        }

        try
        {
            IdentitySuggestionPolicy policy = await repository.UpdateAsync(
                parsedModelId,
                parsedModelHash,
                request.AutoAssignEnabled,
                request.HighScoreThreshold,
                request.HighMarginThreshold,
                request.MediumScoreThreshold,
                request.Actor,
                cancellationToken);
            return Results.Ok(ToResponse(parsedModelId, parsedModelHash, policy));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static IdentitySuggestionPolicyResponse ToResponse(
        ModelId modelId,
        Sha256Digest modelHash,
        IdentitySuggestionPolicy policy) =>
        new(
            modelId.ToString(),
            modelHash.ToString(),
            policy.Version,
            policy.AutoAssignEnabled,
            policy.HighScoreThreshold,
            policy.HighMarginThreshold,
            policy.MediumScoreThreshold,
            policy.UpdatedBy,
            policy.UpdatedAtUtc);

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
}
