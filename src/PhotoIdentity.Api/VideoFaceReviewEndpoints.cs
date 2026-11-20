using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

/// <summary>
/// Human review of already-published, revision-bound video-face observations.
/// No media upload, inference, evidence publication or video-person tagging.
/// Disabled unless explicitly enabled with PhotoIdentity:VideoFaceReviewEnabled.
/// </summary>
public static class VideoFaceReviewEndpoints
{
    private const string Actor = "local-maintainer";

    public static IEndpointRouteBuilder MapVideoFaceReviewEndpoints(
        this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/videos/{revisionId}/face-review", GetAsync);
        routes.MapPost("/api/videos/{revisionId}/face-review/{observationId}/decision",
            DecideAsync);
        routes.MapGet("/api/videos/{revisionId}/face-review/{observationId}/native-crop",
            NativeCropAsync);
        routes.MapPost("/api/videos/{revisionId}/face-review/{observationId}/presence",
            PromotePresenceAsync);
        routes.MapDelete("/api/videos/{revisionId}/face-review/{observationId}/presence",
            UndoPresenceAsync);
        return routes;
    }

    public sealed record DecisionRequest(string Decision, string? PersonId);

    private static bool Enabled(IConfiguration settings) =>
        string.Equals(settings["PhotoIdentity:VideoLibraryEnabled"], "true",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(settings["PhotoIdentity:VideoFaceReviewEnabled"], "true",
            StringComparison.OrdinalIgnoreCase);

    // Separate operator approval from review or native crop access.
    // A known-face review decision alone never changes video-person presence.
    private static bool PresenceEnabled(IConfiguration settings) =>
        Enabled(settings) &&
        string.Equals(settings["PhotoIdentity:VideoFacePresencePromotionEnabled"], "true",
            StringComparison.OrdinalIgnoreCase);

    private static bool NativeCropEnabled(IConfiguration settings) =>
        Enabled(settings) &&
        string.Equals(settings["PhotoIdentity:VideoFaceNativeCropEnabled"], "true",
            StringComparison.OrdinalIgnoreCase);

    private static async Task<IResult> NativeCropAsync(
        string revisionId, string observationId,
        VideoFaceNativeCropService crops, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        NoStore(context);
        if (!NativeCropEnabled(settings) ||
            !TryGuid(revisionId, out Guid revision) ||
            !TryGuid(observationId, out Guid observation))
            return Results.NotFound();

        try
        {
            VideoFaceNativeCropResponse? crop = await crops.OpenAsync(
                AssetRevisionId.From(revision), observation, settings, token);
            return crop is null ? Results.NotFound() : new NativeCropResult(crop);
        }
        catch (Exception error) when (error is IOException or
                                      UnauthorizedAccessException or
                                      InvalidOperationException or
                                      ArgumentException or TimeoutException)
        {
            // Private paths, source names, tool output and decoder errors
            // must never escape through an API diagnostic.
            return Results.NotFound();
        }
    }

    private sealed class NativeCropResult(VideoFaceNativeCropResponse crop) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            // Keep source/asset locks until the private JPEG response ends.
            // No range processing or public caching of identity evidence.
            await using (crop)
            {
                await Results.File(crop.Crop.Bytes, "image/jpeg",
                    enableRangeProcessing: false).ExecuteAsync(context);
            }
        }
    }

    public sealed record PresenceRequest(string PersonId);

    private static async Task<IResult> PromotePresenceAsync(
        string revisionId, string observationId, PresenceRequest request,
        PostgresVideoFacePresenceRepository presence,
        IConfiguration settings, HttpContext context, CancellationToken token)
    {
        NoStore(context);
        if (!PresenceEnabled(settings)) return Results.NotFound();
        if (!TryGuid(revisionId, out Guid revision) ||
            !TryGuid(observationId, out Guid observation) ||
            request is null || !TryGuid(request.PersonId, out Guid person))
            return Results.BadRequest(new { error = "Invalid explicit face-presence request." });
        try
        {
            VideoFacePresenceOutcome outcome = await presence.PromoteAsync(
                AssetRevisionId.From(revision), observation, person, Actor, token);
            return Results.Ok(outcome);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException) { return Results.BadRequest(); }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error =
                "The face, person or video presence changed. Refresh before approving." });
        }
    }

    private static async Task<IResult> UndoPresenceAsync(
        string revisionId, string observationId,
        PostgresVideoFacePresenceRepository presence,
        IConfiguration settings, HttpContext context, CancellationToken token)
    {
        NoStore(context);
        if (!PresenceEnabled(settings)) return Results.NotFound();
        if (!TryGuid(revisionId, out Guid revision) ||
            !TryGuid(observationId, out Guid observation))
            return Results.BadRequest(new { error = "Invalid explicit face-presence request." });
        try
        {
            VideoFacePresenceOutcome outcome = await presence.UndoAsync(
                AssetRevisionId.From(revision), observation, Actor, token);
            return Results.Ok(outcome);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException) { return Results.BadRequest(); }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error =
                "The face-to-presence link changed. Refresh before undo." });
        }
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static bool TryGuid(string? text, out Guid id) =>
        Guid.TryParse(text, out id) && id != Guid.Empty;

    private static async Task<IResult> GetAsync(
        string revisionId, PostgresVideoFaceReviewRepository repository,
        IConfiguration settings, HttpContext context, CancellationToken token)
    {
        NoStore(context);
        if (!Enabled(settings)) return Results.NotFound();
        if (!TryGuid(revisionId, out Guid id))
            return Results.BadRequest(new { error = "Invalid video revision." });
        VideoFaceReviewBatch? current = await repository.GetCurrentAsync(
            AssetRevisionId.From(id), token);
        return current is null ? Results.NotFound() :
            Results.Ok(current with { PresencePromotionEnabled = PresenceEnabled(settings) });
    }

    private static async Task<IResult> DecideAsync(
        string revisionId, string observationId, DecisionRequest request,
        PostgresVideoFaceReviewRepository repository,
        IConfiguration settings, HttpContext context, CancellationToken token)
    {
        NoStore(context);
        if (!Enabled(settings)) return Results.NotFound();
        if (!TryGuid(revisionId, out Guid revision) ||
            !TryGuid(observationId, out Guid face) ||
            request is null || request.Decision is null ||
            (request.PersonId is not null &&
                !TryGuid(request.PersonId, out _)))
            return Results.BadRequest(new { error = "Invalid face-review request." });

        try
        {
            Guid? person = request.PersonId is null ? null : Guid.Parse(request.PersonId);
            bool changed = await repository.RecordDecisionAsync(
                AssetRevisionId.From(revision), face,
                request.Decision, person, Actor, token);
            return Results.Ok(new { changed });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "Invalid human review decision." });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Current video face evidence unavailable." });
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error = "The selected person or video has changed." });
        }
    }
}
