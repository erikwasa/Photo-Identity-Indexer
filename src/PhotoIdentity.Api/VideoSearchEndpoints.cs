using System.Diagnostics;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

public sealed record VideoSearchRequest(string Query, string? Mode = null, int Limit = 40);

public sealed record VideoSearchResultResponse(
    string RevisionId, string PosterUrl, long TimestampMilliseconds,
    string Caption, double CombinedScore, double? SemanticScore,
    double? CaptionScore, string[] Sources, string EvidenceKind,
    string SamplingPolicyVersion, string ClipModelId,
    string CaptionModelId, string CaptionPromptVersion);

public sealed record VideoSearchResponse(
    string Query, string Mode, bool SemanticModelAvailable,
    int CandidateVideos, bool CandidateLimitReached,
    double SearchMilliseconds, VideoSearchResultResponse[] Items);

/// <summary>
/// Separate, explicit video search API. Photo search routes and saved photo
/// result membership are untouched. This does not start any video processing.
/// </summary>
public static class VideoSearchEndpoints
{
    public static IEndpointRouteBuilder MapVideoSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/video-search/query", QueryAsync);
        return endpoints;
    }

    private static async Task<IResult> QueryAsync(
        VideoSearchRequest request, IConfiguration settings,
        PostgresVideoSearchRepository videos, PhotoSemanticSearchModel model,
        HttpContext context, CancellationToken token)
    {
        // Read-only search results and their private captions are never cached.
        context.Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (!string.Equals(settings["PhotoIdentity:VideoLibraryEnabled"], "true",
                StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
            if (request.Query.Trim().Length > 300 || request.Limit is < 1 or > 200)
                return Results.BadRequest(new { error = "Invalid video search bound." });
            string mode = PhotoSearchModes.Normalize(request.Mode);
            long started = Stopwatch.GetTimestamp();

            PhotoSemanticSearchModelDescriptor? descriptor = null;
            IReadOnlyList<float>? vector = null;
            if (mode != PhotoSearchModes.Caption)
            {
                descriptor = await model.GetDescriptorAsync(token);
                if (descriptor is not null)
                    vector = (await model.EncodeTextAsync(request.Query.Trim(), token))?.Vector;
            }

            IReadOnlyList<VideoSearchCandidate> candidates =
                await videos.ReadCurrentAsync(token);
            IReadOnlyList<VideoSearchHit> hits = VideoSearchRanker.Rank(
                candidates, request.Query, mode, request.Limit, vector,
                descriptor?.ModelId, descriptor?.ModelSha256,
                descriptor?.PreprocessingVersion);
            return Results.Ok(new VideoSearchResponse(
                request.Query.Trim(), mode, descriptor is not null,
                candidates.Count,
                candidates.Count == PostgresVideoSearchRepository.MaximumCandidateVideos,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                hits.Select(hit => new VideoSearchResultResponse(
                    hit.RevisionId.ToString("D"),
                    $"/api/videos/{hit.RevisionId:D}/poster",
                    hit.TimestampMilliseconds, hit.Caption,
                    hit.CombinedScore, hit.SemanticScore, hit.CaptionScore,
                    hit.Sources, "generated-frame-evidence",
                    hit.SamplingPolicyVersion, hit.ClipModelId,
                    hit.CaptionModelId, hit.CaptionPromptVersion)).ToArray()));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
