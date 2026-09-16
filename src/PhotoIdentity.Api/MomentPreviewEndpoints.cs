using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Api;

public sealed record MomentPreviewMemberResponse(
    string RevisionId,
    DateTime TakenAtLocal,
    string ThumbnailUrl);

public sealed record MomentPreviewMomentResponse(
    string Id,
    DateTime StartedAtLocal,
    DateTime EndedAtLocal,
    int Count,
    MomentPreviewMemberResponse[] Members);

public sealed record MomentPreviewPolicyResponse(
    string PolicyVersion,
    int GapMinutes,
    int MomentCount,
    MomentPreviewMomentResponse[] Moments);

public sealed record MomentPreviewResponse(
    int Offset,
    int Limit,
    int TotalTimestampedPhotos,
    int SampledPhotos,
    bool SampleMayTruncateBoundaryMoments,
    string CaptureTimeSemantics,
    MomentPreviewPolicyResponse[] Policies);

public static class MomentPreviewEndpoints
{
    private const int DefaultLimit = 120;
    private const int MaximumLimit = 200;

    public static IEndpointRouteBuilder MapMomentPreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/moments/preview", PreviewAsync);
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        ISmartCollectionQueryRepository query,
        CancellationToken cancellationToken,
        int offset = 0,
        int limit = DefaultLimit,
        int gapMinutes = 30,
        int comparisonGapMinutes = 90)
    {
        if (offset < 0)
        {
            return Results.BadRequest(new { error = "Offset cannot be negative." });
        }

        if (limit is < 1 or > MaximumLimit)
        {
            return Results.BadRequest(new { error = $"Limit must be between 1 and {MaximumLimit}." });
        }

        if (!TryPolicy(gapMinutes, out PhotoMomentGapPolicy? firstPolicy, out string? firstError))
        {
            return Results.BadRequest(new { error = firstError });
        }

        if (!TryPolicy(comparisonGapMinutes, out PhotoMomentGapPolicy? secondPolicy, out string? secondError))
        {
            return Results.BadRequest(new { error = secondError });
        }

        SmartCollectionFilter timestampedPhotos = new(
            taken: new SmartCollectionDateRange(DateOnly.MinValue, DateOnly.MaxValue));
        SmartCollectionPhotoPage page = await query.QueryAsync(
            timestampedPhotos,
            offset,
            limit,
            cancellationToken);

        PhotoMomentCandidate[] candidates = page.Items
            .Select(photo => new PhotoMomentCandidate(
                photo.RevisionId,
                photo.TakenAtLocal,
                Latitude: photo.Latitude,
                Longitude: photo.Longitude))
            .ToArray();

        PhotoMomentGapPolicy[] policies = [firstPolicy!, secondPolicy!];
        MomentPreviewPolicyResponse[] previews = policies
            .DistinctBy(policy => policy.Version, StringComparer.Ordinal)
            .Select(policy => ToPolicyResponse(
                PhotoMomentClusterer.Cluster(candidates, policy),
                policy))
            .ToArray();

        return Results.Ok(new MomentPreviewResponse(
            page.Offset,
            page.Limit,
            page.Total,
            page.Items.Count,
            page.Offset > 0 || page.Offset + page.Items.Count < page.Total,
            "TakenAtLocal is photographic wall-clock time. Observed/import time is never used as a fallback.",
            previews));
    }

    private static bool TryPolicy(
        int gapMinutes,
        out PhotoMomentGapPolicy? policy,
        out string? error)
    {
        try
        {
            policy = PhotoMomentGapPolicy.CreateTimeGapEvaluation(gapMinutes);
            error = null;
            return true;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            policy = null;
            error = exception.Message;
            return false;
        }
    }

    private static MomentPreviewPolicyResponse ToPolicyResponse(
        PhotoMomentClusteringResult result,
        PhotoMomentGapPolicy policy) => new(
        result.PolicyVersion,
        checked((int)policy.BaseMaximumGap.TotalMinutes),
        result.Moments.Count,
        result.Moments.Select(ToMomentResponse).ToArray());

    private static MomentPreviewMomentResponse ToMomentResponse(PhotoMoment moment) => new(
        moment.Id,
        moment.StartedAtLocal,
        moment.EndedAtLocal,
        moment.Members.Count,
        moment.Members
            .Select(member => new MomentPreviewMemberResponse(
                member.RevisionId.ToString(),
                member.TakenAtLocal,
                $"/api/collections/photos/{member.RevisionId}/thumbnail"))
            .ToArray());
}
