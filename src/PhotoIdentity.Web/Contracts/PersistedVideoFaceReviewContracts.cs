namespace PhotoIdentity.Web.Contracts;

/// <summary>
/// Browser DTOs for the existing, default-off, revision-scoped production face
/// review API. These contain geometry and suggestions, never source paths,
/// pixels, embeddings or an automatically accepted video-person association.
/// </summary>
public sealed record PersistedVideoFaceSuggestion(
    string PersonId, string DisplayName, int Rank, double Similarity, bool Selectable);

public sealed record PersistedVideoFacePresence(string PersonId, bool VideoTagStillCurrent);

public sealed record PersistedVideoFaceObservation(
    string Id, int FrameOrdinal, long TimestampMs, int FaceOrdinal,
    int FrameWidth, int FrameHeight, double X, double Y, double Width, double Height,
    IReadOnlyList<PersistedVideoFaceSuggestion> Suggestions,
    string? Decision, string? ReviewedPersonId, string? ReviewedPersonName,
    PersistedVideoFacePresence? Presence = null);

public sealed record PersistedVideoFaceBatch(
    string RevisionId, string BatchId, string ProducerVersion,
    string DetectorModelId, string SFaceModelId, string AlignmentProtocol,
    IReadOnlyList<PersistedVideoFaceObservation> Observations,
    bool PresencePromotionEnabled = false);

public sealed record PersistedVideoFaceDecisionRequest(string Decision, string? PersonId);
public sealed record PersistedVideoFacePresenceRequest(string PersonId);
public sealed record PersistedVideoFacePresenceOutcome(string Status, bool VideoPersonTagChanged);

/// <summary>
/// Pure, deterministic presence-first presentation. A confirmed identity may
/// appear in many observations but counts once. No face count establishes
/// whole-video coverage; zero observations are a valid sampled result.
/// </summary>
public static class VideoFaceReviewPresentation
{
    public static bool IsReviewed(PersistedVideoFaceObservation face) =>
        face.Decision is "known" or "unknown" or "not-face" or "cannot-tell";

    public static IReadOnlyList<PersistedVideoFaceObservation> Visible(
        IReadOnlyList<PersistedVideoFaceObservation> observations, bool showReviewed) =>
        showReviewed ? observations : observations.Where(face => !IsReviewed(face)).ToArray();

    public static int DistinctConfirmedPeople(IReadOnlyList<PersistedVideoFaceObservation> observations) =>
        observations.Where(face => face.Decision == "known" &&
                                   !string.IsNullOrWhiteSpace(face.ReviewedPersonId))
            .Select(face => face.ReviewedPersonId!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public static string Moment(long milliseconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0, 86_400_000));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss\.fff")
                                   : time.ToString(@"m\:ss\.fff");
    }
}
