using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record PhotoPresentationPreferenceMutationRequest(string Preference);

public sealed record PhotoPresentationPreferenceActionResponse(
    long Id,
    string ActionKind,
    string? Preference,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record PhotoPresentationPreferenceResponse(
    string RevisionId,
    string? Preference,
    PhotoPresentationPreferenceActionResponse[] History);

public static class PhotoPresentationPreferenceEndpoints
{
    private const string LocalMaintainerActor = "local-maintainer";

    public static IEndpointRouteBuilder MapPhotoPresentationPreferenceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/collections/photos/{revisionId}/presentation-preference",
            GetAsync);
        endpoints.MapPut(
            "/api/collections/photos/{revisionId}/presentation-preference",
            SetAsync);
        endpoints.MapDelete(
            "/api/collections/photos/{revisionId}/presentation-preference",
            ClearAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        string revisionId,
        IPhotoPresentationPreferenceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsed))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        try
        {
            return Results.Ok(ToResponse(
                await repository.GetStateAsync(parsed, cancellationToken)));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SetAsync(
        string revisionId,
        PhotoPresentationPreferenceMutationRequest request,
        IPhotoPresentationPreferenceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsed))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        try
        {
            return Results.Ok(ToResponse(await repository.SetAsync(
                parsed,
                request.Preference,
                LocalMaintainerActor,
                cancellationToken)));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ClearAsync(
        string revisionId,
        IPhotoPresentationPreferenceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsed))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        try
        {
            return Results.Ok(ToResponse(await repository.ClearAsync(
                parsed,
                LocalMaintainerActor,
                cancellationToken)));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static PhotoPresentationPreferenceResponse ToResponse(
        PhotoPresentationPreferenceState state) => new(
        state.RevisionId.ToString(),
        state.Preference,
        state.History.Select(action => new PhotoPresentationPreferenceActionResponse(
            action.Id,
            action.ActionKind,
            action.Preference,
            action.Actor,
            action.CreatedAtUtc)).ToArray());

    private static bool TryParseRevisionId(string value, out AssetRevisionId revisionId)
    {
        if (Guid.TryParse(value, out Guid guid) && guid != Guid.Empty)
        {
            revisionId = AssetRevisionId.From(guid);
            return true;
        }

        revisionId = default;
        return false;
    }
}
