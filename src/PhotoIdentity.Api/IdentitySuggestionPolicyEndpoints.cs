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
        CancellationToken cancellationToken)
    {
        IdentitySuggestionPolicy policy = await repository.GetAsync(cancellationToken);
        return Results.Ok(ToResponse(policy));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateIdentitySuggestionPolicyRequest request,
        SqliteIdentitySuggestionPolicyRepository repository,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Actor))
        {
            return BadRequest("A policy update actor is required.");
        }

        try
        {
            IdentitySuggestionPolicy policy = await repository.UpdateAsync(
                request.AutoAssignEnabled,
                request.HighScoreThreshold,
                request.HighMarginThreshold,
                request.MediumScoreThreshold,
                request.Actor,
                cancellationToken);
            return Results.Ok(ToResponse(policy));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static IdentitySuggestionPolicyResponse ToResponse(IdentitySuggestionPolicy policy) =>
        new(
            policy.Version,
            policy.AutoAssignEnabled,
            policy.HighScoreThreshold,
            policy.HighMarginThreshold,
            policy.MediumScoreThreshold,
            policy.UpdatedBy,
            policy.UpdatedAtUtc);

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });
}
